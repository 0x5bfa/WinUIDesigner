// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.Shell;
using MS.Internal.Package.Toolbox;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WinUIDesigner.Toolbox;

[Guid("BC1C0860-EB3A-4FA2-A9F2-F6F3840182B3")]
public sealed class WinUIToolboxItemDiscovery : IToolboxItemDiscoveryWithContext, IToolboxItemDiscoveryByName
{
    private static readonly Bitmap _DefaultIcon = SystemIcons.Application.ToBitmap();

    public IEnumerable<string> AdditionalAssemblyPaths => [];

    public WinUIToolboxItemDiscovery()
    {
    }

    public WinUIToolboxItemDiscovery(IServiceProvider serviceProvider)
    {
    }

    // Manifest-based discovery can supply names rather than reflection-only Types.
    public IToolboxItemInfo? GetItemInfo(IToolboxTypeByName type, ToolboxItemDiscoveryContext context)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (type.AssemblyInfo is null)
        {
            throw new ArgumentException("AssemblyInfo property is null", nameof(type));
        }

        string? path = type.AssemblyInfo.OriginalPath;
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("OriginalPath property is null", nameof(type));
        }

        string displayName = ToolboxUtilities.RemoveNamespaceQualification(type.TypeFullName);
        string assemblyName = AssemblyName.GetAssemblyName(path).Name ?? string.Empty;


        return new XamlToolboxItemInfo(
            displayName,
            _DefaultIcon,
            Color.Transparent,
            [
                new("OriginalPath", path),
                new("TargetPlatform", "Windows, Version=10.0"),
                new("IsPlatformControl", "False"),
                new("ToolboxTabName", "WinUI 3"),
            ],
            new Dictionary<string, string>()
            {
                ["DirectoryId"] = path,
                ["AssemblyId"] = assemblyName,
                ["TargetPlatformId"] = "Windows",
            });
    }

    public IToolboxItemInfo? GetItemInfo(IToolboxType type, ToolboxItemDiscoveryContext context)
    {
        if (type is null || type.Type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }


        if (type.Type.IsAbstract || type.Type.ContainsGenericParameters ||
            !(type.Type.IsPublic || type.Type.IsNestedPublic) || type.Type.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        // Simplified version of XamlToolboxItemDiscovery.HasCorrectBaseType
        bool isWinUIControl = false;
        for (var parent = type.Type; parent is not null; parent = parent.BaseType)
        {
            if (parent.FullName is "Microsoft.UI.Xaml.Controls.Page" or "Microsoft.UI.Xaml.Window")
            {
                return null;
            }

            if (parent.FullName == "Microsoft.UI.Xaml.FrameworkElement")
            {
                isWinUIControl = true;
                break;
            }
        }

        if (!isWinUIControl)
        {
            return null;
        }

        if (type.Type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == "System.ComponentModel.ToolboxItemAttribute" &&
            attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is false))
        {
            return null;
        }

        var extraInfo = new List<KeyValuePair<string, string>> { new("ToolboxTabName", "WinUI 3"), };
        if (!string.IsNullOrEmpty(type.AssemblyInfo?.OriginalPath))
        {
            extraInfo.Add(new("OriginalPath", type.AssemblyInfo?.OriginalPath!));
        }
        extraInfo.Add(new("TargetPlatform", "Windows, Version=10.0"));
        extraInfo.Add(new("IsPlatformControl", "False"));

        return new XamlToolboxItemInfo(
            ToolboxUtilities.RemoveNamespaceQualification(type.Type.AssemblyQualifiedName!),
            _DefaultIcon,
            Color.Transparent,
            extraInfo,
            new Dictionary<string, string>()
            {
                ["DirectoryId"] = type.AssemblyInfo?.OriginalPath ?? string.Empty,
                ["AssemblyId"] = type.Type.Assembly.GetName().Name ?? string.Empty,
                ["TargetPlatformId"] = "Windows",
            });
    }
}
