// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Resolves designer hit-test requests against the live WinUI visual tree.
/// </summary>
internal sealed partial class SurfaceService
{
    private HitResponse HandleHitTest(HitTestRequestContract request)
    {
        try
        {
            if (!_dispatcherQueue.HasThreadAccess)
            {
                return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => HandleHitTest(request), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            }

            if (!_objectIdentity.TryGetObject(request.RootHandle, out object? rootObject) || rootObject is not UIElement root)
            {
                return new HitResponse() { Hits = [] };
            }

            List<HitCandidate> candidates;
            if (request.Point is not null)
            {
                var rootPoint = new Windows.Foundation.Point(request.Point.X, request.Point.Y);
                candidates = [];
                if (root.XamlRoot is not null)
                {
                    // HitTest already uses the island/host coordinate space. RootHandle
                    // restricts the subtree; it does not make the point root-relative.
                    foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(rootPoint, root, includeAllElements: true))
                    {
                        candidates.Add(new HitCandidate(element, IsVisibleInTree(element)));
                    }
                }
                else
                {
                    AppendPointHits(root, root, rootPoint, ancestorsVisible: true, candidates);
                }
            }
            else if (request.Rect is not null)
            {
                var rootRect = new Windows.Foundation.Rect(request.Rect.X, request.Rect.Y, request.Rect.Width, request.Rect.Height);
                candidates = [];
                if (root.XamlRoot is not null)
                {
                    foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(rootRect, root, includeAllElements: true))
                    {
                        candidates.Add(new HitCandidate(element, IsVisibleInTree(element)));
                    }
                }
                else
                {
                    AppendRectHits(root, root, rootRect, ancestorsVisible: true, candidates);
                }
            }
            else
            {
                candidates = [];
            }

            var hits = candidates.Select(candidate => new LiveValueHitEntry() { Element = new LiveValue() { Handle = _objectIdentity.GetHandle(candidate.Element), Type = candidate.Element.GetType().FullName, SourceInfo = _objectIdentity.GetSourceInfo(candidate.Element), }, IsVisible = candidate.IsVisible, }).ToList();

#if DEBUG
            if (WinUIDesignerLogger.IsVerboseLoggingEnabled)
            {
                var region = request.Point is not null
                    ? $"point={request.Point.X},{request.Point.Y}"
                    : request.Rect is not null
                        ? $"rect={request.Rect.X},{request.Rect.Y},{request.Rect.Width},{request.Rect.Height}"
                        : "region=<none>";

                var hitSummary = string.Join(
                    ", ",
                    hits.Select(hit =>
                    {
                        SourceInfo? source = hit.Element?.SourceInfo;
                        return $"{hit.Element?.Handle}:{hit.Element?.Type}@{source?.FileName ?? "<none>"}:{source?.LineNumber ?? 0}:{source?.ColumnNumber ?? 0}:visible={hit.IsVisible}";
                    }));

                WinUIDesignerLogger.LogTrace("Surface", $"HitTest ({(int)DesignerMessageId.HitTest}) {region}; root={request.RootHandle}; hits=[{hitSummary}].");
            }
#endif

            return new HitResponse() { Hits = hits };
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", $"HitTest ({(int)DesignerMessageId.HitTest}) failed for root {request.RootHandle}.", ex);

            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse { Handle = request.RootHandle, Message = ex.Message, CallStack = ex.ToString() });

            return new HitResponse() { Hits = [] };
        }
    }

    private static void AppendPointHits(UIElement root, UIElement element, Windows.Foundation.Point point, bool ancestorsVisible, List<HitCandidate> hits)
    {
        bool isVisible = ancestorsVisible && element.Visibility == Visibility.Visible;
        int childCount = VisualTreeHelper.GetChildrenCount(element);

        // Visit frontmost children first so the response follows visual z-order and
        // the shared selection tool sees the topmost candidate first.
        for (int index = childCount - 1; index >= 0; index--)
        {
            if (VisualTreeHelper.GetChild(element, index) is UIElement child)
            {
                AppendPointHits(root, child, point, isVisible, hits);
            }
        }

        if (TryGetBoundsInRoot(root, element, out Windows.Foundation.Rect bounds) && bounds.Contains(point))
        {
            hits.Add(new HitCandidate(element, isVisible && element.IsHitTestVisible));
        }
    }

    private static void AppendRectHits(UIElement root, UIElement element, Windows.Foundation.Rect rect, bool ancestorsVisible, List<HitCandidate> hits)
    {
        bool isVisible = ancestorsVisible && element.Visibility == Visibility.Visible;
        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = childCount - 1; index >= 0; index--)
        {
            if (VisualTreeHelper.GetChild(element, index) is UIElement child)
            {
                AppendRectHits(root, child, rect, isVisible, hits);
            }
        }

        if (TryGetBoundsInRoot(root, element, out Windows.Foundation.Rect bounds) && (bounds.X <= rect.X + rect.Width && bounds.X + bounds.Width >= rect.X && bounds.Y <= rect.Y + rect.Height && bounds.Y + bounds.Height >= rect.Y))
        {
            hits.Add(new HitCandidate(element, isVisible && element.IsHitTestVisible));
        }
    }

    private static bool TryGetBoundsInRoot(UIElement root, UIElement element, out Windows.Foundation.Rect bounds)
    {
        bounds = default;
        double width;
        double height;

        if (element is FrameworkElement frameworkElement)
        {
            width = frameworkElement.ActualWidth;
            height = frameworkElement.ActualHeight;
        }
        else
        {
            Windows.Foundation.Size renderSize = element.RenderSize;
            width = renderSize.Width;
            height = renderSize.Height;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 0 || height < 0)
        {
            return false;
        }

        try
        {
            GeneralTransform transform = element.TransformToVisual(root);
            var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var topRight = transform.TransformPoint(new Windows.Foundation.Point(width, 0));
            var bottomLeft = transform.TransformPoint(new Windows.Foundation.Point(0, height));
            var bottomRight = transform.TransformPoint(new Windows.Foundation.Point(width, height));

            double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
            double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
            double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
            double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
            bounds = new Windows.Foundation.Rect(left, top, right - left, bottom - top);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVisibleInTree(UIElement element)
    {
        for (UIElement? current = element; current is not null; current = VisualTreeHelper.GetParent(current) as UIElement)
        {
            if (current.Visibility != Visibility.Visible || !current.IsHitTestVisible)
            {
                return false;
            }
        }

        return true;
    }
}
