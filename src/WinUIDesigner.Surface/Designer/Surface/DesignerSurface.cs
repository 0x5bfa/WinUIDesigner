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
using WinUIDesigner;
using WinUIDesigner.Surface.Designer.Operations;

namespace WinUIDesigner.Surface.Designer.Surface;

/// <summary>
/// Hosts and positions the document visual tree inside the designer-provided window.
/// Keeps the artboard transform, viewport clipping, and native child-window lifetime aligned
/// while hosting the document inside the Visual Studio-provided HWND.
/// </summary>
internal sealed partial class DesignerSurface : IDisposable
{
    private readonly Grid _viewportRoot;
    private readonly Canvas _transformRoot;
    private readonly ManualResetEventSlim _unfreezeCompositionCompleted = new(initialState: true);
    private FrameworkElement _content;
    private ElementTheme _appRequestedTheme;
    private ElementTheme? _previewRequestedTheme;
    private EventHandler<object>? _compositionRenderingHandler;
    private double _deviceWidth;
    private double _deviceHeight;
    private DesktopWindowXamlSource? _xamlSource;
    private IntPtr _hostWindow;
    private IntPtr _parentWindow;
    private bool _boundsUpdatePending;
    private bool _renderingSuspended;
    private bool _disposed;
    private double _publishedDpi;

    public event EventHandler? BoundsInvalidated;

    public event EventHandler? SurfaceLayoutUpdated;

