// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using WinUIDesigner.Protocol;
using WinUIDesigner.Surface.Designer.Shared;
using WinUIDesigner.Surface.Designer.Surface;
using WinUIDesigner.Surface.Runtime;
using WinUIDesigner.Surface.Transport;

namespace WinUIDesigner.Surface;

/// <summary>
/// Initializes the surface process protocol, document services, and XAML metadata providers.
/// Initializes per-process IPC and document services before registering the protocol handler so early host messages have observers.
/// </summary>
public sealed partial class App : Application, IXamlMetadataProvider, IDisposable
{
    private readonly int _hostProcessId;
    private readonly string _tapPath;
    private readonly string _bridgeInitializationData;
    private readonly DispatcherQueue _dispatcherQueue;
    private SurfacePipeDataBridge? _dataBridge;
    private ProtocolHandler? _protocolHandler;
    private ObjectIdentityRegistry? _objectIdentity;
    private SurfaceService? _surfaceService;
    private bool _disposed;

    // The XAML compiler generates this surface app metadata provider in a later build pass.
    // Resolve it at runtime because this source must compile before that generated member exists.
    // This provider describes the designer surface app; project-assembly providers are queried as fallback below.
    private IXamlMetadataProvider GeneratedMetadata =>
        (IXamlMetadataProvider)(this.GetType().GetProperty("_AppProvider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(this)
            ?? throw new InvalidOperationException("The generated XAML metadata provider was not found."));

    public App(int hostProcessId, string tapPath, string bridgeInitializationData, DispatcherQueue dispatcherQueue)
    {
        this._hostProcessId = hostProcessId;
        this._tapPath = tapPath;
        this._bridgeInitializationData = bridgeInitializationData;
        this._dispatcherQueue = dispatcherQueue;

        ResourceManagerRequested += (_, args) => args.CustomResourceManager = ProjectRuntimeResolver.CreateResourceManager();

        InitializeComponent();
    }

    public void Initialize()
    {
        UnhandledException += (_, args) =>
        {
            WinUIDesignerLogger.LogTrace("Surface", $"WinUI unhandled exception: {args.Exception}");
            _protocolHandler?.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse()
            {
                Message = args.Exception.Message,
                CallStack = args.Exception.ToString(),
                IsArtboardException = true,
            });
        };

        WinUIDesignerLogger.LogTrace("Surface", $"Microsoft.UI.Xaml.Application initialized; DispatcherQueue acquired; TAP='{_tapPath}'.");

        _dataBridge = new(_bridgeInitializationData);
        _protocolHandler = new(_dataBridge, tokenSource: null, shouldStart: false);
        _protocolHandler.OnUnhandledException += (_, exception) =>
        {
            WinUIDesignerLogger.LogTrace("Surface", $"Asynchronous protocol request failed: {exception}");

            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse()
            {
                Message = exception.Message,
                CallStack = exception.ToString(),
                IsArtboardException = true,
            });
        };

        _objectIdentity = new();
        _surfaceService = new(_protocolHandler, _dispatcherQueue, _objectIdentity);

        WinUIDesignerLogger.LogTrace("Surface", $"PipeDataBridge, ProtocolHandler, and minimal SurfaceService initialized; message {(int)DesignerMessageId.CreateSurface} registered before protocol start.");

        _protocolHandler.Start();

        WinUIDesignerLogger.LogTrace("Surface", "ProtocolHandler started.");

        // Exit the surface when the host process goes away, even if it never sends an orderly protocol shutdown.
        // WPF has a similar watcher as HostProcessWatcher in WpfSurface.exe.
        // UWP manages its DesignerAppView lifecycle from UwpSurfaceProcess in MS.VS.DT.UwpSurfaceDesigner.dll through DesignerAppManager.
        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    using var hostProcess = Process.GetProcessById(_hostProcessId);
                    while (_protocolHandler is { IsShutdown: false } && !hostProcess.WaitForExit(250))
                    {
                    }
                }
                catch (ArgumentException)
                {
                    // The host exited before the watcher was attached.
                }

                _dispatcherQueue.TryEnqueue(Exit);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    IXamlType? IXamlMetadataProvider.GetXamlType(Type type)
    {
        return GeneratedMetadata.GetXamlType(type) ?? ProjectRuntimeResolver.GetXamlType(type.FullName!);
    }

    IXamlType? IXamlMetadataProvider.GetXamlType(string name)
    {
        return GeneratedMetadata.GetXamlType(name) ?? ProjectRuntimeResolver.GetXamlType(name);
    }

    XmlnsDefinition[] IXamlMetadataProvider.GetXmlnsDefinitions()
    {
        return GeneratedMetadata.GetXmlnsDefinitions().Concat(ProjectRuntimeResolver.GetXmlnsDefinitions()).ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Shutdown cancels pending protocol/dispatcher requests before their
        // document state is released; closing only the pipes leaves them live.
        _disposed = true;
        _protocolHandler?.Shutdown();
        _surfaceService?.Dispose();
        _protocolHandler?.Dispose();
        _dataBridge?.Dispose();
    }
}
