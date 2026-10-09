// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Versioning;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.Platform;

/// <summary>
/// Implements <see cref="IPlatformCreator"/> for the WinUI platform, responsible for creating
/// and configuring the WinUI platform instance for the XAML designer.
/// </summary>
public sealed class WinUIPlatformCreator : PlatformCreatorBase
{
    protected override FrameworkName RuntimeFramework => FrameworkNames.CurrentDotNetCore;

    protected override PlatformName RuntimePlatform => PlatformNames.CurrentWindows;

    protected override string XamlRuntime => XamlRuntimeNames.WinUI;

    public WinUIPlatformCreator(IPlatformService platformService)
        : base(platformService)
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatformCreator instantiated.");
    }

    protected override PlatformBase CreatePlatformInternal(IPlatformReferenceAssemblyResolver referenceAssemblyResolver)
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatformCreator.CreatePlatformInternal reached.");

        return new WinUIPlatform(referenceAssemblyResolver);
    }
}
