// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace WinUIDesigner.Surface.Services;

// Hosts the document inside the Visual Studio-provided HWND while keeping the
// artboard transform, viewport clipping, and native child-window lifetime aligned.
internal sealed class DesignerSurface : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;
    private const int GwlStyle = -16;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndBottom = new(1);

    private readonly Grid viewportRoot;
    private readonly Canvas transformRoot;
    private readonly ManualResetEventSlim unfreezeCompositionCompleted = new(initialState: true);
    private FrameworkElement content;
    private ElementTheme appRequestedTheme;
    private ElementTheme? previewRequestedTheme;
    private EventHandler<object>? compositionRenderingHandler;
    private double deviceWidth;
    private double deviceHeight;
    private DesktopWindowXamlSource? xamlSource;
    private IntPtr hostWindow;
    private IntPtr parentWindow;
    private bool boundsUpdatePending;
    private bool renderingSuspended;
    private bool disposed;
    private double publishedDpi;

    public event EventHandler? BoundsInvalidated;

    public event EventHandler? SurfaceLayoutUpdated;

    public DesignerSurface(FrameworkElement content, double width, double height)
    {
        this.content = content;

        // Keep the HWND-sized viewport separate from the design surface itself.
        // The Visual Studio artboard sends pan/zoom for the design surface; applying
        // that transform to the XamlSource root also moves the viewport and breaks
        // clipping/input coordinates when the artboard is scrolled.
        viewportRoot = new Grid();
        // Canvas arranges the document at its origin even when d:DesignWidth is
        // smaller than the chosen device. A Grid centers a Stretch child whose
        // explicit width is smaller, disagreeing with the frontend's root origin.
        transformRoot = (Canvas)XamlReader.Load(
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" />");
        transformRoot.HorizontalAlignment = HorizontalAlignment.Left;
        transformRoot.VerticalAlignment = VerticalAlignment.Top;
        transformRoot.Children.Add(content);
        viewportRoot.Children.Add(transformRoot);
        viewportRoot.LayoutUpdated += OnLayoutUpdated;
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.WidthProperty) && double.IsFinite(content.Width))
            XSurfUwp.DT.SetRuntimeWidth(content, content.Width);
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.HeightProperty) && double.IsFinite(content.Height))
            XSurfUwp.DT.SetRuntimeHeight(content, content.Height);
        SetDeviceSize(width, height);
    }

    public FrameworkElement Content => content;

    public bool IsFrozen { get; private set; }
    public event Action<double>? DpiChanged;

    public void Freeze()
    {
        if (!renderingSuspended)
        {
            renderingSuspended = DiagnosticsPropertySourceService.TrySetRenderingEnabled(false);
        }
        IsFrozen = true;
    }

    public void Unfreeze()
    {
        if (!IsFrozen)
        {
            return;
        }

        if (renderingSuspended && DiagnosticsPropertySourceService.TrySetRenderingEnabled(true))
        {
            renderingSuspended = false;
        }
        IsFrozen = false;
        ArmUnfreezeCompositionBarrier();
    }

    public bool WaitForUnfreezeComposition(int millisecondsTimeout)
        => unfreezeCompositionCompleted.Wait(millisecondsTimeout);

    public Rect GetDocumentBounds()
    {
        double width = content.ActualWidth > 0 ? content.ActualWidth : deviceWidth;
        double height = content.ActualHeight > 0 ? content.ActualHeight : deviceHeight;
        return new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
    }

    public Rect GetContentBounds()
    {
        // Content can render outside its own layout slot; include those descendants
        // so the bounds reported to the artboard cover the pixels users can see.
        Rect bounds = GetDocumentBounds();
        AppendDescendantBounds(content, content, ref bounds);
        return bounds;
    }

    public void SetSurfacePosition(IntPtr parentHwnd, int width, int height)
    {
        // Message 548 supplies the holder HWND and viewport dimensions after the
        // surface has been created; this also reparents the XAML island when needed.
        EnsureIsland();
        if (parentWindow != parentHwnd)
        {
            nint style = NativeMethods.GetWindowLongPtr(hostWindow, GwlStyle);
            nint childStyle = (nint)((style.ToInt64() & ~(long)WsPopup) | WsChild | WsVisible | WsClipChildren | WsClipSiblings);
            _ = NativeMethods.SetWindowLongPtr(hostWindow, GwlStyle, childStyle);
            _ = NativeMethods.SetParent(hostWindow, parentHwnd);
            parentWindow = parentHwnd;
        }

        // The shipped UWP surface is resized synchronously before VS repositions its
        // adorner child HWND. Our message 548 crosses the process boundary, so using
        // HWND_TOP here can run after that step and cover the grid/selection adorners.
        // Keep the remote surface at the bottom of the holder's child z-order.
        _ = NativeMethods.SetWindowPos(hostWindow, HwndBottom, 0, 0, width, height, SwpFrameChanged);
        xamlSource!.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));
        viewportRoot.Width = Math.Max(1, width);
        viewportRoot.Height = Math.Max(1, height);
        viewportRoot.Measure(new Size(viewportRoot.Width, viewportRoot.Height));
        viewportRoot.Arrange(new Rect(0, 0, viewportRoot.Width, viewportRoot.Height));
        viewportRoot.UpdateLayout();
        _ = NativeMethods.InvalidateRect(parentHwnd, IntPtr.Zero, true);
        Program.WriteDiagnosticTrace($"DesignerSurface positioned: parent=0x{parentHwnd.ToInt64():X}, size={width}x{height}.");
    }

    public void SetPanZoomTransform(double offsetX, double offsetY, double scale)
    {
        scale = scale <= 0 ? 1.0 : scale;
        transformRoot.RenderTransform = new CompositeTransform
        {
            ScaleX = scale,
            ScaleY = scale,
            TranslateX = offsetX,
            TranslateY = offsetY,
        };
        Program.WriteDiagnosticTrace($"DesignerSurface pan/zoom updated: offset={offsetX},{offsetY}, scale={scale}.");
    }

    public void SetDeviceSize(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        deviceWidth = width;
        deviceHeight = height;
        transformRoot.Width = width;
        transformRoot.Height = height;
        XSurfUwp.DT.SetRootWidth(content, width);
        XSurfUwp.DT.SetRootHeight(content, height);
        transformRoot.Measure(new Size(width, height));
        transformRoot.Arrange(new Rect(0, 0, width, height));
        transformRoot.UpdateLayout();
        Program.WriteDiagnosticTrace($"DesignerSurface device size updated: {width}x{height}.");
    }

    public void ReplaceContent(FrameworkElement newContent)
    {
        if (ReferenceEquals(content, newContent))
        {
            RefreshLayout();
            return;
        }

        transformRoot.Children.Remove(content);
        content = newContent;
        transformRoot.Children.Insert(0, newContent);
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.WidthProperty) && double.IsFinite(content.Width))
            XSurfUwp.DT.SetRuntimeWidth(content, content.Width);
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.HeightProperty) && double.IsFinite(content.Height))
            XSurfUwp.DT.SetRuntimeHeight(content, content.Height);
        SetDeviceSize(deviceWidth > 0 ? deviceWidth : 800, deviceHeight > 0 ? deviceHeight : 600);
        Program.WriteDiagnosticTrace($"DesignerSurface content replaced with {newContent.GetType().FullName}.");
    }

    public void RefreshLayout()
    {
        double width = deviceWidth > 0 ? deviceWidth : Math.Max(1, transformRoot.Width);
        double height = deviceHeight > 0 ? deviceHeight : Math.Max(1, transformRoot.Height);
        transformRoot.Measure(new Size(width, height));
        transformRoot.Arrange(new Rect(0, 0, width, height));
        transformRoot.UpdateLayout();
    }

    private void OnLayoutUpdated(object? sender, object e)
    {
        SurfaceLayoutUpdated?.Invoke(this, EventArgs.Empty);

        if (boundsUpdatePending || disposed)
        {
            return;
        }

        boundsUpdatePending = true;
        if (!viewportRoot.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            boundsUpdatePending = false;
            if (!disposed)
            {
                BoundsInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }))
        {
            boundsUpdatePending = false;
        }
    }

    private void ArmUnfreezeCompositionBarrier()
    {
        if (compositionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= compositionRenderingHandler;
            compositionRenderingHandler = null;
        }

        unfreezeCompositionCompleted.Reset();

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (handler is not null)
            {
                CompositionTarget.Rendering -= handler;
            }

            compositionRenderingHandler = null;
            unfreezeCompositionCompleted.Set();
            Program.WriteDiagnosticTrace("DesignerSurface unfreeze composition barrier completed.");
        };

        compositionRenderingHandler = handler;
        CompositionTarget.Rendering += handler;
        Program.WriteDiagnosticTrace("DesignerSurface unfreeze composition barrier armed.");
    }

    public void SetCheckerboardColors(Windows.UI.Color color1, Windows.UI.Color color2)
    {
        // The island covers the entire viewport, including the area outside the
        // transformed document. Match SceneScrollViewer.ArtboardBrush there instead
        // of leaving the island's default white background visible in dark previews.
        viewportRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            (byte)((color1.R >> 1) + (color2.R >> 1)),
            (byte)((color1.G >> 1) + (color2.G >> 1)),
            (byte)((color1.B >> 1) + (color2.B >> 1))));

        // The shared frontend's background toggle sends colors, not a RequestedTheme
        // action. Use its chosen light/dark backdrop as the preview theme, so controls
        // and ThemeResource expressions change together without recreating the tree.
        previewRequestedTheme = color1.R + color1.G + color1.B >= 3 * 128
            ? ElementTheme.Light : ElementTheme.Dark;
        ApplyRequestedTheme();
        Program.WriteDiagnosticTrace($"DesignerSurface preview theme: colors={color1},{color2}, requested={previewRequestedTheme}, actual={viewportRoot.ActualTheme}.");
    }

    public void SetRequestedTheme(ElementTheme theme)
    {
        appRequestedTheme = theme;
        ApplyRequestedTheme();
    }

    private void ApplyRequestedTheme()
    {
        viewportRoot.RequestedTheme = previewRequestedTheme ?? appRequestedTheme;
        RefreshLayout();
    }

    private static void AppendDescendantBounds(UIElement root, DependencyObject parent, ref Rect bounds)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is UIElement element && TryGetBoundsInRoot(root, element, out Rect childBounds))
            {
                bounds = Union(bounds, childBounds);
            }

            AppendDescendantBounds(root, child, ref bounds);
        }
    }

    private static bool TryGetBoundsInRoot(UIElement root, UIElement element, out Rect bounds)
    {
        bounds = default;
        double width = element is FrameworkElement frameworkElement ? frameworkElement.ActualWidth : element.RenderSize.Width;
        double height = element is FrameworkElement frameworkElement2 ? frameworkElement2.ActualHeight : element.RenderSize.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0)
        {
            return false;
        }

        try
        {
            GeneralTransform transform = element.TransformToVisual(root);
            Point topLeft = transform.TransformPoint(new Point(0, 0));
            Point topRight = transform.TransformPoint(new Point(width, 0));
            Point bottomLeft = transform.TransformPoint(new Point(0, height));
            Point bottomRight = transform.TransformPoint(new Point(width, height));
            double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
            double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
            double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
            double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
            bounds = new Rect(left, top, right - left, bottom - top);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Rect Union(Rect left, Rect right)
    {
        double x = Math.Min(left.X, right.X);
        double y = Math.Min(left.Y, right.Y);
        double rightEdge = Math.Max(left.X + left.Width, right.X + right.Width);
        double bottomEdge = Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new Rect(x, y, rightEdge - x, bottomEdge - y);
    }

    private void EnsureIsland()
    {
        if (xamlSource is not null)
        {
            return;
        }

        // DesktopWindowXamlSource cannot initialize against a message-only parent.
        // Give it a local top-level HWND on this thread, then reparent that HWND into
        // the Visual Studio artboard after the island has been initialized.
        hostWindow = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            "WinUIDesigner.RemoteSurface",
            WsPopup | WsClipChildren | WsClipSiblings,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (hostWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        xamlSource = new DesktopWindowXamlSource();
        xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hostWindow));
        xamlSource.Content = viewportRoot;
        if (viewportRoot.XamlRoot is { } root)
        {
            root.Changed += OnXamlRootChanged;
            PublishDpi(root);
        }
        Program.WriteDiagnosticTrace($"DesktopWindowXamlSource initialized on surface HWND 0x{hostWindow.ToInt64():X}.");
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        => PublishDpi(sender);

    private void PublishDpi(XamlRoot root)
    {
        double dpi = root.RasterizationScale * 96;
        if (publishedDpi == dpi) return;
        publishedDpi = dpi;
        DpiChanged?.Invoke(dpi);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (renderingSuspended)
        {
            _ = DiagnosticsPropertySourceService.TrySetRenderingEnabled(true);
            renderingSuspended = false;
        }
        IsFrozen = false;
        viewportRoot.LayoutUpdated -= OnLayoutUpdated;
        if (viewportRoot.XamlRoot is { } root) root.Changed -= OnXamlRootChanged;
        if (compositionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= compositionRenderingHandler;
            compositionRenderingHandler = null;
        }
        unfreezeCompositionCompleted.Set();
        xamlSource?.Dispose();
        xamlSource = null;
        if (hostWindow != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyWindow(hostWindow);
            hostWindow = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    }
}
