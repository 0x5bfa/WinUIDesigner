// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;

namespace WinUIDesigner.Surface.Transport;

/// <summary>
/// Adapts the Visual Studio anonymous-pipe handles to the designer runtime host data-bridge protocol.
/// Wraps the host-provided connection without negotiating a new endpoint.
/// </summary>
internal sealed partial class SurfacePipeDataBridge : IDataBridge, IDisposable
{
    private readonly SurfaceAnonymousPipe _readPipe;
    private readonly SurfaceAnonymousPipe _writePipe;
    private bool _isFirstMessage = true;
    private volatile bool _closed;

    public ManualResetEvent ReadyEvent { get; } = new(initialState: true);

    public ManualResetEvent FirstMessageEvent { get; } = new(initialState: false);

    public bool IsConnected => !_closed;

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

        _readPipe = new SurfaceAnonymousPipe(handles[0], handles[1], handles[2], handles[3]);
        _writePipe = new SurfaceAnonymousPipe(handles[4], handles[5], handles[6], handles[7]);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _readPipe.Close();
        _writePipe.Close();
    }

    public byte[] ReadMessage()
    {
        if (_closed)
        {
            return null!;
        }

        byte[]? buffer;
        try
        {
            buffer = MessageFrameReader.Read(_readPipe.Read);
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

        if (_isFirstMessage)
        {
            _isFirstMessage = false;
            FirstMessageEvent.Set();

            WinUIDesignerLogger.LogTrace("Surface", "First protocol message received from Visual Studio.");
        }

        return buffer;
    }

    public void WriteMessage(byte[] buffer)
    {
        _writePipe.Write(buffer);
    }

    public bool VerifyConnected(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        return !_closed;
    }

    public void Dispose()
    {
        Close();
        ReadyEvent.Dispose();
        FirstMessageEvent.Dispose();
        _readPipe.Dispose();
        _writePipe.Dispose();
        GC.SuppressFinalize(this);
    }

}
