using System;
using System.Runtime.Versioning;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.Platform;

public sealed class WinUIPlatformCreator : PlatformCreatorBase
{
    protected override FrameworkName RuntimeFramework => FrameworkNames.CurrentDotNetCore;

    protected override PlatformName RuntimePlatform => PlatformNames.CurrentWindows;

    protected override string XamlRuntime => XamlRuntimeNames.WinUI;

    public WinUIPlatformCreator(IPlatformService platformService)
        : base(platformService)
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIPlatformCreator instantiated.");
    }

    protected override PlatformBase CreatePlatformInternal(IPlatformReferenceAssemblyResolver referenceAssemblyResolver)
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIPlatformCreator.CreatePlatformInternal reached.");
        return new WinUIPlatform(referenceAssemblyResolver);
    }
}
