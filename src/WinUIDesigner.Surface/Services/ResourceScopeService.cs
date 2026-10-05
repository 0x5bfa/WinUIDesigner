// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.Surface.Services;

internal sealed class ResourceScopeService(ObjectIdentityRegistry identity)
{
    private readonly Dictionary<object, object> parents = new(ReferenceEqualityComparer.Instance);

    private string? themeAssembly;
    private string? themePath;
    private ResourceDictionary? themeResources;

    public void SetParent(object child, object? parent)
    {
        if (parent is null)
        {
            parents.Remove(child);
            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { child };
        for (object? current = parent; current is not null; current = parents.GetValueOrDefault(current))
        {
            if (!visited.Add(current))
            {
                throw new InvalidOperationException("The resource search scope contains a cycle.");
            }
        }

        parents[child] = parent;
    }

    public void SetThemeScope(string assembly, string relativePath)
    {
        if (themeAssembly == assembly && themePath == relativePath)
        {
            return;
        }

        themeAssembly = assembly;
        themePath = relativePath;
        themeResources = string.IsNullOrEmpty(relativePath) ? null : new ResourceDictionary
        {
            Source = new Uri($"ms-appx:///{assembly.Split(',')[0]}/{relativePath.TrimStart('/')}")
        };
    }

    public object? ResolveStaticResource(object owner, object? key)
    {
        if (key is null)
        {
            throw new InvalidOperationException("A StaticResource reference has no resource key.");
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? current = owner; current is not null && visited.Add(current);
            current = parents.TryGetValue(current, out object? parent) ? parent : (current as FrameworkElement)?.Parent)
        {
            ResourceDictionary? dictionary = current switch
            {
                FrameworkElement element => element.Resources,
                ResourceDictionary resources => resources,
                Application app => app.Resources,
                _ => null,
            };
            if (dictionary is not null && dictionary.TryGetValue(key, out object? value))
            {
                return value;
            }
        }

        if (Application.Current.Resources.TryGetValue(key, out object? applicationValue))
        {
            return applicationValue;
        }

        if (themeResources is not null && themeResources.TryGetValue(key, out object? themeValue))
        {
            return themeValue;
        }

        throw new KeyNotFoundException($"Unable to resolve StaticResource '{key}' in the owning resource scope.");
    }

    public object Parse(object? owner, string xaml)
    {
        ResourceDictionary application = Application.Current.Resources;
        var dictionaries = new List<ResourceDictionary>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? current = owner; current is not null && visited.Add(current); current = parents.GetValueOrDefault(current))
        {
            ResourceDictionary? dictionary = current switch
            {
                FrameworkElement element => element.Resources,
                ResourceDictionary resources => resources,
                Application app => app.Resources,
                _ => null,
            };
            if (dictionary is not null && !ReferenceEquals(dictionary, application) && !dictionaries.Contains(dictionary))
            {
                dictionaries.Add(dictionary);
            }
        }

        // The innermost scope must win over ancestors while parsing detached objects.
        dictionaries.Reverse();

        if (themeResources is not null && !dictionaries.Contains(themeResources))
        {
            dictionaries.Insert(0, themeResources);
        }

        int originalCount = application.MergedDictionaries.Count;
        try
        {
            foreach (ResourceDictionary dictionary in dictionaries)
            {
                application.MergedDictionaries.Add(dictionary);
            }

            if (!string.IsNullOrEmpty(themePath))
            {
                Program.WriteDiagnosticTrace($"Parsing resource in theme scope '{themeAssembly}/{themePath}'.");
            }

            return XamlRuntimeUtilities.ParseXaml(xaml) ?? throw new InvalidOperationException("XAML parsing returned no object.");
        }
        finally
        {
            while (application.MergedDictionaries.Count > originalCount)
                application.MergedDictionaries.RemoveAt(application.MergedDictionaries.Count - 1);
        }
    }

    public void UpdateResources(object owner)
    {
        ResourceDictionary? dictionary = owner switch
        {
            ResourceDictionary resources => resources,
            FrameworkElement element => element.Resources,
            Application application => application.Resources,
            _ => null,
        };
        if (dictionary is null)
        {
            throw new InvalidOperationException("The resource owner has no dictionary.");
        }

        foreach (var entry in dictionary)
        {
            if (entry.Value is not null)
            {
                identity.Track(entry.Value);
            }
        }
    }

    public void RemoveObjects(HashSet<object> released)
    {
        foreach (object key in new List<object>(parents.Keys))
        {
            if (released.Contains(key) || released.Contains(parents[key]))
            {
                parents.Remove(key);
            }
        }
    }
}
