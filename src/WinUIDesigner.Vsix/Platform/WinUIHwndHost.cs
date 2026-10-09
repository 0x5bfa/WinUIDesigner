// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIIsolatedImageHost
{
    /// <summary>
    /// Hosts the remote WinUI preview and the shared editing overlay.
    /// </summary>
    private sealed class WinUIHwndHost(WinUIIsolatedImageHost host) : IsolatedHwndHost(host)
    {
        private readonly WinUIIsolatedImageHost imageHost = host;
        private int parentHwnd;
        private readonly int surfaceDocumentId = host.SurfaceDocument.DocumentId;
        private int mutationObserverRegistrationId;
        private System.Windows.Controls.Canvas? inputBridgeRoot;
        private System.Windows.UIElement? inputBridgeAdornerLayer;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            HandleRef handle = base.BuildWindowCore(hwndParent);

            InstallInputBridge();

            return handle;
        }

        protected override void OnWindowPositionChanged(System.Windows.Rect boundingBox)
        {
            base.OnWindowPositionChanged(boundingBox);

            // The UWP child overlay positions itself at (0, 0). In the VS editor its
            // parent also contains the device toolbar, whereas the surface holder
            // starts below it. Keep the input/adorner overlay over the holder.
            if (ExtensibilityLayerHwndSource?.HwndSource is { } overlay)
            {
                WinUIOverlayWindow.PositionAtHost(overlay.Handle, Handle, boundingBox);
            }
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

            WinUIDesignerLogger.LogTrace(
                "Platform", $"WinUI artboard HWND initialized: parent=0x{parentHwnd.ToInt64():X}, document={surfaceDocumentId}.");
        }

        protected override void ResizeSurfaceHwnd(int width, int height)
        {
            if (Host.Pipeline is InstanceBuilderPipeline pipeline)
            {
                if (mutationObserverRegistrationId == 0)
                {
                    // Apply WinUI surface mutations to the Visual Studio live tree so
                    // selection and hit testing use the current runtime visual hierarchy.
                    mutationObserverRegistrationId = pipeline.ProtocolHandler.RegisterMessageObserver<MutationList>(9, mutations =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            pipeline.LiveNodeTree.ProcessPendingMutations();
                            var liveRoot = pipeline.LiveNodeTree.RootNode;
#if DEBUG
                            WinUIDesignerLogger.LogTrace(
                                "Platform",
                                $"Frontend mutation received: count={mutations.Mutations?.Count ?? 0}, " +
                                $"liveRootChildren={liveRoot.Children.Count()}, " +
                                $"mutations=[{string.Join("; ", mutations.Mutations?.Select(m => $"{m.VisualMutationType}:h={m.Element?.Handle},p={m.Relation?.Parent},c={m.Relation?.Child},i={m.Relation?.ChildIndex},root={m.Element?.IsRoot},type={m.Element?.Type}") ?? [])}].");

                            imageHost.ScheduleGeometryDiagnostic();
#endif
                        }));
                    });
                }

                // This is the same as WPF
                pipeline.ProtocolHandler.PostMessage(548, new SetSurfacePositionRequestInfo()
                {
                    DocumentId = surfaceDocumentId,
                    ParentWindow = parentHwnd,
                    Width = width,
                    Height = height,
                });

                WinUIDesignerLogger.LogTrace(
                    "Platform", $"SetSurfacePosition (548) posted: document={surfaceDocumentId}, parent=0x{parentHwnd:X}, size={width}x{height}.");
            }
        }

        private void InstallInputBridge()
        {
            if (inputBridgeRoot is not null || ExtensibilityLayerHwndSource?.HwndSource?.RootVisual is not System.Windows.Controls.Canvas root)
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

            WinUIDesignerLogger.LogTrace(
                "Platform", $"Input bridge installed: root={root.GetType().FullName}, child={inputBridgeAdornerLayer?.GetType().FullName ?? "<none>"}.");

            WinUIDesignerLogger.LogTrace(
                "Platform", $"Input bridge installed: root={root.GetType().FullName}, child={inputBridgeAdornerLayer?.GetType().FullName ?? "<none>"}.");
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
        {
            // Events from real adorner children already belong to the VS designer;
            // bridge only the blank overlay events that otherwise have no WPF target.
            return inputBridgeRoot is not null &&
                (ReferenceEquals(args.OriginalSource, inputBridgeRoot) || ReferenceEquals(args.OriginalSource, inputBridgeAdornerLayer));
        }

        private bool IsArtboardMouseCaptureWithin()
        {
            return imageHost.SceneView.Artboard.IsMouseCaptureWithin;
        }

        private void InputBridge_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            imageHost.SceneView.WritePointerDiagnostic("Input bridge MouseDown", args);

            WinUIDesignerLogger.LogTrace(
                "Platform", $"Input bridge MouseDown: source={args.OriginalSource?.GetType().FullName ?? "<null>"}, forward={ShouldForwardInput(args)}.");
            WinUIDesignerLogger.LogTrace(
                "Platform",
                $"Input bridge MouseDown: source={args.OriginalSource?.GetType().FullName ?? "<null>"}, " +
                $"forward={ShouldForwardInput(args)}, activeTool={imageHost.SceneView.DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
                $"activeBehavior={imageHost.SceneView.EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");

            if (!ShouldForwardInput(args))
            {
                return;
            }

            var forwarded = new System.Windows.Input.MouseButtonEventArgs(args.MouseDevice, args.Timestamp, args.ChangedButton, args.StylusDevice)
            {
                RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent,
                Source = imageHost.SceneView.Artboard,
            };

            imageHost.SceneView.Artboard.RaiseEvent(forwarded);

            WinUIDesignerLogger.LogTrace("Platform",
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

            var forwarded = new System.Windows.Input.MouseButtonEventArgs(args.MouseDevice, args.Timestamp, args.ChangedButton, args.StylusDevice)
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
}
