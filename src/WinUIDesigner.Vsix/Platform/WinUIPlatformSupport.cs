// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.Platform;

/// <summary>
/// Answers whether a project's platform identity is one this extension can host.
/// </summary>
/// <remarks>
/// Visual Studio resolves the editing platform and the process host through separate services.
/// The host is bound only to Visual Studio's desktop WinUI configuration, so <c>XamlRuntime=WinUI</c>
/// alone is not specific enough: Uno.Sdk sets <c>DefaultXamlRuntime=WinUI</c> for every target framework,
/// and an Uno project whose active target is Android or Skia still reports the WinUI runtime.
/// Creating the WinUI editing platform for such an identity yields a project context without a process host.
/// </remarks>
internal static class WinUIPlatformSupport
{
    private static PlatformSpecification? _hostedSpecification;
    private static Func<PlatformIdentifier, bool>? _hostBindingProbe;

    /// <summary>
    /// Records the Visual Studio platform configuration that carries this extension's host binding.
    /// </summary>
    /// <param name="hostedSpecification">The specification of the configuration that received the host binding.</param>
    /// <param name="hostBindingProbe">
    /// An optional check that the identity still resolves to this extension's host,
    /// mirroring the lookup Visual Studio performs when it creates the process host.
    /// </param>
    public static void Configure(PlatformSpecification hostedSpecification, Func<PlatformIdentifier, bool>? hostBindingProbe = null)
    {
        Volatile.Write(ref _hostBindingProbe, hostBindingProbe);
        Volatile.Write(ref _hostedSpecification, hostedSpecification);
    }

    /// <summary>
    /// Forgets the hosted configuration; every identity is then reported as not hosted.
    /// </summary>
    public static void Reset()
    {
        Volatile.Write(ref _hostedSpecification, null);
        Volatile.Write(ref _hostBindingProbe, null);
    }

    /// <summary>
    /// Gets whether Visual Studio can supply this extension's process host for the identity.
    /// </summary>
    public static bool IsHosted(PlatformIdentifier? platformIdentifier)
    {
        PlatformSpecification? specification = Volatile.Read(ref _hostedSpecification);
        if (platformIdentifier is null || specification is null || !Matches(platformIdentifier, specification))
        {
            return false;
        }

        Func<PlatformIdentifier, bool>? probe = Volatile.Read(ref _hostBindingProbe);

        return probe is null || probe(platformIdentifier);
    }

    /// <summary>
    /// Gets whether the identity is a WinUI identity covered by the given configuration specification.
    /// </summary>
    public static bool Matches(PlatformIdentifier platformIdentifier, PlatformSpecification specification)
    {
        // PlatformSpecification.Match treats a missing XamlRuntime as a wildcard, so require it explicitly.
        return string.Equals(platformIdentifier.XamlRuntime, XamlRuntimeNames.WinUI, StringComparison.Ordinal)
            && specification.Match(platformIdentifier);
    }
}
