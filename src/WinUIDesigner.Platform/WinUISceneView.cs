using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.Designer.Views.ViewObjects;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.ViewModel;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views;

namespace WinUIDesigner.Platform;

internal sealed class WinUISceneView : UwpSceneView
{
    private static readonly FieldInfo ImageHostField = typeof(UwpSceneView).GetField(
        "imageHost",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UwpSceneView).FullName, "imageHost");

    public WinUISceneView(UwpSceneViewModel viewModel)
        : base(viewModel)
    {
    }

    protected override Artboard CreateArtboard()
    {
        PlatformSurface = new IsolatedSurface();

        WinUIIsolatedImageHost imageHost = new WinUIIsolatedImageHost(this);
        ImageHostField.SetValue(this, imageHost);

        WinUIPlatform.WriteDiagnosticTrace("Minimal WinUI isolated image host created.");
        Artboard artboard = new UwpArtboard(PlatformSurface, imageHost, ViewModel);
#if DEBUG
        artboard.AddHandler(
            System.Windows.Input.Mouse.PreviewMouseDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(Artboard_PreviewMouseDown),
            handledEventsToo: true);
#endif
        artboard.Loaded += ArtboardLoaded;
        return artboard;
    }

#if DEBUG
    private void Artboard_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        WritePointerDiagnostic("Artboard PreviewMouseDown", e);
        WinUIPlatform.WriteDiagnosticTrace(
            $"Artboard PreviewMouseDown: source={e.OriginalSource?.GetType().FullName ?? "<null>"}, " +
            $"activeTool={DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
            $"eventRouter={EventRouter?.GetType().FullName ?? "<null>"}, " +
            $"activeBehavior={EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}, handled={e.Handled}.");
    }
#endif

    private void ArtboardLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        ProtectionZone.Execute(delegate
        {
#if DEBUG
            if (System.Windows.PresentationSource.FromVisual(Artboard) is System.Windows.Interop.HwndSource hwndSource)
            {
                WinUIPlatform.WriteDiagnosticTrace(
                    $"Artboard presentation source: hwnd=0x{hwndSource.Handle.ToInt64():X}, " +
                    $"root={hwndSource.RootVisual?.GetType().FullName ?? "<null>"}, " +
                    $"artboard={Artboard.ActualWidth}x{Artboard.ActualHeight}, hitTest={Artboard.IsHitTestVisible}, enabled={Artboard.IsEnabled}.");
                hwndSource.AddHook(PresentationSourceHook);

                if (hwndSource.RootVisual is System.Windows.UIElement root)
                {
                    root.AddHandler(
                        System.Windows.Input.Mouse.PreviewMouseDownEvent,
                        new System.Windows.Input.MouseButtonEventHandler(PresentationRoot_PreviewMouseDown),
                        handledEventsToo: true);
                }
            }
#endif

            WinUIPlatform.WriteDiagnosticTrace(
                $"ArtboardLoaded before update: activeTool={DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
                $"eventRouter={EventRouter?.GetType().FullName ?? "<null>"}, " +
                $"activeBehavior={EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");
            EnsureActiveViewUpdated();
            ViewModel.SchedulePipelineTasks(
                viewSwitched: true,
                DocumentPipelineUpdateInfo.CreateFromViewModel(ViewModel, SceneUpdateStates.None));
            WinUIPlatform.WriteDiagnosticTrace(
                $"ArtboardLoaded after update: activeTool={DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
                $"eventRouter={EventRouter?.GetType().FullName ?? "<null>"}, " +
                $"activeBehavior={EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");
        });
    }

#if DEBUG
    private IntPtr PresentationSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0201 || msg == 0x0202)
        {
            WinUIPlatform.WriteDiagnosticTrace(
                $"Presentation source raw mouse: hwnd=0x{hwnd.ToInt64():X}, msg=0x{msg:X}, " +
                $"wParam=0x{wParam.ToInt64():X}, lParam=0x{lParam.ToInt64():X}, handled={handled}.");
        }

        return IntPtr.Zero;
    }

    private void PresentationRoot_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        WinUIPlatform.WriteDiagnosticTrace(
            $"Presentation root PreviewMouseDown: sender={sender.GetType().FullName}, " +
            $"source={e.OriginalSource?.GetType().FullName ?? "<null>"}, handled={e.Handled}.");
    }
