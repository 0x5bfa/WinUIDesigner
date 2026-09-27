using System;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

public sealed class WinUIProjectContext : XamlProjectContext
{
    public WinUIProjectContext(IDesignerContext designerContext, IPlatform platform)
        : base(designerContext, platform)
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext instantiated.");
    }

    protected override void InitializeProject()
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.InitializeProject reached.");
        base.InitializeProject();
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.InitializeProject completed.");
    }

    protected override void CreateProjectMetadata()
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.CreateProjectMetadata reached.");
        base.CreateProjectMetadata();
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.CreateProjectMetadata completed.");
    }

    protected override IPropertyInspectorContext CreatePropertyInspectorContext()
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.CreatePropertyInspectorContext reached.");
        return new UwpPropertyInspectorContext(this);
    }

    protected override SurfaceProcessContext CreateSurfaceProcessContextCore(ISurfaceProcessContext applicationSurfaceContext)
    {
        WinUIPlatform.WriteDiagnosticTrace("WinUIProjectContext.CreateSurfaceProcessContextCore reached.");

        WinUISurfaceProcessContext? context = applicationSurfaceContext as WinUISurfaceProcessContext;
        if (context is null)
        {
            context = new WinUISurfaceProcessContext(this);
            WinUIPlatform.WriteDiagnosticTrace("WinUISurfaceProcessContext bridge instantiated.");
        }

        return context;
    }

}