    public DesignerSurface(FrameworkElement content, double width, double height)
    {
        this._content = content;
        // Keep the HWND-sized viewport separate from the design surface itself.
        // The Visual Studio artboard sends pan/zoom for the design surface; applying
        // that transform to the XamlSource root also moves the viewport and breaks
        // clipping/input coordinates when the artboard is scrolled.

        // Canvas arranges the document at its origin even when d:DesignWidth is
        // smaller than the chosen device. A Grid centers a Stretch child whose
        // explicit width is smaller, disagreeing with the frontend's root origin.
        _viewportRoot = new Grid();
        _transformRoot = (Canvas)XamlReader.Load(
            "<Canvas xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" />");
        _transformRoot.HorizontalAlignment = HorizontalAlignment.Left;
        _transformRoot.VerticalAlignment = VerticalAlignment.Top;
        _transformRoot.Children.Add(content);
        _viewportRoot.Children.Add(_transformRoot);
        _viewportRoot.LayoutUpdated += OnLayoutUpdated;
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.WidthProperty) && double.IsFinite(content.Width))
            XSurfUwp.DT.SetRuntimeWidth(content, content.Width);
        if (!XSurfUwp.DT.IsSizePropertyShadowed(content, FrameworkElement.HeightProperty) && double.IsFinite(content.Height))
            XSurfUwp.DT.SetRuntimeHeight(content, content.Height);
        SetDeviceSize(width, height);
    }

    public FrameworkElement Content => _content;

    public bool IsFrozen { get; private set; }

    public event Action<double>? DpiChanged;

    public void Freeze()
    {
        if (!_renderingSuspended)
        {
            _renderingSuspended = DiagnosticsPropertySourceService.TrySetRenderingEnabled(false);
        }

        IsFrozen = true;
    }

    public void Unfreeze()
    {
        if (!IsFrozen)
        {
            return;
        }

        if (_renderingSuspended && DiagnosticsPropertySourceService.TrySetRenderingEnabled(true))
        {
            _renderingSuspended = false;
        }

        IsFrozen = false;
        ArmUnfreezeCompositionBarrier();
    }

    public bool WaitForUnfreezeComposition(int millisecondsTimeout)
    {
        return _unfreezeCompositionCompleted.Wait(millisecondsTimeout);
    }

    public Rect GetDocumentBounds()
    {
        double width = _content.ActualWidth > 0 ? _content.ActualWidth : _deviceWidth;
        double height = _content.ActualHeight > 0 ? _content.ActualHeight : _deviceHeight;
        return new Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
    }

    public Rect GetContentBounds()
    {
        // Content can render outside its own layout slot; include those descendants
        // so the bounds reported to the artboard cover the pixels users can see.
        Rect bounds = GetDocumentBounds();
        AppendDescendantBounds(_content, _content, ref bounds);

        return bounds;
    }

    public void SetSurfacePosition(IntPtr parentHwnd, int width, int height)
    {
        // SetSurfacePosition supplies the holder HWND and viewport dimensions after the
        // surface has been created; this also reparents the XAML island when needed.
        EnsureIsland();

        if (_parentWindow != parentHwnd)
        {
            nint style = NativeMethods.GetWindowLongPtr(_hostWindow, NativeMethods.GWL_STYLE);
            nint childStyle = (nint)((style.ToInt64() & ~(long)NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS);
            _ = NativeMethods.SetWindowLongPtr(_hostWindow, NativeMethods.GWL_STYLE, childStyle);
            _ = NativeMethods.SetParent(_hostWindow, parentHwnd);
            _parentWindow = parentHwnd;
        }
        // The shipped UWP surface is resized synchronously before VS repositions its
        // adorner child HWND. This message crosses the process boundary, so using
        // HWND_TOP here can run after that step and cover the grid/selection adorners.
        // Keep the remote surface at the bottom of the holder's child z-order.

        _ = NativeMethods.SetWindowPos(_hostWindow, NativeMethods.HWND_BOTTOM, 0, 0, width, height, NativeMethods.SWP_FRAMECHANGED);
        _xamlSource!.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));

        _viewportRoot.Width = Math.Max(1, width);
        _viewportRoot.Height = Math.Max(1, height);
        _viewportRoot.Measure(new Size(_viewportRoot.Width, _viewportRoot.Height));
        _viewportRoot.Arrange(new Rect(0, 0, _viewportRoot.Width, _viewportRoot.Height));
        _viewportRoot.UpdateLayout();

        _ = NativeMethods.InvalidateRect(parentHwnd, IntPtr.Zero, true);

        WinUIDesignerLogger.LogTrace("Surface", $"DesignerSurface positioned: parent=0x{parentHwnd.ToInt64():X}, size={width}x{height}.");
    }

    public void SetPanZoomTransform(double offsetX, double offsetY, double scale)
    {
        scale = scale <= 0 ? 1.0 : scale;

        _transformRoot.RenderTransform = new CompositeTransform()
        {
            ScaleX = scale,
            ScaleY = scale,
            TranslateX = offsetX,
            TranslateY = offsetY,
        };

        WinUIDesignerLogger.LogTrace("Surface", $"DesignerSurface pan/zoom updated: offset={offsetX},{offsetY}, scale={scale}.");
    }

    public void SetDeviceSize(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        _deviceWidth = width;
        _deviceHeight = height;
        _transformRoot.Width = width;
        _transformRoot.Height = height;
        XSurfUwp.DT.SetRootWidth(_content, width);
        XSurfUwp.DT.SetRootHeight(_content, height);
        _transformRoot.Measure(new Size(width, height));
        _transformRoot.Arrange(new Rect(0, 0, width, height));
        _transformRoot.UpdateLayout();

        WinUIDesignerLogger.LogTrace("Surface", $"DesignerSurface device size updated: {width}x{height}.");
    }

    public void ReplaceContent(FrameworkElement newContent)
    {
        if (ReferenceEquals(_content, newContent))
        {
            RefreshLayout();
            return;
        }

        _transformRoot.Children.Remove(_content);
        _content = newContent;
        _transformRoot.Children.Insert(0, newContent);

        if (!XSurfUwp.DT.IsSizePropertyShadowed(_content, FrameworkElement.WidthProperty) && double.IsFinite(_content.Width))
        {
            XSurfUwp.DT.SetRuntimeWidth(_content, _content.Width);
        }

        if (!XSurfUwp.DT.IsSizePropertyShadowed(_content, FrameworkElement.HeightProperty) && double.IsFinite(_content.Height))
        {
            XSurfUwp.DT.SetRuntimeHeight(_content, _content.Height);
        }

        SetDeviceSize(_deviceWidth > 0 ? _deviceWidth : 800, _deviceHeight > 0 ? _deviceHeight : 600);

        WinUIDesignerLogger.LogTrace("Surface", $"DesignerSurface content replaced with {newContent.GetType().FullName}.");
    }

    public void RefreshLayout()
    {
        double width = _deviceWidth > 0 ? _deviceWidth : Math.Max(1, _transformRoot.Width);
        double height = _deviceHeight > 0 ? _deviceHeight : Math.Max(1, _transformRoot.Height);

        _transformRoot.Measure(new Size(width, height));
        _transformRoot.Arrange(new Rect(0, 0, width, height));
        _transformRoot.UpdateLayout();
    }

    private void OnLayoutUpdated(object? sender, object e)
    {
        SurfaceLayoutUpdated?.Invoke(this, EventArgs.Empty);

        if (_boundsUpdatePending || _disposed)
        {
            return;
        }

        _boundsUpdatePending = true;

        if (!_viewportRoot.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _boundsUpdatePending = false;

            if (!_disposed)
            {
                BoundsInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }))
        {
            _boundsUpdatePending = false;
        }
    }

    private void ArmUnfreezeCompositionBarrier()
    {
        if (_compositionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _compositionRenderingHandler;
            _compositionRenderingHandler = null;
        }

        _unfreezeCompositionCompleted.Reset();

        EventHandler<object>? handler = null;

        handler = (_, _) =>
        {
            if (handler is not null)
            {
                CompositionTarget.Rendering -= handler;
            }

            _compositionRenderingHandler = null;
            _unfreezeCompositionCompleted.Set();

            WinUIDesignerLogger.LogTrace("Surface", "DesignerSurface unfreeze composition barrier completed.");
        };

        _compositionRenderingHandler = handler;
        CompositionTarget.Rendering += handler;

        WinUIDesignerLogger.LogTrace("Surface", "DesignerSurface unfreeze composition barrier armed.");
    }

    public void SetCheckerboardColors(Windows.UI.Color color1, Windows.UI.Color color2)
    {
        // The island covers the entire viewport, including the area outside the
        // transformed document. Match SceneScrollViewer.ArtboardBrush there instead
        // of leaving the island's default white background visible in dark previews.
        _viewportRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            (byte)((color1.R >> 1) + (color2.R >> 1)),
            (byte)((color1.G >> 1) + (color2.G >> 1)),
            (byte)((color1.B >> 1) + (color2.B >> 1))));
        // The shared frontend's background toggle sends colors, not a RequestedTheme
        // action. Use its chosen light/dark backdrop as the preview theme, so controls
        // and ThemeResource expressions change together without recreating the tree.

        _previewRequestedTheme = color1.R + color1.G + color1.B >= 3 * 128
            ? ElementTheme.Light
            : ElementTheme.Dark;

        ApplyRequestedTheme();

        WinUIDesignerLogger.LogTrace("Surface", $"DesignerSurface preview theme: colors={color1},{color2}, requested={_previewRequestedTheme}, actual={_viewportRoot.ActualTheme}.");
    }

    public void SetRequestedTheme(ElementTheme theme)
    {
        _appRequestedTheme = theme;

        ApplyRequestedTheme();
    }

    private void ApplyRequestedTheme()
    {
        _viewportRoot.RequestedTheme = _previewRequestedTheme ?? _appRequestedTheme;

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
        if (_xamlSource is not null)
        {
            return;
        }
        // DesktopWindowXamlSource cannot initialize against a message-only parent.
        // Give it a local top-level HWND on this thread, then reparent that HWND into
        // the Visual Studio artboard after the island has been initialized.

        _hostWindow = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            "WinUIDesigner.RemoteSurface",
            NativeMethods.WS_POPUP | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hostWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(_hostWindow));
        _xamlSource.Content = _viewportRoot;

        if (_viewportRoot.XamlRoot is { } root)
        {
            root.Changed += OnXamlRootChanged;

            PublishDpi(root);
        }

        WinUIDesignerLogger.LogTrace("Surface", $"DesktopWindowXamlSource initialized on surface HWND 0x{_hostWindow.ToInt64():X}.");
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        PublishDpi(sender);
    }

    private void PublishDpi(XamlRoot root)
    {
        double dpi = root.RasterizationScale * 96;
        if (_publishedDpi == dpi)
        {
            return;
        }

        _publishedDpi = dpi;
        DpiChanged?.Invoke(dpi);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_renderingSuspended)
        {
            _ = DiagnosticsPropertySourceService.TrySetRenderingEnabled(true);
            _renderingSuspended = false;
        }

        IsFrozen = false;
        _viewportRoot.LayoutUpdated -= OnLayoutUpdated;

        if (_viewportRoot.XamlRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
        }

        if (_compositionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _compositionRenderingHandler;
            _compositionRenderingHandler = null;
        }

        _unfreezeCompositionCompleted.Set();
        _xamlSource?.Dispose();
        _xamlSource = null;

        if (_hostWindow != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyWindow(_hostWindow);
            _hostWindow = IntPtr.Zero;
        }
    }
}
