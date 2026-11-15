// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Merges the resource dictionaries that documents name through <c>ResourceDictionary.Source</c>.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio prepares every dictionary a document names as its source, writes the prepared copy into its
/// shadow cache, the folder this process reads documents from, and replaces the source with the design-time URI of
/// the copy: as the attribute <c>DT.ResourceDictionarySource</c> of <c>using:XSurfUwp</c> in prepared XAML, and as
/// the property <c>ResourceDictionarySource:XSurfUwp.DT</c> in construction actions. Microsoft's UWP surface hands
/// that value to a native function of its own. This surface reads the copy, applies the namespace policies to it
/// and merges the result into the dictionary that carried the source.
/// </para>
/// <para>
/// In prepared XAML the content is inserted into the document before the XAML reader sees it, so the document and
/// the dictionaries load in one pass. On the construction-action path the dictionary is loaded on its own when Visual
/// Studio sets the source, and merged into the dictionary object Visual Studio created.
/// </para>
/// </remarks>
internal sealed partial class SurfaceService
{
    // What was merged into each dictionary whose source a construction action set; the keys die with the dictionaries.
    private readonly ConditionalWeakTable<ResourceDictionary, AppliedResourceDictionarySource> _appliedResourceDictionarySources = new();

    // The problems reported while the current request is handled: App.xaml is read twice, for instance.
    private readonly HashSet<string> _reportedResourceDictionaryProblems = new(StringComparer.Ordinal);

    private ResourceDictionarySourceExpander? _resourceDictionarySources;

    // The document whose construction actions are being executed, for messages.
    private int _actionDocumentId;

    private ResourceDictionarySourceExpander ResourceDictionarySources => _resourceDictionarySources ??= new(new ResourceDictionarySourceOptions
    {
        ContentDirectory = Program.ContentDirectory,
        Parse = LoadPreparedXaml,
        IsDictionaryType = IsResourceDictionaryType,
        LoadWithRuntime = uri =>
        {
            LoadDictionaryWithRuntime(uri, out string? error);
            return error;
        },
        ReportWarning = ReportResourceDictionaryProblem,
        Report = message => WinUIDesignerLogger.LogTrace("Surface", message),
    });

    /// <summary>
    /// Starts the reports of a new request: each problem is reported once per request.
    /// </summary>
    private void BeginResourceDictionaryReports()
    {
        _reportedResourceDictionaryProblems.Clear();
    }

    /// <summary>
    /// Names the document whose construction actions run until the scope ends, for the messages about its dictionaries.
    /// </summary>
    private DocumentScope EnterActionDocument(int documentId)
    {
        int previous = _actionDocumentId;
        _actionDocumentId = documentId;
        return new DocumentScope(() => _actionDocumentId = previous);
    }

    /// <summary>
    /// Merges into the prepared XAML of a document the dictionaries that its dictionaries name as their source.
    /// </summary>
    /// <param name="root">The element of the prepared XAML that is going to be loaded.</param>
    /// <param name="document">The document the XAML belongs to.</param>
    /// <remarks>
    /// Runs before the design-time attributes are removed, which would remove the sources too. A dictionary that
    /// cannot be merged is reported and stays empty; nothing here fails the document.
    /// </remarks>
    private void MergeResourceDictionarySources(XElement root, CreateDocumentInfo document)
    {
        try
        {
            ResourceDictionaryExpansion expansion = ResourceDictionarySources.Expand(root, document.DesignTimeSurfaceUri, $"document {document.DocumentId}");
            if (expansion.Total != 0)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Resource dictionary sources of document {document.DocumentId}: merged {expansion.Prepared} from Visual Studio's prepared copies and {expansion.Runtime} by URI; left {expansion.Failed} empty.");
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Surface", $"Merging the resource dictionaries of document {document.DocumentId} failed; the dictionaries not merged by then are left empty.", ex);
        }
    }

    /// <summary>
    /// Handles a construction action that sets the source of a dictionary: merges the dictionary it names.
    /// </summary>
    /// <param name="owner">The object the property is set on.</param>
    /// <param name="value">The value of the property, the source as Visual Studio passes it on.</param>
    private void SetResourceDictionarySource(object owner, object? value)
    {
        if (owner is not ResourceDictionary target)
        {
            WinUIDesignerLogger.LogWarning("Surface", $"A construction action sets a resource dictionary source on {owner.GetType().FullName}, which is no resource dictionary; the action is ignored.");
            return;
        }

        // A dictionary whose source changes loses what the old source brought.
        RemoveResourceDictionarySourceContent(target);

        string? source = value as string ?? value?.ToString();
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        ResourceDictionary? content = LoadResourceDictionarySource(source, DescribeActionDocument());
        if (content is null)
        {
            return;
        }

        // Last: the last merged dictionary is looked up first, as the entries of a source are at run time.
        target.MergedDictionaries.Add(content);
        _appliedResourceDictionarySources.AddOrUpdate(target, new AppliedResourceDictionarySource(source, content));
    }

    /// <summary>
    /// Handles a construction action that clears the source of a dictionary.
    /// </summary>
    private void ClearResourceDictionarySource(object owner)
    {
        if (owner is ResourceDictionary target)
        {
            RemoveResourceDictionarySourceContent(target);
        }
    }

    /// <summary>
    /// Returns the source a construction action set on a dictionary.
    /// </summary>
    private object? GetResourceDictionarySource(object owner)
    {
        return owner is ResourceDictionary target && _appliedResourceDictionarySources.TryGetValue(target, out AppliedResourceDictionarySource? applied)
            ? applied.Source
            : null;
    }

