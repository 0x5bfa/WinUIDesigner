// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Mono.Cecil;

namespace WinUIDesigner.Vsix.Toolbox;

[Guid("BC1C0860-EB3A-4FA2-A9F2-F6F3840182B3")]
public sealed class WinUIToolboxItemDiscovery : IToolboxItemDiscoveryWithContext, IToolboxItemDiscoveryByName
{
    public IEnumerable<string> AdditionalAssemblyPaths => Array.Empty<string>();
    public WinUIToolboxItemDiscovery()
    {
        ToolboxTrace.Write("ItemDiscovery constructed without IServiceProvider.");
    }

    public WinUIToolboxItemDiscovery(IServiceProvider serviceProvider)
    {
        ToolboxTrace.Write($"ItemDiscovery constructed with IServiceProvider={serviceProvider?.GetType().FullName ?? "<null>"}.");
    }

    // Manifest-based discovery can supply names rather than reflection-only Types.
    // Inspect PE metadata without loading project code into devenv or invoking constructors.
    public IToolboxItemInfo? GetItemInfo(IToolboxTypeByName type, ToolboxItemDiscoveryContext context)
    {
        string? path = type?.AssemblyInfo?.OriginalPath;
        ToolboxTrace.Write($"DiscoveryByName.GetItemInfo: type={type?.TypeFullName ?? "<null>"}, path={path ?? "<null>"}, context={context}.");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ToolboxTrace.Write("DiscoveryByName rejected: assembly path is missing.");
            return null;
        }

        try
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(path));
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            TypeDefinition? candidate = assembly.MainModule.GetType(type!.TypeFullName);
            if (candidate is null)
            {
                ToolboxTrace.Write("DiscoveryByName rejected: type metadata was not found.");
                return null;
            }
            if (candidate.IsAbstract || candidate.HasGenericParameters || !(candidate.IsPublic || candidate.IsNestedPublic))
            {
                ToolboxTrace.Write("DiscoveryByName rejected: type is abstract, generic, or non-public.");
                return null;
            }
            if (!candidate.Methods.Any(method => method.IsConstructor && method.IsPublic && !method.IsStatic && !method.HasParameters))
            {
                ToolboxTrace.Write("DiscoveryByName rejected: public parameterless constructor was not found.");
                return null;
            }
            if (candidate.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.ComponentModel.ToolboxItemAttribute"
                && attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is false))
            {
                ToolboxTrace.Write("DiscoveryByName rejected: ToolboxItem(false).");
                return null;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (TypeReference? parent = candidate; parent is not null && visited.Add(parent.FullName);)
            {
                if (parent.FullName is "Microsoft.UI.Xaml.Controls.Page" or "Microsoft.UI.Xaml.Window")
                {
                    ToolboxTrace.Write($"DiscoveryByName rejected: disallowed base type {parent.FullName}.");
                    return null;
                }
                if (parent.FullName == "Microsoft.UI.Xaml.FrameworkElement" || IsFrameworkControl(parent.FullName))
                {
                    ToolboxTrace.Write($"DiscoveryByName accepted: type={candidate.FullName}, base={parent.FullName}, assembly={assembly.Name.FullName}.");
                    return new WinUIToolboxItemInfo(candidate.Name, path!, assembly.Name.Name);
                }
                parent = parent.Resolve()?.BaseType;
            }

            ToolboxTrace.Write("DiscoveryByName rejected: Microsoft.UI.Xaml.FrameworkElement base type was not found.");
            return null;
        }
        catch (AssemblyResolutionException exception)
        {
            ToolboxTrace.Write($"DiscoveryByName failed: {exception}");
            return null;
        }
    }

    private static bool IsFrameworkControl(string name) => name is
        "Microsoft.UI.Xaml.Controls.Control" or "Microsoft.UI.Xaml.Controls.ContentControl" or
        "Microsoft.UI.Xaml.Controls.UserControl" or "Microsoft.UI.Xaml.Controls.Button" or
        "Microsoft.UI.Xaml.Controls.TextBox" or "Microsoft.UI.Xaml.Controls.Panel" or
        "Microsoft.UI.Xaml.Controls.ItemsControl" or "Microsoft.UI.Xaml.Controls.ListView" or
        "Microsoft.UI.Xaml.Controls.GridView" or "Microsoft.UI.Xaml.Controls.Border";

    public IToolboxItemInfo? GetItemInfo(IToolboxType type, ToolboxItemDiscoveryContext context)
    {
        Type? candidate = type?.Type;
        ToolboxTrace.Write($"Discovery.GetItemInfo: type={candidate?.FullName ?? "<null>"}, path={type?.AssemblyInfo?.OriginalPath ?? "<null>"}, context={context}.");
        if (candidate is null || candidate.IsAbstract || candidate.ContainsGenericParameters
            || !(candidate.IsPublic || candidate.IsNestedPublic) || candidate.GetConstructor(Type.EmptyTypes) is null)
        {
            ToolboxTrace.Write("Discovery rejected: type is missing, abstract, generic, non-public, or has no public parameterless constructor.");
            return null;
        }
        bool control = false;
        for (Type? parent = candidate; parent is not null; parent = parent.BaseType)
        {
            if (parent.FullName is "Microsoft.UI.Xaml.Controls.Page" or "Microsoft.UI.Xaml.Window")
            {
                ToolboxTrace.Write($"Discovery rejected: disallowed base type {parent.FullName}.");
                return null;
            }
            if (parent.FullName == "Microsoft.UI.Xaml.FrameworkElement") { control = true; break; }
        }
        if (!control)
        {
            ToolboxTrace.Write("Discovery rejected: Microsoft.UI.Xaml.FrameworkElement base type was not found.");
            return null;
        }
        if (candidate.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == "System.ComponentModel.ToolboxItemAttribute"
            && attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is false))
        {
            ToolboxTrace.Write("Discovery rejected: ToolboxItem(false).");
            return null;
        }
        string? path = type!.AssemblyInfo?.OriginalPath;
        if (string.IsNullOrEmpty(path))
        {
            ToolboxTrace.Write("Discovery rejected: assembly path is missing.");
            return null;
        }
        ToolboxTrace.Write($"Discovery accepted: type={candidate.FullName}, assembly={candidate.Assembly.FullName}.");
        return new WinUIToolboxItemInfo(candidate.Name, path!, candidate.Assembly.GetName().Name ?? string.Empty);
    }

    private sealed class WinUIToolboxItemInfo : IToolboxItemInfo
    {
        private static readonly Bitmap DefaultIcon = SystemIcons.Application.ToBitmap();
        private readonly string path;
        private readonly string assembly;
        public string DisplayName { get; }
        public object Icon => DefaultIcon;
        public object TransparentColor => Color.Transparent;
        public IEnumerable<KeyValuePair<string, string>> ExtraInfo { get; }

        public WinUIToolboxItemInfo(string name, string path, string assembly)
        {
            DisplayName = name; this.path = path; this.assembly = assembly;
            ExtraInfo = new[]
            {
                new KeyValuePair<string, string>("OriginalPath", path),
                new KeyValuePair<string, string>("TargetPlatform", "Windows, Version=10.0"),
                new KeyValuePair<string, string>("IsPlatformControl", "False"),
                new KeyValuePair<string, string>("ToolboxTabName", "WinUI 3"),
            };
        }
        public string GetPropertyValue(string name) => name switch
        {
            "DirectoryId" => path,
            "AssemblyId" => assembly,
            "TargetPlatformId" => "Windows",
            _ => string.Empty,
        };
    }
}