#endif

    [Conditional("DEBUG")]
    internal void WritePointerDiagnostic(string prefix, System.Windows.Input.MouseEventArgs args)
    {
#if DEBUG
        try
        {
            System.Windows.Point artboardPoint = args.GetPosition((System.Windows.IInputElement)(object)Artboard);
            System.Windows.Point viewRootPoint = args.GetPosition((System.Windows.IInputElement)(object)ViewRootContainer);
            System.Windows.Point contentPoint = Artboard.TransformFromArtboardToContent(viewRootPoint);
            System.Windows.Media.Matrix contentToArtboard = Artboard.CalculateTransformFromContentToArtboard().Value;
            System.Windows.Media.Matrix artboardToHitRoot = Artboard.ArtboardToHitRootTransform.Value;

            WinUIPlatform.WriteDiagnosticTrace(
                $"{prefix} coordinates: artboard={artboardPoint}, viewRoot={viewRootPoint}, content={contentPoint}, " +
                $"contentToArtboard={contentToArtboard}, artboardToHitRoot={artboardToHitRoot}, " +
                $"zoom={Artboard.Zoom}, viewRootToArtboardScale={Artboard.ViewRootToArtboardScale}, " +
                $"surfaceDpiAdjustmentScale={Artboard.SurfaceProcessDpiAdjustmentScale}.");
        }
        catch (Exception ex)
        {
            WinUIPlatform.WriteDiagnosticTrace($"{prefix} coordinate diagnostic failed: {ex.GetType().FullName}: {ex.Message}");
        }
#endif
    }

}

internal sealed class WinUIIsolatedImageHost : IsolatedSurfaceImageHost
{
    internal WinUISceneView SceneView { get; }
#if DEBUG
    private bool geometryDiagnosticScheduled;
#endif

    private sealed class WinUIHwndHost : IsolatedHwndHost
    {
        private readonly WinUIIsolatedImageHost imageHost;
        private int parentHwnd;
        private readonly int surfaceDocumentId;
        private int mutationObserverRegistrationId;
        private System.Windows.Controls.Canvas? inputBridgeRoot;
        private System.Windows.UIElement? inputBridgeAdornerLayer;

        public WinUIHwndHost(WinUIIsolatedImageHost host)
            : base(host)
        {
            imageHost = host;
            surfaceDocumentId = host.SurfaceDocument.DocumentId;
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            HandleRef handle = base.BuildWindowCore(hwndParent);
            InstallInputBridge();
            return handle;
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (mutationObserverRegistrationId != 0 && Host.Pipeline is InstanceBuilderPipeline pipeline)
            {
                pipeline.ProtocolHandler.UnregisterMessageObserver(mutationObserverRegistrationId);
                mutationObserverRegistrationId = 0;
            }

            RemoveInputBridge();
            base.DestroyWindowCore(hwnd);
        }

        protected override void InitializeSurfaceHwnd(IntPtr parentHwnd)
        {
            this.parentHwnd = parentHwnd.ToInt32();
            WinUIPlatform.WriteDiagnosticTrace(
                $"WinUI artboard HWND initialized: parent=0x{parentHwnd.ToInt64():X}, document={surfaceDocumentId}.");
        }

