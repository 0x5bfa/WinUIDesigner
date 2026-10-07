// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner.Surface;

/// <summary>
/// Owns inherited anonymous-pipe handles and provides framed message transport for the surface process.
/// </summary>
internal sealed partial class SurfaceAnonymousPipe : IDisposable
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
        int totalBytesAvailable = 0;
        if (!WaitForCondition(writeEvent, () =>
            {
                if (done)
                {
                    return ConditionResult.Shutdown;
                }

                int bytesRead = 0;
                int bytesLeft = 0;
                if (!NativeMethods.PeekNamedPipe(readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
                {
                    return ConditionResult.Shutdown;
                }

                // PeekNamedPipe is paired with the inherited events to wait until bytes are
                // available before reading the current chunk from the pipe.
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
                        if (!NativeMethods.PeekNamedPipe(readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
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
