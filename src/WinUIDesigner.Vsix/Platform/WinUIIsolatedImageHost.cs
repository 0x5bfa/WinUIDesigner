// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views;

namespace WinUIDesigner.Platform;

/// <summary>
/// Hosts the isolated WinUI design surface in the shared Visual Studio artboard
/// and bridges surface positioning and input to the designer.
/// </summary>
internal sealed partial class WinUIIsolatedImageHost(WinUISceneView view) : IsolatedSurfaceImageHost(view)
{
    internal WinUISceneView SceneView { get; } = view;

    public override bool IsViewStateSupported(int width, int height, DisplayOrientation displayOrientation, DeviceScaleFactor scale)
    {
        return true;
    }

    protected override IsolatedHwndHost CreateHwndHost(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new WinUIHwndHost(this);
    }
}
