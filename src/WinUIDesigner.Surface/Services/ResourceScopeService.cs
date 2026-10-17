// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Resolves static resources within the document and its theme resource scopes.
/// </summary>
internal sealed class ResourceScopeService(ObjectIdentityRegistry identity)
{
    private readonly Dictionary<object, object> _parents = [with(ReferenceEqualityComparer.Instance)];

    private string? _themeAssembly;

    private string? _themePath;

    private ResourceDictionary? _themeResources;

    public void SetParent(object child, object? parent)
    {
        if (parent is null)
        {
            _parents.Remove(child);

            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { child };
        for (object? current = parent; current is not null; current = _parents.GetValueOrDefault(current))
        {
            if (!visited.Add(current))
            {
                throw new InvalidOperationException("The resource search scope contains a cycle.");
            }
        }

        _parents[child] = parent;
    }

    public void SetThemeScope(string assembly, string relativePath)
    {
        if (_themeAssembly == assembly && _themePath == relativePath)
        {
            return;
        }

        _themeAssembly = assembly;
        _themePath = relativePath;
        _themeResources = string.IsNullOrEmpty(relativePath)
            ? null
            : new ResourceDictionary() { Source = new Uri($"ms-appx:///{assembly.Split(',')[0]}/{relativePath.TrimStart('/')}") };
    }

    public object? ResolveStaticResource(object owner, object? key)
    {
        if (key is null)
        {
            throw new InvalidOperationException("A StaticResource reference has no resource key.");
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? current = owner;
            current is not null && visited.Add(current);
            current = _parents.TryGetValue(current, out object? parent)
                ? parent
                : (current as FrameworkElement)?.Parent)
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

        if (_themeResources is not null && _themeResources.TryGetValue(key, out object? themeValue))
        {
            return themeValue;
        }

        throw new KeyNotFoundException($"Unable to resolve StaticResource '{key}' in the owning resource scope.");
    }

    public object Parse(object? owner, string xaml)
    {
        var application = Application.Current.Resources;

        var dictionaries = new List<ResourceDictionary>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? current = owner; current is not null && visited.Add(current); current = _parents.GetValueOrDefault(current))
        {
            var dictionary = current switch
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

        if (_themeResources is not null && !dictionaries.Contains(_themeResources))
        {
            dictionaries.Insert(0, _themeResources);
        }

        int originalCount = application.MergedDictionaries.Count;

        try
        {
            foreach (ResourceDictionary dictionary in dictionaries)
            {
                application.MergedDictionaries.Add(dictionary);
            }

            if (!string.IsNullOrEmpty(_themePath))
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Parsing resource in theme scope '{_themeAssembly}/{_themePath}'.");
            }

            return XamlReader.Load(xaml) ?? throw new InvalidOperationException("XAML parsing returned no object.");
        }
        finally
        {
            while (application.MergedDictionaries.Count > originalCount)
            {
                application.MergedDictionaries.RemoveAt(application.MergedDictionaries.Count - 1);
            }
        }
    }

    public void UpdateResources(object owner)
    {
        var dictionary = owner switch
        {
            ResourceDictionary resources => resources,
            FrameworkElement element => element.Resources,
            Application application => application.Resources,
            _ => null,
        } ?? throw new InvalidOperationException("The resource owner has no dictionary.");

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
        foreach (object key in new List<object>(_parents.Keys))
        {
            if (released.Contains(key) || released.Contains(_parents[key]))
            {
                _parents.Remove(key);
            }
        }
    }
}
