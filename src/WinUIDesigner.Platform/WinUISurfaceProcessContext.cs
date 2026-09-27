using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

internal sealed class WinUISurfaceProcessContext : UwpSurfaceProcessContext
{
    public WinUISurfaceProcessContext(IProjectContext project)
        : base(project)
    {
    }

    protected override DesignerInstanceManager CreateDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler)
    {
        return new WinUIDesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler);
    }
}
