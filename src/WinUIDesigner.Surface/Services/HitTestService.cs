// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

internal sealed class HitTestService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly int registrationId;

    public HitTestService(ProtocolHandler protocolHandler, DispatcherQueue dispatcherQueue, ObjectIdentityRegistry objectIdentity)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;

        // Visual Studio's HitTestRequest uses System.Windows.Point/Rect even for the
        // platform-neutral wire protocol. Keep the WinUI surface independent of WPF
        // by deserializing the same DataContract JSON shape into local DTOs.
        registrationId = protocolHandler.RegisterMessageObserver<HitTestRequestContract, HitResponse>(526, request =>
        {
            try { return HandleHit(request); }
            catch (Exception ex)
            {
                Program.WriteDiagnosticTrace($"HitTest (526) failed for root {request.RootHandle}: {ex}");
                protocolHandler.PostMessage(529, new UnhandledExceptionResponse { Handle = request.RootHandle, Message = ex.Message, CallStack = ex.ToString() });
                return Empty;
            }
        });
    }

    private HitResponse HandleHit(HitTestRequestContract request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return DispatcherOperation.Invoke(dispatcherQueue, () => HandleHit(request), protocolHandler.CancellationToken);
        }

        if (!objectIdentity.TryGetObject(request.RootHandle, out object? rootObject) || rootObject is not UIElement root)
        {
            return Empty;
        }

        List<HitCandidate> candidates;
        if (request.Point is not null)
        {
            var rootPoint = new Windows.Foundation.Point(request.Point.X, request.Point.Y);
            candidates = HitTestPoint(root, rootPoint);
        }
        else if (request.Rect is not null)
        {
            var rootRect = new Windows.Foundation.Rect(
                request.Rect.X,
                request.Rect.Y,
                request.Rect.Width,
                request.Rect.Height);
            candidates = HitTestRect(root, rootRect);
        }
        else
        {
            candidates = [];
        }

        var hits = candidates
            .Select(candidate => new LiveValueHitEntry
            {
                Element = new LiveValue
                {
                    Handle = objectIdentity.GetHandle(candidate.Element),
                    Type = GetDesignerTypeName(candidate.Element.GetType()),
                    SourceInfo = objectIdentity.GetSourceInfo(candidate.Element),
                },
                IsVisible = candidate.IsVisible,
            })
            .ToList();

        string region = request.Point is not null
            ? $"point={request.Point.X},{request.Point.Y}"
            : request.Rect is not null
                ? $"rect={request.Rect.X},{request.Rect.Y},{request.Rect.Width},{request.Rect.Height}"
                : "region=<none>";
        string hitSummary = string.Join(
            ", ",
            hits.Select(hit =>
            {
                SourceInfo? source = hit.Element?.SourceInfo;
                return $"{hit.Element?.Handle}:{hit.Element?.Type}@{source?.FileName ?? "<none>"}:{source?.LineNumber ?? 0}:{source?.ColumnNumber ?? 0}:visible={hit.IsVisible}";
            }));
        Program.WriteDiagnosticTrace($"HitTest (526) {region}; root={request.RootHandle}; hits=[{hitSummary}].");

        return new HitResponse { Hits = hits };
    }

    private static List<HitCandidate> HitTestPoint(UIElement root, Windows.Foundation.Point point)
    {
        var hits = new List<HitCandidate>();
        if (root.XamlRoot is not null)
        {
            // Message 526 already uses the island/host coordinate space. RootHandle
            // restricts the subtree; it does not make the point root-relative.
            foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(point, root, includeAllElements: true))
                hits.Add(new HitCandidate(element, IsVisibleInTree(element)));
            return hits;
        }
        AppendPointHits(root, root, point, ancestorsVisible: true, hits);
        return hits;
    }

    private static void AppendPointHits(
        UIElement root,
        UIElement element,
        Windows.Foundation.Point point,
        bool ancestorsVisible,
        List<HitCandidate> hits)
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

    private static List<HitCandidate> HitTestRect(UIElement root, Windows.Foundation.Rect rect)
    {
        var hits = new List<HitCandidate>();
        if (root.XamlRoot is not null)
        {
            foreach (UIElement element in VisualTreeHelper.FindElementsInHostCoordinates(rect, root, includeAllElements: true))
                hits.Add(new HitCandidate(element, IsVisibleInTree(element)));
            return hits;
        }
        AppendRectHits(root, root, rect, ancestorsVisible: true, hits);
        return hits;
    }

    private static void AppendRectHits(
        UIElement root,
        UIElement element,
        Windows.Foundation.Rect rect,
        bool ancestorsVisible,
        List<HitCandidate> hits)
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

        if (TryGetBoundsInRoot(root, element, out Windows.Foundation.Rect bounds) && Intersects(bounds, rect))
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

    private static bool Intersects(Windows.Foundation.Rect left, Windows.Foundation.Rect right)
        => left.X <= right.X + right.Width &&
           left.X + left.Width >= right.X &&
           left.Y <= right.Y + right.Height &&
           left.Y + left.Height >= right.Y;

    private static bool IsVisibleInTree(UIElement element)
    {
        for (UIElement? current = element; current is not null; current = VisualTreeHelper.GetParent(current) as UIElement)
            if (current.Visibility != Visibility.Visible || !current.IsHitTestVisible) return false;
        return true;
    }

    private readonly record struct HitCandidate(UIElement Element, bool IsVisible);

    private static string? GetDesignerTypeName(Type type)
        => type.FullName;

    private static HitResponse Empty => new() { Hits = new List<LiveValueHitEntry>() };

    public void Dispose() => protocolHandler.UnregisterMessageObserver(registrationId);

    [DataContract]
    private sealed class HitTestRequestContract
    {
        [DataMember]
        public long RootHandle { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public PointContract? Point { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public RectContract? Rect { get; set; }
    }

    [DataContract]
    private sealed class PointContract
    {
        [DataMember(Name = "_x")]
        public double X { get; set; }

        [DataMember(Name = "_y")]
        public double Y { get; set; }
    }

    [DataContract]
    private sealed class RectContract
    {
        [DataMember(Name = "_x")]
        public double X { get; set; }

        [DataMember(Name = "_y")]
        public double Y { get; set; }

        [DataMember(Name = "_width")]
        public double Width { get; set; }

        [DataMember(Name = "_height")]
        public double Height { get; set; }
    }
}
