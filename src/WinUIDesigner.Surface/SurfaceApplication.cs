using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using WinUIDesigner.Surface.Services;

namespace WinUIDesigner.Surface;

public sealed partial class SurfaceApplication : Application, IDisposable
{
    private readonly int hostProcessId;
    private readonly string tapPath;
    private readonly string bridgeInitializationData;
    private readonly DispatcherQueue dispatcherQueue;
    private SurfacePipeDataBridge? dataBridge;
    private ProtocolHandler? protocolHandler;
    private ObjectIdentityRegistry? objectIdentity;
    private SurfaceService? surfaceService;
    private bool disposed;

    public SurfaceApplication(
        int hostProcessId,
        string tapPath,
        string bridgeInitializationData,
        DispatcherQueue dispatcherQueue)
    {
        this.hostProcessId = hostProcessId;
        this.tapPath = tapPath;
        this.bridgeInitializationData = bridgeInitializationData;
        this.dispatcherQueue = dispatcherQueue;
        InitializeComponent();
    }

    public void Initialize()
    {
        UnhandledException += (_, args) => Program.WriteDiagnosticTrace($"WinUI unhandled exception: {args.Exception}");
        Program.WriteDiagnosticTrace($"Microsoft.UI.Xaml.Application initialized; DispatcherQueue acquired; TAP='{tapPath}'.");

        dataBridge = new SurfacePipeDataBridge(bridgeInitializationData);
        protocolHandler = new ProtocolHandler(dataBridge, tokenSource: null, shouldStart: false);
        objectIdentity = new ObjectIdentityRegistry();
        surfaceService = new SurfaceService(protocolHandler, dispatcherQueue, objectIdentity);
        Program.WriteDiagnosticTrace("PipeDataBridge, ProtocolHandler, and minimal SurfaceService initialized; message 516 registered before protocol start.");
        protocolHandler.Start();
        Program.WriteDiagnosticTrace("ProtocolHandler started.");

        _ = Task.Run(WatchHostProcess);
    }

    private void WatchHostProcess()
    {
        try
        {
            using Process hostProcess = Process.GetProcessById(hostProcessId);
            while (protocolHandler is { IsShutdown: false } && !hostProcess.WaitForExit(250))
            {
            }
        }
        catch (ArgumentException)
        {
            // The host exited before the watcher was attached.
        }

        dispatcherQueue.TryEnqueue(() => Exit());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        surfaceService?.Dispose();
        protocolHandler?.Dispose();
        dataBridge?.Dispose();
    }
}
