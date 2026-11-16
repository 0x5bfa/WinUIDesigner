// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using System.Windows.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

// Reuse UWP's process lifecycle and protocol; WinUI only replaces the instance
// manager that prepares documents and coordinates app-level preview resources.
// The context also owns the design-time options of its surface process, so a library
// that shares the surface process of its application shares the options as well.
internal sealed class WinUISurfaceProcessContext : UwpSurfaceProcessContext, IDesignTimeTargetProvider, IXamlConditionEvaluationSource
{
    private const string _Category = "Platform";

    private readonly DesignTimeOptionsStore _designTimeOptionsStore;
    private readonly Dispatcher _dispatcher;
    private DesignTimeOptions? _designTimeOptions;
    private bool _isDisposed;

    // The instance manager of the current surface process; Visual Studio creates one for every process it starts.
    private WinUIDesignerInstanceManager? _instanceManager;

    public WinUISurfaceProcessContext(IProjectContext project)
        : base(project)
    {
        _designTimeOptionsStore = new DesignTimeOptionsStore(project);
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Visual Studio restarts the surface when the project file changes, so resolving the
        // options at every start lets an edited MSBuild default take effect.
        SurfaceProcessStarted += OnSurfaceProcessStarted;
    }

    public event EventHandler? DesignTimeOptionsChanged;

    public DesignTimeOptions DesignTimeOptions
    {
        get
        {
            DesignTimeOptions? options = Volatile.Read(ref _designTimeOptions);
            if (options is null)
            {
                // Asked before the first surface start. Later reads return the value resolved at that start.
                options = LoadDesignTimeOptions(out _);
                options = Interlocked.CompareExchange(ref _designTimeOptions, options, null) ?? options;
            }

            return options;
        }
    }

    public void SetDesignTimeOptions(DesignTimeOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!_dispatcher.CheckAccess())
        {
            // The restart below runs on a dispatcher timer of the base class, like Visual Studio's own restarts.
            _ = _dispatcher.InvokeAsync(() => SetDesignTimeOptions(options));
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        if (!WinUICreateSurfaceRequestInfo.IsTransportEnabled)
        {
            WinUIDesignerLogger.LogWarning(_Category, $"The design-time options '{options}' are ignored because the options cannot be sent to the surface.");
            return;
        }

        DesignTimeOptions current = DesignTimeOptions;
        if (options.Equals(current))
        {
            return;
        }

        if (!_designTimeOptionsStore.Save(options))
        {
            WinUIDesignerLogger.LogWarning(_Category, $"The design-time options '{options}' could not be stored; the design surface keeps '{current}'.");
            return;
        }

        Volatile.Write(ref _designTimeOptions, options);
        WinUIDesignerLogger.LogInformation(_Category, $"Design-time options of '{ProjectContext?.ProjectName}' changed from '{current}' to '{options}'; reloading the design surface.");

        RaiseDesignTimeOptionsChanged();

        // The options decide which members are instantiated, and Visual Studio neither cleans a prepared
        // document again nor rebuilds its cleaner context while a surface lives, so reload every designer
        // of the project the way Visual Studio does after a project change.
        RestartAfter(ShortDelay);
    }

    public bool? EvaluateIgnoringOverrides(XamlConditionQuery query)
    {
        try
        {
            return Volatile.Read(ref _instanceManager)?.EvaluateConditionIgnoringOverrides(query);
        }
        catch (Exception exception)
        {
            // Only the user interface asks; it shows the value as unknown.
            WinUIDesignerLogger.LogTrace(_Category, $"Evaluating the condition '{query?.Condition.Text}' for the designer's user interface failed: {exception}");
            return null;
        }
    }

#if VS2022
    protected override DesignerInstanceManager CreateDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler,
        IInstanceBuildingTelemetry telemetry)
#else
    protected override DesignerInstanceManager CreateDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler)
#endif
    {
#if VS2022
        var instanceManager = new WinUIDesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler, telemetry);
#else
        var instanceManager = new WinUIDesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler);
#endif
        Volatile.Write(ref _instanceManager, instanceManager);
        return instanceManager;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // A restart requested after this point would leave the base class's timer running forever.
            _isDisposed = true;
            SurfaceProcessStarted -= OnSurfaceProcessStarted;
            Volatile.Write(ref _instanceManager, null);
        }

        base.Dispose(disposing);
    }

    private void OnSurfaceProcessStarted(object? sender, EventArgs e)
    {
        try
        {
            DesignTimeOptions options = LoadDesignTimeOptions(out string source);
            DesignTimeOptions? previous = Interlocked.Exchange(ref _designTimeOptions, options);

            WinUIDesignerLogger.LogInformation(
                _Category,
                $"Design-time options of the surface for '{ProjectContext?.ProjectName}': {options} "
                    + $"({options.TargetPlatform.DisplayName}, {WindowsVersionCatalog.Describe(options.ContractVersion)}, "
                    + $"{options.ConditionOverrides.Count} conditional XAML condition(s) forced; {source}).");

            if (previous is not null && !previous.Equals(options))
            {
                RaiseDesignTimeOptionsChanged();
            }
        }
        catch (Exception exception)
        {
            // The surface has to start whatever happens to the options.
            WinUIDesignerLogger.LogWarning(_Category, "The design-time options could not be resolved when the surface started.", exception);
        }
    }

    private DesignTimeOptions LoadDesignTimeOptions(out string source)
    {
        if (!WinUICreateSurfaceRequestInfo.IsTransportEnabled)
        {
            // Without the transport the surface previews with the defaults, and this side has to agree with it.
            source = "the options cannot be sent to the surface";
            return DesignTimeOptions.Default;
        }

        return _designTimeOptionsStore.Load(out source);
    }

    private void RaiseDesignTimeOptionsChanged()
    {
        try
        {
            DesignTimeOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            // A failing listener must not keep the design surface from reloading.
            WinUIDesignerLogger.LogWarning(_Category, "A listener of the design-time options failed.", exception);
        }
    }
}
