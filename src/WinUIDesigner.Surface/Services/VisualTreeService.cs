// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Publishes the live WinUI visual tree and maps runtime objects back to their source XAML.
/// </summary>
internal sealed class VisualTreeService(ProtocolHandler protocolHandler, ObjectIdentityRegistry objectIdentity)
{
    private const string _XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly Dictionary<long, VisualTreeTopologyEntry[]> _publishedTopologies = [];

    public void RegisterSourceInfo(CreateDocumentInfo? document, string? preparedXaml, FrameworkElement root)
    {
        if (document is null || string.IsNullOrWhiteSpace(document.DesignTimeSurfaceUri) || string.IsNullOrWhiteSpace(preparedXaml))
        {
            return;
        }

        try
        {
            XDocument xamlDocument = XDocument.Parse(preparedXaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            if (xamlDocument.Root is null)
            {
                return;
            }

            var markupElements = new List<XElement>();
            foreach (XElement element in xamlDocument.Root.DescendantsAndSelf())
            {
                if (!element.Name.LocalName.Contains('.', StringComparison.Ordinal)
                    && !element.Ancestors().Any(parent => parent.Name.LocalName is "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate"))
                {
                    markupElements.Add(element);
                }
            }

            var runtimeElements = new List<DependencyObject>();
            AppendAuthoredElements(root, runtimeElements);
            var usedMarkup = new HashSet<XElement>();
            foreach (DependencyObject runtimeElement in runtimeElements)
            {
                XElement? markupElement = null;
                string? runtimeName = (runtimeElement as FrameworkElement)?.Name;
                XNamespace x = _XamlNamespace;
                if (!string.IsNullOrEmpty(runtimeName))
                {
                    foreach (XElement candidate in markupElements)
                    {
                        if (usedMarkup.Contains(candidate) || !MatchesMarkupType(candidate, runtimeElement.GetType()))
                        {
                            continue;
                        }

                        string? candidateName = (string?)candidate.Attribute(x + "Name") ?? (string?)candidate.Attribute("Name");
                        if (string.Equals(candidateName, runtimeName, StringComparison.Ordinal))
                        {
                            markupElement = candidate;
                            break;
                        }
                    }
                }

                if (markupElement is null)
                {
                    foreach (XElement candidate in markupElements)
                    {
                        if (!usedMarkup.Contains(candidate) && MatchesMarkupType(candidate, runtimeElement.GetType()))
                        {
                            markupElement = candidate;
                            break;
                        }
                    }
                }

                if (markupElement is null || markupElement is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
                {
                    continue;
                }

                usedMarkup.Add(markupElement);
                objectIdentity.RegisterSourceInfo(runtimeElement, new SourceInfo
                {
                    FileName = document.DesignTimeSurfaceUri,
                    LineNumber = (uint)lineInfo.LineNumber,
                    ColumnNumber = (uint)lineInfo.LinePosition,
                });
            }

        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Surface", $"Prepared XAML source mapping failed for document {document.DocumentId}.", ex);
        }
    }

    public void PublishMutation(FrameworkElement root, VisualMutationType mutationType)
    {
        long rootHandle = objectIdentity.GetHandle(root);
        if (mutationType == VisualMutationType.Remove)
        {
            protocolHandler.PostMessage((int)DesignerMessageId.VisualTreeMutations, new MutationList
            {
                Mutations =
                [
                    CreateMutation(root, rootHandle, 0, 0, isRoot: true, mutationType),
                ],
            });
            _publishedTopologies.Remove(rootHandle);
            return;
        }

        var mutations = new List<VisualTreeMutationEvent>();
        AppendMutations(root, rootHandle, 0, 0, isRoot: true, mutations);

        protocolHandler.PostMessage((int)DesignerMessageId.VisualTreeMutations, new MutationList
        {
            Mutations = mutations,
        });

        _publishedTopologies[rootHandle] = CaptureTopology(root);
    }

    public void PublishMutationIfChanged(FrameworkElement root)
    {
        long rootHandle = objectIdentity.GetHandle(root);
        VisualTreeTopologyEntry[] currentTopology = CaptureTopology(root);

        if (_publishedTopologies.TryGetValue(rootHandle, out VisualTreeTopologyEntry[]? publishedTopology)
            && publishedTopology.AsSpan().SequenceEqual(currentTopology))
        {
            return;
        }

        if (publishedTopology is not null)
        {
            // A full Add snapshot relocates surviving children, but does not remove
            // deleted nodes in LiveNodeTreeBase. Explicitly remove the vanished roots.
            var retained = currentTopology.Select(entry => entry.Handle).ToHashSet();
            var removed = publishedTopology.Where(entry => !retained.Contains(entry.Handle)).ToArray();
            var removedHandles = removed.Select(entry => entry.Handle).ToHashSet();
            var mutations = removed.Where(entry => !removedHandles.Contains(entry.ParentHandle))
                .Select(entry => new VisualTreeMutationEvent
                {
                    Element = new VisualElement { Handle = entry.Handle },
                    Relation = new ParentChildRelation { Parent = entry.ParentHandle, Child = entry.Handle, ChildIndex = entry.ChildIndex },
                    VisualMutationType = VisualMutationType.Remove,
                }).ToList();
            if (mutations.Count != 0)
            {
                protocolHandler.PostMessage((int)DesignerMessageId.VisualTreeMutations, new MutationList { Mutations = mutations });
            }
            // Keep detached object handles alive until document close: Undo can
            // reconnect an existing proxy. Visual-tree deletion is not object release.
        }

        PublishMutation(root, VisualMutationType.Add);
    }

    public void RemoveIdentities(DependencyObject element)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            RemoveIdentities(VisualTreeHelper.GetChild(element, index));
        }

        objectIdentity.RemoveObject(element);
    }

