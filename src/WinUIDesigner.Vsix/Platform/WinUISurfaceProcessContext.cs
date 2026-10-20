// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

// Reuse UWP's process lifecycle and protocol; WinUI only replaces the instance
// manager that prepares documents and coordinates app-level preview resources.
internal sealed class WinUISurfaceProcessContext(IProjectContext project) : UwpSurfaceProcessContext(project)
{
#if VS2022
    protected override DesignerInstanceManager CreateDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler,
        IInstanceBuildingTelemetry telemetry)
#else
    protected override DesignerInstanceManager CreateDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler)
#endif
    {
#if VS2022
        return new WinUIDesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler, telemetry);
#else
        return new WinUIDesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler);
#endif
    }
}
