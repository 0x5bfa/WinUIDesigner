// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner.Surface;

/// <summary>
/// Adapts the Visual Studio anonymous-pipe handles to the designer runtime host data-bridge protocol.
/// Wraps the host-provided connection without negotiating a new endpoint.
/// </summary>
internal sealed partial class SurfacePipeDataBridge : IDataBridge, IDisposable
{
    private readonly SurfaceAnonymousPipe readPipe;
    private readonly SurfaceAnonymousPipe writePipe;
    private bool isFirstMessage = true;
    private volatile bool closed;

    public ManualResetEvent ReadyEvent { get; } = new(initialState: true);

    public ManualResetEvent FirstMessageEvent { get; } = new(initialState: false);

    public bool IsConnected => !closed;

    public SurfacePipeDataBridge(string initializationData)
    {
        // The host serializes read event/pipe and write event/pipe handles for each
        // direction. This process takes ownership of all eight inherited handles.
        IntPtr[] handles = [.. initializationData
            .Split([' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => (IntPtr)(long)ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];

        if (handles.Length != 8)
        {
            throw new ArgumentException("Expected eight inherited pipe handles.", nameof(initializationData));
        }

        readPipe = new SurfaceAnonymousPipe(handles[0], handles[1], handles[2], handles[3]);
        writePipe = new SurfaceAnonymousPipe(handles[4], handles[5], handles[6], handles[7]);
    }

    public void Close()
    {
        if (closed) return;
        closed = true;
        readPipe.Close();
        writePipe.Close();
    }

    public byte[] ReadMessage()
    {
        if (closed)
        {
            return null!;
        }

        byte[]? buffer;
        try
        {
            buffer = MessageFrameReader.Read(readPipe.Read);
        }
        catch
        {
            Close();
            throw;
        }

        if (buffer is null)
        {
            Close();
            return null!;
        }

        if (isFirstMessage)
        {
            isFirstMessage = false;
            FirstMessageEvent.Set();

            WinUIDesignerLogger.LogTrace("Surface", "First protocol message received from Visual Studio.");
        }

        return buffer;
    }

    public void WriteMessage(byte[] buffer)
    {
        writePipe.Write(buffer);
    }

    public bool VerifyConnected(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        return !closed;
    }

    public void Dispose()
    {
        Close();
        ReadyEvent.Dispose();
        FirstMessageEvent.Dispose();
        readPipe.Dispose();
        writePipe.Dispose();
        GC.SuppressFinalize(this);
    }

}
