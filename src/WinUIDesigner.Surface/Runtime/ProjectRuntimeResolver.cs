// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Resolves staged project assemblies, XAML metadata, and resources in the surface process.
/// Indexes staged metadata without loading every project dependency into the Visual Studio host.
/// </summary>
internal static class ProjectRuntimeResolver
{
    private static readonly Dictionary<string, string> _Assemblies = [with(StringComparer.OrdinalIgnoreCase)];

    private static readonly Dictionary<string, string> _Types = [with(StringComparer.Ordinal)];

    private static readonly Dictionary<string, string> _NativeLibraries = [with(StringComparer.OrdinalIgnoreCase)];
    private static readonly List<string> _PriFiles = [];

    private static readonly List<string> _ProviderTypes = [];

    private static readonly List<IXamlMetadataProvider> _Providers = [];

    private static bool _providersInitialized;

    [ThreadStatic] private static HashSet<string>? _resolving;

    public static void Initialize(string directory)
    {
        _PriFiles.AddRange(Directory.EnumerateFiles(directory, "*.pri", SearchOption.AllDirectories));

        foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
            .OrderBy(file => Path.GetRelativePath(directory, file).Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);

                if (!pe.HasMetadata)
                {
                    if (pe.PEHeaders.CoffHeader.Machine == Machine.Amd64)
                    {
                        _NativeLibraries.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                    }

                    continue;
                }

                MetadataReader metadata = pe.GetMetadataReader();

                if (!metadata.IsAssembly)
                {
                    continue;
                }

                string assemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
                if (assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft.VisualStudio.", StringComparison.Ordinal) ||
                    assemblyName == "WinUISurface" || assemblyName == "Microsoft.WinUI")
                {
                    continue;
                }

                _Assemblies.TryAdd(assemblyName, file);

                foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
                {
                    var definition = metadata.GetTypeDefinition(handle);
                    var name = metadata.GetString(definition.Name);
                    var ns = metadata.GetString(definition.Namespace);

                    if (string.IsNullOrEmpty(ns))
                    {
                        continue;
                    }

                    var fullName = ns + "." + name;
                    _Types.TryAdd(fullName, assemblyName);

                    foreach (InterfaceImplementationHandle implementation in definition.GetInterfaceImplementations())
                    {
                        EntityHandle type = metadata.GetInterfaceImplementation(implementation).Interface;

                        if (type.Kind == HandleKind.TypeReference &&
                            metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)type).Name) == nameof(IXamlMetadataProvider))
                        {
                            _ProviderTypes.Add(fullName);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Unable to index staged assembly '{file}': {ex.Message}");
            }
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            return name.Name is string simpleName && _Assemblies.TryGetValue(simpleName, out string? file)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file)
                : null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            return _NativeLibraries.TryGetValue(Path.GetFileNameWithoutExtension(name), out var file)
                ? NativeLibrary.Load(file)
                : 0;
        };
    }

    public static IResourceManager CreateResourceManager()
    {
        var primary = new ResourceManager();
        var projects = new List<ResourceManager>();

        foreach (string file in _PriFiles)
        {
            try
            {
                projects.Add(new ResourceManager(file));

                WinUIDesignerLogger.LogTrace("Surface", $"Staged PRI loaded: '{file}'.");
            }
            catch (Exception ex)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Unable to load staged PRI '{file}': {ex.Message}");
            }
        }

        primary.ResourceNotFound += (_, args) =>
        {
            foreach (ResourceManager manager in projects)
            {
                var candidate = manager.MainResourceMap.TryGetValue(args.Name, args.Context);
                if (candidate is null)
                {
                    continue;
                }

                args.SetResolvedCandidate(candidate);

                WinUIDesignerLogger.LogTrace("Surface", $"Project resource resolved: {args.Name}.");

                return;
            }

            WinUIDesignerLogger.LogTrace("Surface", $"Project resource was not found: {args.Name}.");
        };

        return primary;
    }

    public static Type? ResolveType(string fullName)
    {
        return _Types.TryGetValue(fullName, out string? name) && _Assemblies.TryGetValue(name, out string? file)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file).GetType(fullName)
            : null;
    }

    private static void EnsureProviders()
    {
        if (_providersInitialized)
        {
            return;
        }

        _providersInitialized = true;

        foreach (string name in _ProviderTypes.Distinct())
        {
            var type = ResolveType(name);
            // A generated metadata provider is safe to construct independently;
            // constructing a second Application would replace the designer process.

            if (type is null || type.IsAbstract || typeof(Application).IsAssignableFrom(type))
            {
                continue;
            }

            if (Activator.CreateInstance(type) is IXamlMetadataProvider provider)
            {
                _Providers.Add(provider);
            }
        }
    }

    public static IXamlType? GetXamlType(string name)
    {
        _resolving ??= [with(StringComparer.Ordinal)];
        if (!_resolving.Add(name))
        {
            return null;
        }

        try
        {
            EnsureProviders();

            foreach (IXamlMetadataProvider provider in _Providers)
            {
                if (provider.GetXamlType(name) is { } type) return type;
            }

            return null;
        }
        finally
        {
            _resolving.Remove(name);
        }
    }

    public static XmlnsDefinition[] GetXmlnsDefinitions()
    {
        EnsureProviders();

        return [.. _Providers.SelectMany(value => value.GetXmlnsDefinitions() ?? [])];
    }
}
