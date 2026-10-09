// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
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
    private static readonly FieldInfo ImageHostField = typeof(UwpSceneView).GetField("imageHost", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(UwpSceneView).FullName, "imageHost");

    protected override Artboard CreateArtboard()
    {
        PlatformSurface = new IsolatedSurface();

        // Use WinUI-specific isolated image host instance so that the artboard can render WinUI content via WinUIHwndHost.
        var imageHost = new WinUIIsolatedImageHost(this);
        ImageHostField.SetValue(this, imageHost);

        WinUIDesignerLogger.LogTrace("Platform", "Minimal WinUI isolated image host created.");

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

        WinUIDesignerLogger.LogTrace("Platform",
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
                WinUIDesignerLogger.LogTrace("Platform",
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

            WinUIDesignerLogger.LogTrace("Platform",
                $"ArtboardLoaded before update: activeTool={DesignerContext.ToolManager.ActiveTool?.GetType().FullName ?? "<null>"}, " +
                $"eventRouter={EventRouter?.GetType().FullName ?? "<null>"}, " +
                $"activeBehavior={EventRouter?.ActiveBehavior?.GetType().FullName ?? "<null>"}.");

            EnsureActiveViewUpdated();

            ViewModel.SchedulePipelineTasks(
                viewSwitched: true,
                DocumentPipelineUpdateInfo.CreateFromViewModel(ViewModel, SceneUpdateStates.None));

            WinUIDesignerLogger.LogTrace("Platform",
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
            WinUIDesignerLogger.LogTrace("Platform",
                $"Presentation source raw mouse: hwnd=0x{hwnd.ToInt64():X}, msg=0x{msg:X}, " +
                $"wParam=0x{wParam.ToInt64():X}, lParam=0x{lParam.ToInt64():X}, handled={handled}.");
        }

        return IntPtr.Zero;
    }

    private void PresentationRoot_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        WinUIDesignerLogger.LogTrace("Platform",
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

            WinUIDesignerLogger.LogTrace("Platform",
                $"{prefix} coordinates: artboard={artboardPoint}, viewRoot={viewRootPoint}, content={contentPoint}, " +
                $"contentToArtboard={contentToArtboard}, artboardToHitRoot={artboardToHitRoot}, " +
                $"zoom={Artboard.Zoom}, viewRootToArtboardScale={Artboard.ViewRootToArtboardScale}, " +
                $"surfaceDpiAdjustmentScale={Artboard.SurfaceProcessDpiAdjustmentScale}.");
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Platform", $"{prefix} coordinate diagnostic failed.", ex);
        }
#endif
    }
}
