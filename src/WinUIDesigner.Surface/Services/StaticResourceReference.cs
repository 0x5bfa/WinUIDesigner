// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Carries a deferred static-resource key from the instance builder.
/// The UWP instance builder sends this protocol object instead of a WinRT type and initializes its key
/// before the reference is evaluated at the owning property.
/// </summary>
internal sealed class StaticResourceReference
{
    public const string ProtocolTypeName = "System.Windows.StaticResourceExtension";

    public object? ResourceKey { get; set; }
}
