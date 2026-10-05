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

namespace WinUIDesigner.Surface;

// Index staged metadata without executing every project dependency in the host.
internal static class ProjectRuntimeResolver
{
    private static readonly Dictionary<string, string> Assemblies = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> NativeLibraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> PriFiles = [];

    private static readonly List<string> ProviderTypes = [];

    private static readonly List<IXamlMetadataProvider> Providers = [];

    private static bool providersInitialized;

    [ThreadStatic] private static HashSet<string>? resolving;

    public static void Initialize(string directory)
    {
        PriFiles.AddRange(Directory.EnumerateFiles(directory, "*.pri", SearchOption.AllDirectories));

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
                        NativeLibraries.TryAdd(Path.GetFileNameWithoutExtension(file), file);
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

                Assemblies.TryAdd(assemblyName, file);

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
                    Types.TryAdd(fullName, assemblyName);

                    foreach (InterfaceImplementationHandle implementation in definition.GetInterfaceImplementations())
                    {
                        EntityHandle type = metadata.GetInterfaceImplementation(implementation).Interface;

                        if (type.Kind == HandleKind.TypeReference &&
                            metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)type).Name) == nameof(IXamlMetadataProvider))
                            ProviderTypes.Add(fullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException)
            {
                Program.WriteDiagnosticTrace($"Unable to index staged assembly '{file}': {ex.Message}");
            }
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            return name.Name is string simpleName && Assemblies.TryGetValue(simpleName, out string? file)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file)
                : null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            return NativeLibraries.TryGetValue(Path.GetFileNameWithoutExtension(name), out var file)
                ? NativeLibrary.Load(file)
                : 0;
        };
    }

    public static IResourceManager CreateResourceManager()
    {
        var primary = new ResourceManager();
        var projects = new List<ResourceManager>();

        foreach (string file in PriFiles)
        {
            try
            {
                projects.Add(new ResourceManager(file));

                Program.WriteDiagnosticTrace($"Staged PRI loaded: '{file}'.");
            }
            catch (Exception ex)
            {
                Program.WriteDiagnosticTrace($"Unable to load staged PRI '{file}': {ex.Message}");
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
                Program.WriteDiagnosticTrace($"Project resource resolved: {args.Name}.");

                return;
            }

            Program.WriteDiagnosticTrace($"Project resource was not found: {args.Name}.");
        };

        return primary;
    }

    public static Type? ResolveType(string fullName)
    {
        return Types.TryGetValue(fullName, out string? name) && Assemblies.TryGetValue(name, out string? file)
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file).GetType(fullName)
            : null;
    }

    private static void EnsureProviders()
    {
        if (providersInitialized)
        {
            return;
        }

        providersInitialized = true;

        foreach (string name in ProviderTypes.Distinct())
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
                Providers.Add(provider);
            }
        }
    }

    public static IXamlType? GetXamlType(string name)
    {
        resolving ??= new HashSet<string>(StringComparer.Ordinal);
        if (!resolving.Add(name))
        {
            return null;
        }

        try
        {
            EnsureProviders();

            foreach (IXamlMetadataProvider provider in Providers)
            {
                if (provider.GetXamlType(name) is { } type) return type;
            }

            return null;
        }
        finally
        {
            resolving.Remove(name);
        }
    }

    public static XmlnsDefinition[] GetXmlnsDefinitions()
    {
        EnsureProviders();

        return [.. Providers.SelectMany(value => value.GetXmlnsDefinitions() ?? [])];
    }
}
