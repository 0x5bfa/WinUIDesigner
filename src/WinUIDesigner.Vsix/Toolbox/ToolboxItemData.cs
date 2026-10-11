// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.Toolbox;

internal sealed class ToolboxItemData
{
    public ToolboxItemData(
        bool isCommon,
        string assemblyQualifiedTypeName,
        string? targetFrameworks,
        IEnumerable<PlatformName>? targetPlatforms)
    {
        if (assemblyQualifiedTypeName is null)
        {
            throw new ArgumentNullException(nameof(assemblyQualifiedTypeName));
        }

        int separator = assemblyQualifiedTypeName.IndexOf(',');
        if (separator < 0 || assemblyQualifiedTypeName.Length <= separator + 1)
        {
            throw new ArgumentException("The type name must include an assembly name.", nameof(assemblyQualifiedTypeName));
        }

        IsCommon = isCommon;
        AssemblyQualifiedTypeName = assemblyQualifiedTypeName;
        NamespaceQualifiedTypeName = assemblyQualifiedTypeName.Substring(0, separator);
        UnqualifiedTypeName = NamespaceQualifiedTypeName.Substring(NamespaceQualifiedTypeName.LastIndexOf('.') + 1);
        AssemblyName = assemblyQualifiedTypeName.Substring(separator + 2);
        TargetFrameworks = targetFrameworks;
        TargetPlatforms = targetPlatforms;
    }

    public bool IsCommon { get; }

    public string AssemblyQualifiedTypeName { get; }

    public string NamespaceQualifiedTypeName { get; }

    public string UnqualifiedTypeName { get; }

    public string AssemblyName { get; }

    public string? TargetFrameworks { get; }

    public IEnumerable<PlatformName>? TargetPlatforms { get; }
}
