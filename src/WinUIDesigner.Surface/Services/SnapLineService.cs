// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Calculates designer alignment guides from WinUI layout bounds.
/// Reports positions relative to the requested container, not the screen or its window handle.
/// </summary>
internal sealed partial class SnapLineService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly int snapLinesRegistrationId;
    private readonly int elementDataRegistrationId;

    public SnapLineService(ProtocolHandler protocolHandler, DispatcherQueue dispatcherQueue, ObjectIdentityRegistry objectIdentity)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;

        snapLinesRegistrationId = protocolHandler.RegisterMessageObserver<GetSnapLinesRequest, GetSnapLinesResponse>((int)DesignerMessageId.GetSnapLines, HandleGetSnapLines);
        elementDataRegistrationId = protocolHandler.RegisterMessageObserver<GetElementSnapDataRequest, GetElementSnapDataResponse>((int)DesignerMessageId.GetElementSnapData, HandleGetElementSnapData);
    }

    private GetSnapLinesResponse HandleGetSnapLines(GetSnapLinesRequest request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleGetSnapLines(request), GetSnapLinesResponse.Empty);
        }

        if (!TryGetElement(request.ContainerHandle, out FrameworkElement? container))
        {
            return GetSnapLinesResponse.Empty;
        }

        var ignored = new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance);
        foreach (long handle in request.IgnoredHandles ?? [])
        {
            if (TryGetElement(handle, out FrameworkElement? element) && element is not null)
            {
                ignored.Add(element);
            }
        }

        TryGetElement(request.TargetHandle, out FrameworkElement? target);
        var lines = new List<SingleSnapLine>();
        AddSnapLines(lines, container!, container!, true);
        if (container is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement element && !ReferenceEquals(element, target) && !ignored.Contains(element))
                {
                    AddSnapLines(lines, element, container, false);
                }
            }
        }

        WinUIDesignerLogger.LogTrace("Surface", $"GetSnapLines ({(int)DesignerMessageId.GetSnapLines}) returned {lines.Count} line(s) for container handle {request.ContainerHandle}.");

        return new GetSnapLinesResponse { SnapLines = lines };
    }

    private GetElementSnapDataResponse HandleGetElementSnapData(GetElementSnapDataRequest request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleGetElementSnapData(request), new GetElementSnapDataResponse());
        }

        if (!TryGetElement(request.TargetHandle, out FrameworkElement? element) || element is null || VisualTreeHelper.GetParent(element) is not FrameworkElement container || !TryGetBounds(element, container, out Rect bounds))
        {
            return new GetElementSnapDataResponse();
        }

        return new GetElementSnapDataResponse { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom, Baseline = TryGetBaseline(element, container) };
    }

    private static void AddSnapLines(List<SingleSnapLine> lines, FrameworkElement element, FrameworkElement container, bool isContainer)
    {
        if (element.Visibility == Visibility.Collapsed || !TryGetBounds(element, container, out Rect bounds))
        {
            return;
        }

        SingleSnapLineFlags flags = isContainer ? SingleSnapLineFlags.Container : SingleSnapLineFlags.None;

        double centerX = bounds.Left + bounds.Width / 2;
        double centerY = bounds.Top + bounds.Height / 2;
        lines.Add(new SingleSnapLine(bounds.Top, bounds.Left, bounds.Right, flags));
        lines.Add(new SingleSnapLine(centerY, bounds.Left, bounds.Right, flags | SingleSnapLineFlags.Center));
        lines.Add(new SingleSnapLine(bounds.Bottom, bounds.Left, bounds.Right, flags | SingleSnapLineFlags.Maximum));
        lines.Add(new SingleSnapLine(bounds.Left, bounds.Top, bounds.Bottom, flags | SingleSnapLineFlags.Vertical));
        lines.Add(new SingleSnapLine(centerX, bounds.Top, bounds.Bottom, flags | SingleSnapLineFlags.Vertical | SingleSnapLineFlags.Center));
        lines.Add(new SingleSnapLine(bounds.Right, bounds.Top, bounds.Bottom, flags | SingleSnapLineFlags.Vertical | SingleSnapLineFlags.Maximum));
        // Only TextBlock exposes a baseline in this implementation; all elements
        // still receive edge and center guides above.

        double? baseline = TryGetBaseline(element, container);
        if (baseline.HasValue)
        {
            lines.Add(new SingleSnapLine(baseline.Value, bounds.Left, bounds.Right, SingleSnapLineFlags.Baseline));
        }
    }

    private static bool TryGetBounds(FrameworkElement element, FrameworkElement container, out Rect bounds)
    {
        try
        {
            GeneralTransform transform = element.TransformToVisual(container);
            Point origin = transform.TransformPoint(new Point(0, 0));
            Point corner = transform.TransformPoint(new Point(element.ActualWidth, element.ActualHeight));
            Point topRight = transform.TransformPoint(new Point(element.ActualWidth, 0));
            Point bottomLeft = transform.TransformPoint(new Point(0, element.ActualHeight));
            double left = Math.Min(Math.Min(origin.X, corner.X), Math.Min(topRight.X, bottomLeft.X));
            double top = Math.Min(Math.Min(origin.Y, corner.Y), Math.Min(topRight.Y, bottomLeft.Y));
            double right = Math.Max(Math.Max(origin.X, corner.X), Math.Max(topRight.X, bottomLeft.X));
            double bottom = Math.Max(Math.Max(origin.Y, corner.Y), Math.Max(topRight.Y, bottomLeft.Y));
            bounds = new Rect(left, top, right - left, bottom - top);

            return true;
        }
        catch
        {
            bounds = default;

            return false;
        }
    }

    private static double? TryGetBaseline(FrameworkElement element, FrameworkElement container)
    {
        return element is TextBlock textBlock && TryGetBounds(element, container, out Rect bounds) ? bounds.Top + textBlock.BaselineOffset : null;
    }

    private bool TryGetElement(long handle, out FrameworkElement? element)
    {
        if (objectIdentity.TryGetObject(handle, out object? value) && value is FrameworkElement frameworkElement)
        {
            element = frameworkElement;

            return true;
        }

        element = null;

        return false;
    }

    private T InvokeOnDispatcher<T>(Func<T> action, T fallback)
    {
        try
        {
            return DispatcherOperation.Invoke(dispatcherQueue, action, protocolHandler.CancellationToken);
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Surface", $"Snap request failed: {ex}");

            protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse { Message = ex.Message, CallStack = ex.ToString() });

            return fallback;
        }
    }

    public void Dispose()
    {
        protocolHandler.UnregisterMessageObserver(snapLinesRegistrationId);
        protocolHandler.UnregisterMessageObserver(elementDataRegistrationId);
    }
}
