// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using WinUIDesigner.Surface.Services;

namespace WinUIDesigner.Surface;

// Own the per-process IPC and document services. Register each observer before
// starting ProtocolHandler so VS cannot send an early message to an empty table.
public sealed partial class SurfaceApplication : Application, IXamlMetadataProvider, IDisposable
{
    // The XAML compiler adds this provider after its first compilation pass.
    // Resolve it at runtime so the explicit interface also compiles during pass one.
    private IXamlMetadataProvider GeneratedMetadata
        => (IXamlMetadataProvider)(GetType().GetProperty("_AppProvider", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(this)
            ?? throw new InvalidOperationException("The generated XAML metadata provider was not found."));

    IXamlType? IXamlMetadataProvider.GetXamlType(Type type)
        => GeneratedMetadata.GetXamlType(type) ?? ProjectRuntimeResolver.GetXamlType(type.FullName!);

    IXamlType? IXamlMetadataProvider.GetXamlType(string name)
        => GeneratedMetadata.GetXamlType(name) ?? ProjectRuntimeResolver.GetXamlType(name);

    XmlnsDefinition[] IXamlMetadataProvider.GetXmlnsDefinitions()
        => GeneratedMetadata.GetXmlnsDefinitions().Concat(ProjectRuntimeResolver.GetXmlnsDefinitions()).ToArray();
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
        ResourceManagerRequested += (_, args) => args.CustomResourceManager = ProjectRuntimeResolver.CreateResourceManager();
        InitializeComponent();
    }

    public void Initialize()
    {
        UnhandledException += (_, args) =>
        {
            Program.WriteDiagnosticTrace($"WinUI unhandled exception: {args.Exception}");
            protocolHandler?.PostMessage(529, new UnhandledExceptionResponse
            {
                Message = args.Exception.Message,
                CallStack = args.Exception.ToString(),
                IsArtboardException = true,
            });
        };
        Program.WriteDiagnosticTrace($"Microsoft.UI.Xaml.Application initialized; DispatcherQueue acquired; TAP='{tapPath}'.");

        dataBridge = new SurfacePipeDataBridge(bridgeInitializationData);
        protocolHandler = new ProtocolHandler(dataBridge, tokenSource: null, shouldStart: false);
        protocolHandler.OnUnhandledException += (_, exception) =>
        {
            Program.WriteDiagnosticTrace($"Asynchronous protocol request failed: {exception}");
            protocolHandler.PostMessage(529, new UnhandledExceptionResponse
            {
                Message = exception.Message, CallStack = exception.ToString(), IsArtboardException = true,
            });
        };
        objectIdentity = new ObjectIdentityRegistry();
        surfaceService = new SurfaceService(protocolHandler, dispatcherQueue, objectIdentity);
        Program.WriteDiagnosticTrace("PipeDataBridge, ProtocolHandler, and minimal SurfaceService initialized; message 516 registered before protocol start.");
        protocolHandler.Start();
        Program.WriteDiagnosticTrace("ProtocolHandler started.");

        _ = Task.Run(WatchHostProcess);
    }

    private void WatchHostProcess()
    {
        // The pipes do not own Visual Studio's lifetime. Exit the island when devenv
        // goes away, even if the host never sends an orderly protocol shutdown.
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
        dataBridge?.Close();
        surfaceService?.Dispose();
        protocolHandler?.Dispose();
        dataBridge?.Dispose();
    }
}
