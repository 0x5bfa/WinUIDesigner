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

// Adapt VS's inherited anonymous-pipe handles to RuntimeHost's IDataBridge framing;
// this wraps the host-provided connection rather than negotiating a new endpoint.
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

            Program.WriteDiagnosticTrace("First protocol message received from Visual Studio.");
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

internal sealed class SurfaceAnonymousPipe : IDisposable
{
    private enum ConditionResult
    {
        Success,
        NotReadyYet,
        Shutdown,
    }

    private volatile bool done;
    private readonly AutoResetEvent readEvent = new(initialState: false);
    private readonly AutoResetEvent writeEvent = new(initialState: false);
    private readonly AnonymousPipeServerStream readPipe;
    private readonly AnonymousPipeClientStream writePipe;
    private readonly int writeBufferSize;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(
        SafePipeHandle handle,
        byte[]? buffer,
        int bufferSize,
        ref int bytesRead,
        ref int bytesAvailable,
        ref int bytesLeftThisMessage);

    public SurfaceAnonymousPipe(IntPtr readEventHandle, IntPtr readPipeHandle, IntPtr writeEventHandle, IntPtr writePipeHandle)
    {
        readEvent.SafeWaitHandle = new SafeWaitHandle(readEventHandle, ownsHandle: true);
        writeEvent.SafeWaitHandle = new SafeWaitHandle(writeEventHandle, ownsHandle: true);
        readPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            new SafePipeHandle(readPipeHandle, ownsHandle: true),
            new SafePipeHandle(writePipeHandle, ownsHandle: true));
        writePipe = new AnonymousPipeClientStream(PipeDirection.Out, readPipe.ClientSafePipeHandle);

        writeBufferSize = writePipe.OutBufferSize != 0 ? writePipe.OutBufferSize : readPipe.InBufferSize;
    }

    public void Close()
    {
        done = true;
        writeEvent.Set();
        readEvent.Set();
    }

    public int Read(byte[] buffer, int offset, int maxBytesToRead)
    {
        // PeekNamedPipe is paired with the inherited events to wait until bytes are
        // available before reading the current chunk from the pipe.
        int totalBytesAvailable = 0;
        if (!WaitForCondition(writeEvent, () =>
            {
                if (done)
                {
                    return ConditionResult.Shutdown;
                }

                int bytesRead = 0;
                int bytesLeft = 0;
                if (!PeekNamedPipe(readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
                {
                    return ConditionResult.Shutdown;
                }

                return totalBytesAvailable > 0 ? ConditionResult.Success : ConditionResult.NotReadyYet;
            }))
        {
            return 0;
        }

        int count = Math.Min(maxBytesToRead, totalBytesAvailable);
        int result = readPipe.Read(buffer, offset, count);
        readEvent.Set();

        return result;
    }

    public void Write(byte[] buffer)
    {
        for (int offset = 0; offset < buffer.Length;)
        {
            int bytesToWrite = buffer.Length - offset;
            if (writeBufferSize != 0)
            {
                int totalBytesAvailable = 0;
                if (!WaitForCondition(readEvent, () =>
                    {
                        if (done)
                        {
                            return ConditionResult.Shutdown;
                        }

                        int bytesRead = 0;
                        int bytesLeft = 0;
                        if (!PeekNamedPipe(readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
                        {
                            return ConditionResult.Shutdown;
                        }

                        return totalBytesAvailable < writeBufferSize ? ConditionResult.Success : ConditionResult.NotReadyYet;
                    }))
                {
                    return;
                }

                bytesToWrite = Math.Min(writeBufferSize - totalBytesAvailable, bytesToWrite);
            }

            writePipe.Write(buffer, offset, bytesToWrite);
            offset += bytesToWrite;
            writeEvent.Set();
        }
    }

    public void Dispose()
    {
        readEvent.Dispose();
        readPipe.Dispose();
        writeEvent.Dispose();
        writePipe.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool WaitForCondition(WaitHandle waitHandle, Func<ConditionResult> condition)
    {
        do
        {
            switch (condition())
            {
                case ConditionResult.Success:
                    return true;
                case ConditionResult.Shutdown:
                    return false;
            }
        }
        while (!done && WaitForSignal(waitHandle));

        return false;
    }

    private static bool WaitForSignal(WaitHandle waitHandle)
    {
        // Recheck the pipe periodically: a terminated peer cannot signal its event.
        _ = waitHandle.WaitOne(250);
        return true;
    }
}
