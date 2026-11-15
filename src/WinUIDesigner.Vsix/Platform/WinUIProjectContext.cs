// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
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
public sealed class WinUIProjectContext : XamlProjectContext, IProjectContext
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
        // A library shares the surface process of the application that references it, but only when that
        // application can launch a WinUI surface itself. Visual Studio picks the application by target framework
        // family alone, so the candidate can be an Uno app head whose active target is Android or Skia;
        // its project context has no process host, and launching the surface through it fails.
        if (applicationSurfaceContext is WinUISurfaceProcessContext context
            && GetSurfaceLaunchBlocker(context.ProjectContext) is null)
        {
            WinUIDesignerLogger.LogInformation("Platform", $"Project '{ProjectName}' uses the surface process of '{context.ProjectContext?.ProjectName}'.");

            return context;
        }

        if (applicationSurfaceContext is not null)
        {
            WinUIDesignerLogger.LogInformation(
                "Platform",
                $"Project '{ProjectName}' does not share the surface process of '{applicationSurfaceContext.ProjectContext?.ProjectName}' ('{applicationSurfaceContext.ProjectContext?.PlatformIdentifier?.Identifier}'); starting its own.");
        }

        // Creates the surface-process context used to create and preview this project's documents.
        return new WinUISurfaceProcessContext(this);
    }

    // Visual Studio calls this member through IProjectContext right before it launches the surface, and its
    // implementation dereferences the process host and the project metadata without checking them. Re-implement
    // the interface member so a context that cannot launch a surface fails with a reason in the log and on the
    // design surface instead of a NullReferenceException.
    SurfaceProcessInfo IProjectContext.CreateSurfaceProcessInfo(bool forcePlatformOnly)
    {
        if (GetSurfaceLaunchBlocker(this) is { } blocker)
        {
            WinUIDesignerLogger.LogError("Platform", blocker);

            throw new InvalidOperationException(blocker);
        }

        return CreateSurfaceProcessInfo(forcePlatformOnly);
    }

    private static string? GetSurfaceLaunchBlocker(IProjectContext? projectContext)
    {
        if (projectContext is null)
        {
            return "The design surface has no project context.";
        }

        if (projectContext.IsDisposed)
        {
            return $"The designer context of project '{projectContext.ProjectName}' was closed before its design surface started. Close and reopen the XAML document.";
        }

        if (!WinUIPlatformSupport.IsHosted(projectContext.PlatformIdentifier))
        {
            return $"Project '{projectContext.ProjectName}' cannot start a WinUI design surface: no WinUI process host matches '{projectContext.PlatformIdentifier?.Identifier}'. "
                + "Make a Windows target framework (net*-windows10.0.*) the active target of this project.";
        }

        if (projectContext.Metadata is null)
        {
            return $"Project '{projectContext.ProjectName}' has no designer metadata, so its design surface cannot be started. Close and reopen the XAML document.";
        }

        return null;
    }
}