    public void Clear()
    {
        _publishedTopologies.Clear();
    }

    private VisualTreeTopologyEntry[] CaptureTopology(DependencyObject root)
    {
        // Compare parent/child order and stable handles to avoid resending an unchanged tree on every idle.
        var topology = new List<VisualTreeTopologyEntry>();
        AppendTopology(root, parentHandle: 0, childIndex: 0, topology);
        return topology.ToArray();
    }

    private void AppendTopology(DependencyObject element, long parentHandle, uint childIndex, List<VisualTreeTopologyEntry> topology)
    {
        long handle = objectIdentity.GetHandle(element);
        topology.Add(new VisualTreeTopologyEntry(handle, parentHandle, childIndex));

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            AppendTopology(
                VisualTreeHelper.GetChild(element, index),
                handle,
                (uint)index,
                topology);
        }
    }

    private void AppendMutations(DependencyObject element, long handle, long parentHandle, uint childIndex, bool isRoot, List<VisualTreeMutationEvent> mutations)
    {
        // The designer needs each element and its ordered parent relation to rebuild
        // its selectable tree from this out-of-process visual tree.
        mutations.Add(CreateMutation(element, handle, parentHandle, childIndex, isRoot, VisualMutationType.Add));

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, index);
            long childHandle = objectIdentity.GetHandle(child);
            AppendMutations(child, childHandle, handle, (uint)index, isRoot: false, mutations);
        }
    }

    private VisualTreeMutationEvent CreateMutation(DependencyObject element, long handle, long parentHandle, uint childIndex, bool isRoot, VisualMutationType mutationType)
    {
        return new VisualTreeMutationEvent
        {
            Element = new VisualElement
            {
                Handle = handle,
                Type = element.GetType().FullName,
                Name = (element as FrameworkElement)?.Name,
                SourceInfo = objectIdentity.GetSourceInfo(element),
                IsRoot = isRoot,
            },
            Relation = new ParentChildRelation
            {
                Parent = parentHandle,
                Child = handle,
                ChildIndex = childIndex,
            },
            VisualMutationType = mutationType,
        };
    }

    private static void AppendAuthoredElements(DependencyObject element, List<DependencyObject> elements, HashSet<object>? visited = null)
    {
        visited ??= [with(ReferenceEqualityComparer.Instance)];
        if (!visited.Add(element))
        {
            return;
        }

        elements.Add(element);

        // Walk the object graph produced from source XAML instead of the rendered
        // visual tree. ControlTemplate implementation elements have no authored node
        // and could otherwise claim the source position of an authored control.
        foreach (string propertyName in new[] { "Resources", "MergedDictionaries", "Setters", "Value", "Children", "Content", "Child", "Items" })
        {
            System.Reflection.PropertyInfo? property = element.GetType().GetProperty(propertyName);
            if (property?.CanRead != true)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(element);
            }
            catch
            {
                continue;
            }

            if (value is DependencyObject child)
            {
                AppendAuthoredElements(child, elements, visited);
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (object? item in enumerable)
                {
                    object? authoredValue = item is not null && item.GetType().IsGenericType && item.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                        ? item.GetType().GetProperty("Value")?.GetValue(item) : item;
                    if (authoredValue is DependencyObject dependencyObject)
                    {
                        AppendAuthoredElements(dependencyObject, elements, visited);
                    }
                }
            }
        }
    }

    private static bool MatchesMarkupType(XElement element, Type runtimeType)
    {
        if (element.Name.NamespaceName.StartsWith("using:", StringComparison.Ordinal))
        {
            return string.Equals(element.Name.NamespaceName[6..] + "." + element.Name.LocalName, runtimeType.FullName, StringComparison.Ordinal);
        }

        if (element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return string.Equals(element.Name.NamespaceName[14..].Split(';')[0] + "." + element.Name.LocalName, runtimeType.FullName, StringComparison.Ordinal);
        }

        return element.Name.LocalName == runtimeType.Name && runtimeType.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) == true;
    }
}
