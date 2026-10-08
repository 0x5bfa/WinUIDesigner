// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.ViewModel;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views;

namespace WinUIDesigner.Platform;

/// <summary>
/// Creates the isolated surface presenter and coordinates designer geometry diagnostics.
/// </summary>
internal sealed partial class WinUIIsolatedImageHost(WinUISceneView view) : IsolatedSurfaceImageHost(view)
{
    internal WinUISceneView SceneView { get; } = view;

#if DEBUG
    private bool geometryDiagnosticScheduled;
#endif

#if DEBUG
    private void ScheduleGeometryDiagnostic()
    {
        if (geometryDiagnosticScheduled)
        {
            return;
        }

        geometryDiagnosticScheduled = true;

        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            (sender, _) =>
            {
                ((System.Windows.Threading.DispatcherTimer)sender).Stop();
                geometryDiagnosticScheduled = false;
                LogGeometry(SceneView.ViewModel.RootNode, 0);
                LogSelectionHitTests();
            },
            System.Windows.Threading.Dispatcher.CurrentDispatcher);

        timer.Start();
    }

    private void LogSelectionHitTests()
    {
        System.Windows.Media.GeneralTransform contentToArtboard = SceneView.Artboard.CalculateTransformFromContentToArtboard();

        double[] xs = [400, 600, 800];
        double[] ys = [200, 400, 600];

        foreach (double x in xs)
        {
            foreach (double y in ys)
            {
                var contentPoint = new System.Windows.Point(x, y);
                var artboardPoint = contentToArtboard.Transform(contentPoint);
                var hit = SceneView.GetSelectableElementAtPoint(artboardPoint, SelectionFor3D.None, selectedOnly: false);
                var typeName = hit?.Type.FullName ?? "<none>";
                var bounds = hit is { IsViewObjectValid: true } ? SceneView.GetActualBounds(hit.ViewTargetElement).ToString() : "<invalid>";
                var hitDetails = "";

                if (hit is { IsViewObjectValid: true })
                {
                    var hitView = hit.ViewTargetElement;
                    var knownProperties = hit.ProjectContext.Metadata.PlatformMetadata.KnownProperties;
                    var boundsInParent = SceneView.GetActualBoundsInParent(hitView).ToString();
                    var horizontalAlignment = hitView.LiveObject.GetValue(knownProperties.FrameworkElementHorizontalAlignment);
                    var verticalAlignment = hitView.LiveObject.GetValue(knownProperties.FrameworkElementVerticalAlignment);
                    var layoutSlot = hitView.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.LayoutSlotProperty);
                    var transformToParent = hitView.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.TransformToParentProperty);
                    hitDetails =
                        $", parent={hitView.VisualParent?.LiveObject?.Type?.FullName ?? "<null>"}, " +
                        $"boundsInParent={boundsInParent}, visualChildren={hitView.VisualChildrenCount}, " +
                        $"horizontalAlignment={horizontalAlignment?.Value ?? "<null>"}, verticalAlignment={verticalAlignment?.Value ?? "<null>"}, " +
                        $"layoutSlot={layoutSlot?.Value ?? "<null>"}, transformToParent={transformToParent?.Value ?? "<null>"}";
                }

                WinUIDesignerLogger.LogTrace("Platform",
                    $"Frontend selection hit: content={contentPoint}, artboard={artboardPoint}, type={typeName}, bounds={bounds}{hitDetails}.");
            }
        }
    }

    private void LogGeometry(SceneNode? node, int depth)
    {
        if (node is null)
        {
            return;
        }

        try
        {
            if (node.IsViewObjectValid)
            {
                var viewObject = node.ViewTargetElement;
                var bounds = SceneView.GetActualBounds(viewObject);
                var boundsInParent = SceneView.GetActualBoundsInParent(viewObject);
                var expectedActualWidth = "<unavailable>";
                var expectedActualHeight = "<unavailable>";
                var expectedHorizontalAlignment = "<unavailable>";
                var expectedVerticalAlignment = "<unavailable>";
                var expectedLayoutSlot = "<unavailable>";
                var expectedTransformToParent = "<unavailable>";

                try
                {
                    var metadata = node.ProjectContext.Metadata;
                    var instanceBuilderPlatform = new Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation.UwpDesignerInstanceBuilderPlatform(node.ProjectContext);
                    var actualWidthProperty = metadata.ResolveProperty(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualWidth);
                    var actualHeightProperty = metadata.ResolveProperty(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualHeight);
                    var horizontalAlignmentProperty = metadata.ResolveProperty(metadata.PlatformMetadata.KnownProperties.FrameworkElementHorizontalAlignment);
                    var verticalAlignmentProperty = metadata.ResolveProperty(metadata.PlatformMetadata.KnownProperties.FrameworkElementVerticalAlignment);
                    var layoutSlotProperty = metadata.ResolveProperty(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.LayoutSlotProperty);
                    var transformToParentProperty = metadata.ResolveProperty(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.TransformToParentProperty);
                    expectedActualWidth = instanceBuilderPlatform.SerializeProperty(actualWidthProperty);
                    expectedActualHeight = instanceBuilderPlatform.SerializeProperty(actualHeightProperty);
                    expectedHorizontalAlignment = instanceBuilderPlatform.SerializeProperty(horizontalAlignmentProperty);
                    expectedVerticalAlignment = instanceBuilderPlatform.SerializeProperty(verticalAlignmentProperty);
                    expectedLayoutSlot = instanceBuilderPlatform.SerializeProperty(layoutSlotProperty);
                    expectedTransformToParent = instanceBuilderPlatform.SerializeProperty(transformToParentProperty);

                    var actualWidth = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualWidth);
                    var actualHeight = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualHeight);
                    var horizontalAlignment = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementHorizontalAlignment);
                    var verticalAlignment = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementVerticalAlignment);
                    var layoutSlot = viewObject.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.LayoutSlotProperty);
                    var transformToParent = viewObject.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.TransformToParentProperty);
                    expectedActualWidth += $" -> valueType={instanceBuilderPlatform.SerializeType(actualWidthProperty.PropertyType)}, live={actualWidth?.Value ?? "<null>"}";
                    expectedActualHeight += $" -> valueType={instanceBuilderPlatform.SerializeType(actualHeightProperty.PropertyType)}, live={actualHeight?.Value ?? "<null>"}";
                    expectedHorizontalAlignment += $" -> valueType={instanceBuilderPlatform.SerializeType(horizontalAlignmentProperty.PropertyType)}, live={horizontalAlignment?.Value ?? "<null>"}";
                    expectedVerticalAlignment += $" -> valueType={instanceBuilderPlatform.SerializeType(verticalAlignmentProperty.PropertyType)}, live={verticalAlignment?.Value ?? "<null>"}";
                    expectedLayoutSlot += $" -> valueType={instanceBuilderPlatform.SerializeType(layoutSlotProperty.PropertyType)}, live={layoutSlot?.Value ?? "<null>"}";
                    expectedTransformToParent += $" -> valueType={instanceBuilderPlatform.SerializeType(transformToParentProperty.PropertyType)}, live={transformToParent?.Value ?? "<null>"}";
                }
                catch (Exception ex)
                {
                    expectedActualWidth = $"<failed: {ex.GetType().Name}: {ex.Message}>";
                }

                WinUIDesignerLogger.LogTrace("Platform",
                    $"Frontend geometry depth={depth}, type={node.Type.FullName}, bounds={bounds}, boundsInParent={boundsInParent}, " +
                    $"visualParent={viewObject.VisualParent?.LiveObject?.Type?.FullName ?? "<null>"}, visualChildren={viewObject.VisualChildrenCount}, " +
                    $"transformToRoot={node.TransformToRoot}, expectedActualWidth={expectedActualWidth}, expectedActualHeight={expectedActualHeight}, " +
                    $"expectedHorizontalAlignment={expectedHorizontalAlignment}, expectedVerticalAlignment={expectedVerticalAlignment}, " +
                    $"expectedLayoutSlot={expectedLayoutSlot}, expectedTransformToParent={expectedTransformToParent}.");
            }
            else
            {
                WinUIDesignerLogger.LogTrace("Platform", $"Frontend geometry depth={depth}, type={node.Type.FullName}, viewObject=<invalid>.");
            }

            foreach (SceneNode child in new SceneNodeLiveChildrenCollection<SceneNode>(node))
            {
                LogGeometry(child, depth + 1);
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Platform", $"Frontend geometry failed for depth={depth}.", ex);
        }
    }
#endif

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
