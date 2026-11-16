// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Reflection;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.ViewModel;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views;

namespace WinUIDesigner.Platform;

/// <summary>
/// Provides the view that displays the WinUI design surface (artboard) in Visual Studio's XAML Designer
/// and connects it to editing interactions.
/// </summary>
internal sealed class WinUISceneView(UwpSceneViewModel viewModel) : UwpSceneView(viewModel)
{
    private static readonly FieldInfo _ImageHostField = typeof(UwpSceneView).GetField("imageHost", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UwpSceneView).FullName, "imageHost");

    // The "Conditions" drop-down of this view; it lives as long as the scroll viewer it was added to.
    private ConditionalXamlSwitches? _conditionalXamlSwitches;

    protected override Artboard CreateArtboard()
    {
        PlatformSurface = new IsolatedSurface();

        // Use WinUI-specific isolated image host instance so that the artboard can render WinUI content via WinUIHwndHost.
        var imageHost = new WinUIIsolatedImageHost(this);
        _ImageHostField.SetValue(this, imageHost);

        Artboard artboard = new UwpArtboard(PlatformSurface, imageHost, ViewModel);

        artboard.Loaded += ArtboardLoaded;

        return artboard;
    }

    /// <summary>
    /// Creates Visual Studio's scroll viewer of the design surface and adds the conditional XAML switches to its
    /// device toolbar. The designer pane has no extension point for toolbar items, and this is where the scroll
    /// viewer, together with its toolbar, comes into being.
    /// </summary>
    protected override SceneScrollViewer CreateScrollViewer(Artboard artboard)
    {
        SceneScrollViewer scrollViewer = base.CreateScrollViewer(artboard);
        try
        {
            _conditionalXamlSwitches = ConditionalXamlSwitches.TryAttach(scrollViewer, () => DocumentRoot, () => ProjectContext);
        }
        catch (Exception exception)
        {
            // The designer works without the switches.
            WinUIDesignerLogger.LogWarning("Platform", "Conditional XAML switches could not be added to the designer; the designer works without them.", exception);
        }

        return scrollViewer;
    }

    protected override void DisposeInternal(bool disposing)
    {
        if (disposing)
        {
            // The switches listen to the document's text and to the project's options, which outlive this view.
            ConditionalXamlSwitches? switches = _conditionalXamlSwitches;
            _conditionalXamlSwitches = null;
            switches?.Dispose();
        }

        base.DisposeInternal(disposing);
    }

    private void ArtboardLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ProtectionZone.Execute(delegate
        {
            EnsureActiveViewUpdated();

            ViewModel.SchedulePipelineTasks(
                viewSwitched: true,
                DocumentPipelineUpdateInfo.CreateFromViewModel(ViewModel, SceneUpdateStates.None));

        });
    }
}
