// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO.Pipes;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner.Surface.Transport;

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

    private volatile bool _done;
    private readonly AutoResetEvent _readEvent = new(initialState: false);
    private readonly AutoResetEvent _writeEvent = new(initialState: false);
    private readonly AnonymousPipeServerStream _readPipe;
    private readonly AnonymousPipeClientStream _writePipe;
    private readonly int _writeBufferSize;

    public SurfaceAnonymousPipe(IntPtr readEventHandle, IntPtr readPipeHandle, IntPtr writeEventHandle, IntPtr writePipeHandle)
    {
        _readEvent.SafeWaitHandle = new SafeWaitHandle(readEventHandle, ownsHandle: true);
        _writeEvent.SafeWaitHandle = new SafeWaitHandle(writeEventHandle, ownsHandle: true);
        _readPipe = new AnonymousPipeServerStream(
            PipeDirection.In,
            new SafePipeHandle(readPipeHandle, ownsHandle: true),
            new SafePipeHandle(writePipeHandle, ownsHandle: true));
        _writePipe = new AnonymousPipeClientStream(PipeDirection.Out, _readPipe.ClientSafePipeHandle);

        _writeBufferSize = _writePipe.OutBufferSize != 0 ? _writePipe.OutBufferSize : _readPipe.InBufferSize;
    }

    public void Close()
    {
        _done = true;
        _writeEvent.Set();
        _readEvent.Set();
    }

    public int Read(byte[] buffer, int offset, int maxBytesToRead)
    {
        int totalBytesAvailable = 0;
        if (!WaitForCondition(_writeEvent, () =>
            {
                if (_done)
                {
                    return ConditionResult.Shutdown;
                }

                int bytesRead = 0;
                int bytesLeft = 0;
                if (!NativeMethods.PeekNamedPipe(_readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
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
        int result = _readPipe.Read(buffer, offset, count);
        _readEvent.Set();

        return result;
    }

    public void Write(byte[] buffer)
    {
        for (int offset = 0; offset < buffer.Length;)
        {
            int bytesToWrite = buffer.Length - offset;
            if (_writeBufferSize != 0)
            {
                int totalBytesAvailable = 0;
                if (!WaitForCondition(_readEvent, () =>
                    {
                        if (_done)
                        {
                            return ConditionResult.Shutdown;
                        }

                        int bytesRead = 0;
                        int bytesLeft = 0;
                        if (!NativeMethods.PeekNamedPipe(_readPipe.SafePipeHandle, null, 0, ref bytesRead, ref totalBytesAvailable, ref bytesLeft))
                        {
                            return ConditionResult.Shutdown;
                        }

                        return totalBytesAvailable < _writeBufferSize ? ConditionResult.Success : ConditionResult.NotReadyYet;
                    }))
                {
                    return;
                }

                bytesToWrite = Math.Min(_writeBufferSize - totalBytesAvailable, bytesToWrite);
            }

            _writePipe.Write(buffer, offset, bytesToWrite);
            offset += bytesToWrite;
            _writeEvent.Set();
        }
    }

    public void Dispose()
    {
        _readEvent.Dispose();
        _readPipe.Dispose();
        _writeEvent.Dispose();
        _writePipe.Dispose();
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

            if (_done)
            {
                break;
            }

            // Recheck the pipe periodically: a terminated peer cannot signal its event.
            _ = waitHandle.WaitOne(250);
        }
        while (true);

        return false;
    }
}