    /// <summary>
    /// Merges again what a source brought after a construction action cleared the merged dictionaries of its
    /// dictionary; Visual Studio does not know about that entry and clears it with the others.
    /// </summary>
    private void RestoreResourceDictionarySourceContent(object owner, string fullPropertyName)
    {
        if (owner is not ResourceDictionary target
            || !_appliedResourceDictionarySources.TryGetValue(target, out AppliedResourceDictionarySource? applied))
        {
            return;
        }

        int separator = fullPropertyName.IndexOf(':');
        string propertyName = separator < 0 ? fullPropertyName : fullPropertyName[..separator];
        if (propertyName == nameof(ResourceDictionary.MergedDictionaries) && !target.MergedDictionaries.Contains(applied.Dictionary))
        {
            target.MergedDictionaries.Add(applied.Dictionary);
        }
    }

    private void RemoveResourceDictionarySourceContent(ResourceDictionary target)
    {
        if (_appliedResourceDictionarySources.TryGetValue(target, out AppliedResourceDictionarySource? applied))
        {
            target.MergedDictionaries.Remove(applied.Dictionary);
            _appliedResourceDictionarySources.Remove(target);
        }
    }

    /// <summary>
    /// Loads the dictionary a source names, on its own. It sees what is in the application's resources at this
    /// moment, as a dictionary loaded through <c>Source</c> does at run time.
    /// </summary>
    /// <returns>The dictionary, or <see langword="null"/> when it cannot be loaded; that has been reported.</returns>
    private ResourceDictionary? LoadResourceDictionarySource(string source, string owner)
    {
        ResourceDictionarySourceContent content = ResourceDictionarySources.Load(source, documentUri: null, owner);
        switch (content.Kind)
        {
            case ResourceDictionarySourceContentKind.Prepared:
                {
                    string name = ResourceDictionarySourceUri.GetDisplayName(content.Uri!);
                    XElement dictionary = content.Dictionary!;
                    PrepareForXamlReader(dictionary);
                    try
                    {
                        if (XamlReader.Load(dictionary.ToString(SaveOptions.DisableFormatting)) is ResourceDictionary loaded)
                        {
                            WinUIDesignerLogger.LogTrace("Surface", $"Resource dictionary '{name}' merged by {owner} was loaded from Visual Studio's prepared copy.");
                            return loaded;
                        }

                        ReportResourceDictionaryProblem($"Resource dictionary '{name}' merged by {owner} is left empty: its prepared copy does not load as a resource dictionary.");
                    }
                    catch (Exception ex)
                    {
                        ReportResourceDictionaryProblem($"Resource dictionary '{name}' merged by {owner} is left empty: the XAML reader rejects its prepared copy: {DescribeException(ex)}");
                    }

                    return null;
                }

            case ResourceDictionarySourceContentKind.Runtime:
                {
                    ResourceDictionary? loaded = LoadDictionaryWithRuntime(content.Uri!, out string? error);
                    if (loaded is null)
                    {
                        ResourceDictionarySources.ReportRuntimeLoadFailure(content.Uri!, owner, error ?? "unknown error");
                        return null;
                    }

                    WinUIDesignerLogger.LogTrace("Surface", $"Resource dictionary '{content.Uri}' merged by {owner} was loaded by the WinUI runtime.");
                    return loaded;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Asks the WinUI runtime to load a dictionary from its URI, as a <c>Source</c> outside the solution, such as a
    /// dictionary compiled into a package, is loaded at run time. The project's staged PRI files take part in the
    /// lookup through the resource manager of the surface.
    /// </summary>
    private static ResourceDictionary? LoadDictionaryWithRuntime(string uri, out string? error)
    {
        try
        {
            var dictionary = new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) };
            error = null;
            return dictionary;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            return null;
        }
    }

    /// <summary>
    /// Returns whether an element name that is not one of WinUI's dictionary types names a type that derives from
    /// <see cref="ResourceDictionary"/>, such as a dictionary class of the project.
    /// </summary>
    private static bool IsResourceDictionaryType(XName name)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(name.NamespaceName);
        string? clrNamespace = uri.UsingNamespaces.Count != 0
            ? uri.UsingNamespaces[0]
            : uri.BaseUri.StartsWith(XamlNamespaceUri.UsingScheme, StringComparison.Ordinal)
                ? uri.BaseUri[XamlNamespaceUri.UsingScheme.Length..]
                : null;
        if (string.IsNullOrEmpty(clrNamespace))
        {
            return false;
        }

        Type? type = XamlRuntimeUtilities.ResolveType(clrNamespace + "." + name.LocalName);
        return type is not null && typeof(ResourceDictionary).IsAssignableFrom(type);
    }

    private void ReportResourceDictionaryProblem(string message)
    {
        if (_reportedResourceDictionaryProblems.Add(message))
        {
            WinUIDesignerLogger.LogWarning("Surface", message);
        }
        else
        {
            WinUIDesignerLogger.LogTrace("Surface", "Reported before in this request: " + message);
        }
    }

    private string DescribeActionDocument()
    {
        return _actionDocumentId != 0 ? $"document {_actionDocumentId}" : "the document being edited";
    }

    private static string DescribeException(Exception exception)
    {
        // WinUI's messages span several lines; one line per problem keeps the log readable.
        return string.Join(" ", exception.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// What a construction action merged into a dictionary for its source.
    /// </summary>
    private sealed record AppliedResourceDictionarySource(string Source, ResourceDictionary Dictionary);
}
