// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PropertyInspector;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

/// <summary>
/// Provides the project-level bridge between a WinUI project and Visual Studio's shared XAML designer.
/// </summary>
/// <remarks>
/// The base <see cref="XamlProjectContext"/> handles common XAML project initialization and metadata;
/// this class selects the property-inspector and surface-process implementations used by the WinUI designer.
/// </remarks>
public sealed class WinUIProjectContext : XamlProjectContext
{
    public WinUIProjectContext(IDesignerContext designerContext, IPlatform platform)
        : base(designerContext, platform)
    {
    }

    protected override void InitializeProject()
    {
        // Creates the XAML schema manager and performs the shared initialization
        base.InitializeProject();
    }

    protected override void CreateProjectMetadata()
    {
        // Supplies the shared metadata and platform attribute tables
        base.CreateProjectMetadata();
    }

    protected override IPropertyInspectorContext CreatePropertyInspectorContext()
    {
        // Creates the property-inspector context for this WinUI project.
        // This UWP-compatible context supplies the Windows XAML property editors, categories, and resource handling
        return new UwpPropertyInspectorContext(this);
    }

    protected override SurfaceProcessContext CreateSurfaceProcessContextCore(ISurfaceProcessContext applicationSurfaceContext)
    {
        // reuse it when it is already a WinUI context, otherwise create one.
        if (applicationSurfaceContext is not WinUISurfaceProcessContext context)
        {
            // Creates the surface-process context used to create and preview this project's documents.
            context = new WinUISurfaceProcessContext(this);

        }

        return context;
    }
}