        protected override void ResizeSurfaceHwnd(int width, int height)
        {
            if (Host.Pipeline is InstanceBuilderPipeline pipeline)
            {
                if (mutationObserverRegistrationId == 0)
                {
                    mutationObserverRegistrationId = pipeline.ProtocolHandler.RegisterMessageObserver<MutationList>(9, mutations =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            pipeline.LiveNodeTree.ProcessPendingMutations();
                            var liveRoot = pipeline.LiveNodeTree.RootNode;
#if DEBUG
                            WinUIPlatform.WriteDiagnosticTrace(
                                $"Frontend mutation received: count={mutations.Mutations?.Count ?? 0}, " +
                                $"liveRootChildren={liveRoot.Children.Count()}, " +
                                $"mutations=[{string.Join("; ", mutations.Mutations?.Select(m => $"{m.VisualMutationType}:h={m.Element?.Handle},p={m.Relation?.Parent},c={m.Relation?.Child},i={m.Relation?.ChildIndex},root={m.Element?.IsRoot},type={m.Element?.Type}") ?? [])}].");
                            imageHost.ScheduleGeometryDiagnostic();
#endif
                        }));
                    });
                }

                pipeline.ProtocolHandler.PostMessage(548, new SetSurfacePositionRequestInfo
                {
                    DocumentId = surfaceDocumentId,
                    ParentWindow = parentHwnd,
                    Width = width,
                    Height = height,
                });
                WinUIPlatform.WriteDiagnosticTrace(
                    $"SetSurfacePosition (548) posted: document={surfaceDocumentId}, parent=0x{parentHwnd:X}, size={width}x{height}.");
            }
        }

        private void InstallInputBridge()
        {
            if (inputBridgeRoot is not null
                || ExtensibilityLayerHwndSource?.HwndSource?.RootVisual is not System.Windows.Controls.Canvas root)
            {
                return;
            }

            inputBridgeRoot = root;
            inputBridgeAdornerLayer = root.Children.Count == 1 ? root.Children[0] : null;
            // The child HwndSource used by the VS designer is per-pixel transparent.
            // A fully transparent root is skipped by Win32 hit testing, so mouse input
            // falls through to the host HwndSource instead of reaching this bridge.
            // Keep the overlay visually transparent, but give it a non-zero alpha so
            // it remains an input target.
            root.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
            root.AddHandler(
                System.Windows.Input.Mouse.PreviewMouseDownEvent,
                new System.Windows.Input.MouseButtonEventHandler(InputBridge_MouseDown),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.PreviewMouseMoveEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseMove),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.PreviewMouseUpEvent,
                new System.Windows.Input.MouseButtonEventHandler(InputBridge_MouseUp),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.MouseEnterEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseEnter),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.MouseLeaveEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseLeave),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.PreviewMouseWheelEvent,
                new System.Windows.Input.MouseWheelEventHandler(InputBridge_MouseWheel),
                handledEventsToo: true);
            root.AddHandler(
                System.Windows.Input.Mouse.QueryCursorEvent,
                new System.Windows.Input.QueryCursorEventHandler(InputBridge_QueryCursor),
                handledEventsToo: true);

            WinUIPlatform.WriteDiagnosticTrace(
                $"Input bridge installed: root={root.GetType().FullName}, child={inputBridgeAdornerLayer?.GetType().FullName ?? "<none>"}.");
            WinUIPlatform.WriteDiagnosticTrace(
                $"Input bridge installed: root={root.GetType().FullName}, child={inputBridgeAdornerLayer?.GetType().FullName ?? "<none>"}.");
        }

        private void RemoveInputBridge()
        {
            if (inputBridgeRoot is not { } root)
            {
                return;
            }

            root.RemoveHandler(
                System.Windows.Input.Mouse.PreviewMouseDownEvent,
                new System.Windows.Input.MouseButtonEventHandler(InputBridge_MouseDown));
            root.RemoveHandler(
                System.Windows.Input.Mouse.PreviewMouseMoveEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseMove));
            root.RemoveHandler(
                System.Windows.Input.Mouse.PreviewMouseUpEvent,
                new System.Windows.Input.MouseButtonEventHandler(InputBridge_MouseUp));
            root.RemoveHandler(
                System.Windows.Input.Mouse.MouseEnterEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseEnter));
            root.RemoveHandler(
                System.Windows.Input.Mouse.MouseLeaveEvent,
                new System.Windows.Input.MouseEventHandler(InputBridge_MouseLeave));
            root.RemoveHandler(
                System.Windows.Input.Mouse.PreviewMouseWheelEvent,
                new System.Windows.Input.MouseWheelEventHandler(InputBridge_MouseWheel));
            root.RemoveHandler(
                System.Windows.Input.Mouse.QueryCursorEvent,
                new System.Windows.Input.QueryCursorEventHandler(InputBridge_QueryCursor));
            root.Background = null;
            inputBridgeAdornerLayer = null;
            inputBridgeRoot = null;
        }

        private bool ShouldForwardInput(System.Windows.RoutedEventArgs args)
            => inputBridgeRoot is not null
                && (ReferenceEquals(args.OriginalSource, inputBridgeRoot)
                    || ReferenceEquals(args.OriginalSource, inputBridgeAdornerLayer));

        private bool IsArtboardMouseCaptureWithin()
            => imageHost.SceneView.Artboard.IsMouseCaptureWithin;

        private void InputBridge_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            imageHost.SceneView.WritePointerDiagnostic("Input bridge MouseDown", args);
            WinUIPlatform.WriteDiagnosticTrace(
                $"Input bridge MouseDown: source={args.OriginalSource?.GetType().FullName ?? "<null>"}, forward={ShouldForwardInput(args)}.");
            WinUIPlatform.WriteDiagnosticTrace(
                $"Input bridge MouseDown: source={args.OriginalSource?.GetType().FullName ?? "<null>"}, " +
                $"forward={ShouldForwardInput(args)}, activeTool={imageHost.SceneView.DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
                $"activeBehavior={imageHost.SceneView.EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");

            if (!ShouldForwardInput(args))
            {
                return;
            }

            var forwarded = new System.Windows.Input.MouseButtonEventArgs(
                args.MouseDevice,
                args.Timestamp,
                args.ChangedButton,
                args.StylusDevice)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent,
                Source = imageHost.SceneView.Artboard,
            };
            imageHost.SceneView.Artboard.RaiseEvent(forwarded);
            WinUIPlatform.WriteDiagnosticTrace(
                $"Input bridge MouseDown forwarded: handled={forwarded.Handled}, " +
                $"activeBehavior={imageHost.SceneView.EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");
            args.Handled = forwarded.Handled;
        }

        private void InputBridge_MouseMove(object sender, System.Windows.Input.MouseEventArgs args)
        {
            // EventRouter or one of the designer adorners can capture the mouse after the
            // forwarded initial MouseDown. Once capture is anywhere inside the Artboard,
            // WPF already routes the physical move/up stream through the designer. Forwarding
            // the overlay event as well makes relocate/resize process every drag update twice.
            if (ShouldForwardInput(args) && !IsArtboardMouseCaptureWithin())
            {
                ForwardMouseEvent(args, System.Windows.Input.Mouse.MouseMoveEvent);
            }
        }

        private void InputBridge_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            if (!ShouldForwardInput(args) || IsArtboardMouseCaptureWithin())
            {
                return;
            }

            var forwarded = new System.Windows.Input.MouseButtonEventArgs(
                args.MouseDevice,
                args.Timestamp,
                args.ChangedButton,
                args.StylusDevice)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseUpEvent,
                Source = imageHost.SceneView.Artboard,
            };
            imageHost.SceneView.Artboard.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }

        private void InputBridge_MouseEnter(object sender, System.Windows.Input.MouseEventArgs args)
        {
            if (ShouldForwardInput(args))
            {
                ForwardMouseEvent(args, System.Windows.Input.Mouse.MouseEnterEvent);
            }
        }

        private void InputBridge_MouseLeave(object sender, System.Windows.Input.MouseEventArgs args)
        {
            if (ShouldForwardInput(args))
            {
                ForwardMouseEvent(args, System.Windows.Input.Mouse.MouseLeaveEvent);
            }
        }

        private void InputBridge_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs args)
        {
            if (!ShouldForwardInput(args))
            {
                return;
            }

            var forwarded = new System.Windows.Input.MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent,
                Source = imageHost.SceneView.Artboard,
            };
            imageHost.SceneView.Artboard.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }

        private void InputBridge_QueryCursor(object sender, System.Windows.Input.QueryCursorEventArgs args)
        {
            if (!ShouldForwardInput(args))
            {
                return;
            }

            var forwarded = new System.Windows.Input.QueryCursorEventArgs(args.MouseDevice, args.Timestamp)
            {
                RoutedEvent = System.Windows.Input.Mouse.QueryCursorEvent,
                Source = imageHost.SceneView.Artboard,
            };
            imageHost.SceneView.Artboard.RaiseEvent(forwarded);
            args.Cursor = forwarded.Cursor;
            args.Handled = forwarded.Handled;
        }

        private void ForwardMouseEvent(System.Windows.Input.MouseEventArgs args, System.Windows.RoutedEvent routedEvent)
        {
            var forwarded = new System.Windows.Input.MouseEventArgs(args.MouseDevice, args.Timestamp, args.StylusDevice)
            {
                RoutedEvent = routedEvent,
                Source = imageHost.SceneView.Artboard,
            };
            imageHost.SceneView.Artboard.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }

    }

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
                System.Windows.Point artboardPoint = contentToArtboard.Transform(contentPoint);
                SceneNode? hit = SceneView.GetSelectableElementAtPoint(artboardPoint, SelectionFor3D.None, selectedOnly: false);
                string typeName = hit?.Type.FullName ?? "<none>";
                string bounds = hit is { IsViewObjectValid: true }
                    ? SceneView.GetActualBounds(hit.ViewTargetElement).ToString()
                    : "<invalid>";
                string hitDetails = "";
                if (hit is { IsViewObjectValid: true })
                {
                    var hitView = hit.ViewTargetElement;
                    var knownProperties = hit.ProjectContext.Metadata.PlatformMetadata.KnownProperties;
                    string boundsInParent = SceneView.GetActualBoundsInParent(hitView).ToString();
                    LiveObject? horizontalAlignment = hitView.LiveObject.GetValue(knownProperties.FrameworkElementHorizontalAlignment);
                    LiveObject? verticalAlignment = hitView.LiveObject.GetValue(knownProperties.FrameworkElementVerticalAlignment);
                    LiveObject? layoutSlot = hitView.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.LayoutSlotProperty);
                    LiveObject? transformToParent = hitView.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.TransformToParentProperty);
                    hitDetails =
                        $", parent={hitView.VisualParent?.LiveObject?.Type?.FullName ?? "<null>"}, " +
                        $"boundsInParent={boundsInParent}, visualChildren={hitView.VisualChildrenCount}, " +
                        $"horizontalAlignment={horizontalAlignment?.Value ?? "<null>"}, verticalAlignment={verticalAlignment?.Value ?? "<null>"}, " +
                        $"layoutSlot={layoutSlot?.Value ?? "<null>"}, transformToParent={transformToParent?.Value ?? "<null>"}";
                }
                WinUIPlatform.WriteDiagnosticTrace(
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
                System.Windows.Rect bounds = SceneView.GetActualBounds(viewObject);
                System.Windows.Rect boundsInParent = SceneView.GetActualBoundsInParent(viewObject);
                string expectedActualWidth = "<unavailable>";
                string expectedActualHeight = "<unavailable>";
                string expectedHorizontalAlignment = "<unavailable>";
                string expectedVerticalAlignment = "<unavailable>";
                string expectedLayoutSlot = "<unavailable>";
                string expectedTransformToParent = "<unavailable>";
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

                LiveObject? actualWidth = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualWidth);
                LiveObject? actualHeight = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementActualHeight);
                LiveObject? horizontalAlignment = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementHorizontalAlignment);
                LiveObject? verticalAlignment = viewObject.LiveObject.GetValue(metadata.PlatformMetadata.KnownProperties.FrameworkElementVerticalAlignment);
                LiveObject? layoutSlot = viewObject.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.LayoutSlotProperty);
                LiveObject? transformToParent = viewObject.LiveObject.GetValue(Microsoft.VisualStudio.DesignTools.Markup.Metadata.XamlDesignTimeProperties.TransformToParentProperty);
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

                WinUIPlatform.WriteDiagnosticTrace(
                    $"Frontend geometry depth={depth}, type={node.Type.FullName}, bounds={bounds}, boundsInParent={boundsInParent}, " +
                    $"visualParent={viewObject.VisualParent?.LiveObject?.Type?.FullName ?? "<null>"}, visualChildren={viewObject.VisualChildrenCount}, " +
                    $"transformToRoot={node.TransformToRoot}, expectedActualWidth={expectedActualWidth}, expectedActualHeight={expectedActualHeight}, " +
                    $"expectedHorizontalAlignment={expectedHorizontalAlignment}, expectedVerticalAlignment={expectedVerticalAlignment}, " +
                    $"expectedLayoutSlot={expectedLayoutSlot}, expectedTransformToParent={expectedTransformToParent}.");
            }
            else
            {
                WinUIPlatform.WriteDiagnosticTrace($"Frontend geometry depth={depth}, type={node.Type.FullName}, viewObject=<invalid>.");
            }

            foreach (SceneNode child in new SceneNodeLiveChildrenCollection<SceneNode>(node))
            {
                LogGeometry(child, depth + 1);
            }
        }
        catch (Exception ex)
        {
            WinUIPlatform.WriteDiagnosticTrace($"Frontend geometry failed for depth={depth}: {ex.GetType().FullName}: {ex.Message}");
        }
    }
#endif

    public WinUIIsolatedImageHost(WinUISceneView view)
        : base(view)
    {
        SceneView = view;
    }

    public override bool IsViewStateSupported(
        int width,
        int height,
        DisplayOrientation displayOrientation,
        DeviceScaleFactor scale)
    {
        return true;
    }

    protected override IsolatedHwndHost CreateHwndHost(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new WinUIHwndHost(this);
    }
}
