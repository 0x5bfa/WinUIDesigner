// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Services;

// UWP's instance builder sends this protocol object instead of a WinRT type.
// Its key is initialized before the reference is evaluated at the owning property.
internal sealed class StaticResourceReference
{
    public const string ProtocolTypeName = "System.Windows.StaticResourceExtension";

    public object? ResourceKey { get; set; }
}
