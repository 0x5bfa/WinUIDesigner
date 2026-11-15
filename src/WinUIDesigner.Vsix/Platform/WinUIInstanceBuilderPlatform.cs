// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation;

namespace WinUIDesigner.Platform;

/// <summary>
/// Turns the nodes of a WinUI document into the construction actions the surface executes.
/// </summary>
/// <remarks>
/// WinUI reuses the UWP instance builders unchanged. The class exists so that rules which differ from UWP,
/// such as the XAML namespace policies, have a place to live.
/// </remarks>
internal partial class WinUIInstanceBuilderPlatform(IProjectContext projectContext) : UwpDesignerInstanceBuilderPlatform(projectContext)
{
    /// <summary>
    /// Gets the namespace-policy verdicts for the documents built through this platform.
    /// The instance manager of the same pipeline shares them.
    /// </summary>
    public XamlMemberDispositionService MemberDispositions { get; } = new();
}
