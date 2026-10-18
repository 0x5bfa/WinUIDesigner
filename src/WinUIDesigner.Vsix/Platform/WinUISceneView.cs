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
