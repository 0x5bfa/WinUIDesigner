// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents;

using WinUIDesigner.Vsix;

namespace WinUIDesigner.Platform;

// Keep the shared XAML project/document pipeline, substituting the UWP-compatible
// property inspector and our surface-process context at its extension points.
public sealed class WinUIProjectContext : XamlProjectContext
{
    public WinUIProjectContext(IDesignerContext designerContext, IPlatform platform)
        : base(designerContext, platform)
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext instantiated.");
    }

    protected override void InitializeProject()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.InitializeProject reached.");
        base.InitializeProject();
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.InitializeProject completed.");
    }

    protected override void CreateProjectMetadata()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.CreateProjectMetadata reached.");
        base.CreateProjectMetadata();
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.CreateProjectMetadata completed.");
    }

    protected override IPropertyInspectorContext CreatePropertyInspectorContext()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.CreatePropertyInspectorContext reached.");
        return new UwpPropertyInspectorContext(this);
    }

    protected override SurfaceProcessContext CreateSurfaceProcessContextCore(ISurfaceProcessContext applicationSurfaceContext)
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIProjectContext.CreateSurfaceProcessContextCore reached.");

        WinUISurfaceProcessContext? context = applicationSurfaceContext as WinUISurfaceProcessContext;
        if (context is null)
        {
            context = new WinUISurfaceProcessContext(this);
            WinUIDesignerLogger.LogTrace("Platform", "WinUISurfaceProcessContext bridge instantiated.");
        }

        return context;
    }

}
