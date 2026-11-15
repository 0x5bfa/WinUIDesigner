// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    /// <summary>
    /// The environment variable that turns <see cref="_ReprepareRawPlatformElements"/> on when it is set
    /// to <c>1</c> or <c>true</c> before Visual Studio starts.
    /// </summary>
    internal const string ReprepareRawPlatformElementsVariable = "WINUIDESIGNER_REPREPARE_RAW_PLATFORM_ELEMENTS";

    /// <summary>
    /// Whether a document with elements under an Uno platform prefix is cleaned a second time, so that those
    /// elements reach the surface in the prepared XAML and the namespace policies decide about them there.
    /// </summary>
    /// <remarks>
    /// Off by default: loading a document this way was confirmed in Visual Studio 18.13, but editing it
    /// afterwards and Visual Studio 17.14 have not been checked. While it is off, such a document is built
    /// from construction actions, which are generated from a document model that leaves out the members of
    /// an ignorable namespace nothing resolves.
    /// </remarks>
    private static readonly bool _ReprepareRawPlatformElements = IsSwitchOn(ReprepareRawPlatformElementsVariable);

    // The documents whose prepared XAML lost its platform elements. Only touched while a request is created.
    private readonly HashSet<int> _documentsBuiltFromActions = [];

    /// <summary>
    /// Prepares the Uno platform the designer previews before a document is sent to the surface.
    /// </summary>
    private Task PreparePlatformPrefixesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        // The designer previews the Windows flavor of the markup. Previewing another platform means setting
        // another target here and in the surface; the policies themselves do not change.
        _memberDispositions.TargetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;

        // The request can carry App.xaml and the design-time resources as well; they were not necessarily
        // prepared together with the target, so they are looked at on their own.
        var seen = new HashSet<int>();
        PreparePlatformElements(targetDocument, seen);
        for (int index = 0; index < preparedDocuments.Count; index++)
        {
            PreparePlatformElements(preparedDocuments[index], seen);
        }

        if (MarkupProvider is WinUISurfaceProcessMarkupProvider provider)
        {
            PreparePlatformElements(provider.AppXaml, seen);
            PreparePlatformElements(provider.DesignTimeResources, seen);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns whether <paramref name="document"/> has to be built from construction actions because its
    /// prepared XAML no longer contains the elements that were written under an Uno platform prefix.
    /// </summary>
    private bool PlatformPrefixesRequireConstructionActions(ISurfaceProcessDocument? document)
    {
        return document is not null && _documentsBuiltFromActions.Contains(document.DocumentId);
    }

    /// <summary>
    /// Decides how a document with elements under an Uno platform prefix is built.
    /// A failure is logged and leaves the document to be built as it would be without platform prefixes.
    /// </summary>
    private void PreparePlatformElements(ISurfaceProcessDocument? document, HashSet<int> seen)
    {
        if (document is null || !seen.Add(document.DocumentId))
        {
            return;
        }

        int documentId = document.DocumentId;
        try
        {
            // The scan is cached per text version, and a document without Uno namespaces ends here.
            XamlNamespaceScanResult namespaces = _memberDispositions.GetNamespaces(document.Document);
            if (!namespaces.HasPlatformNamespaces)
            {
                _documentsBuiltFromActions.Remove(documentId);
                return;
            }

            string? path = document.Document?.DocumentContext?.DocumentPath;
            ReportPrefixesMissingFromIgnorable(namespaces, path);

            // Attributes and property elements under a platform prefix pass through the XAML cleaner as they
            // are written. Object elements do not: the cleaner cannot resolve their type and replaces each of
            // them with a fallback control without its children.
            if (!namespaces.HasPlatformPrefixedObjectElements)
            {
                _documentsBuiltFromActions.Remove(documentId);
                return;
            }

            if (_ReprepareRawPlatformElements && TryReprepareRawPlatformElements(document))
            {
                _documentsBuiltFromActions.Remove(documentId);
                WinUIDesignerLogger.LogTrace("Platform", $"Document {documentId} ({path}) was prepared again to keep its Uno platform elements.");
                return;
            }

            _documentsBuiltFromActions.Add(documentId);
            WinUIDesignerLogger.LogTrace("Platform", $"Document {documentId} ({path}) has elements under an Uno platform prefix; it is built from construction actions.");
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Platform", $"Preparing the Uno platform prefixes of document {documentId} failed; it is built as prepared.", ex);
        }
    }

    private bool TryReprepareRawPlatformElements(ISurfaceProcessDocument document)
    {
        try
        {
            return MarkupProvider is WinUISurfaceProcessMarkupProvider provider && provider.ReprepareRawPlatformElements(document);
        }
        catch (Exception ex)
        {
            // The document is as Visual Studio prepared it, or about to be built from construction actions anyway.
            WinUIDesignerLogger.LogWarning("Platform", $"Preparing document {document.DocumentId} again failed; it is built from construction actions.", ex);
            return false;
        }
    }

    private static void ReportPrefixesMissingFromIgnorable(XamlNamespaceScanResult namespaces, string? path)
    {
        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(namespaces);
        for (int index = 0; index < missing.Count; index++)
        {
            XamlNamespaceDeclaration declaration = missing[index];
            string consequence = UnoPlatformPrefixes.TryParse(declaration.Prefix, out _, out _)
                ? "The designer applies the platform prefix regardless."
                : "Its members are left as written for the XAML reader to reject.";
            WinUIDesignerLogger.LogWarning(
                "Platform",
                $"{path}({declaration.LineNumber},{declaration.LinePosition}): xmlns:{declaration.Prefix}=\"{declaration.Uri.Original}\" is an Uno platform namespace that is not listed in mc:Ignorable, " +
                $"so the Windows XAML compiler cannot resolve it. {consequence}");
        }
    }

    private static bool IsSwitchOn(string variable)
    {
        try
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return string.Equals(value, "1", StringComparison.Ordinal) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Without access to the environment the switch stays off.
            return false;
        }
    }
}
