using System;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner.Surface;

internal sealed class SurfacePipeDataBridge : IDataBridge, IDisposable
{
    private readonly SurfaceAnonymousPipe readPipe;
    private readonly SurfaceAnonymousPipe writePipe;
    private bool isFirstMessage = true;
    private bool closed;

    public ManualResetEvent ReadyEvent { get; } = new(initialState: true);

    public ManualResetEvent FirstMessageEvent { get; } = new(initialState: false);

    public bool IsConnected => !closed;

    public SurfacePipeDataBridge(string initializationData)
    {
        IntPtr[] handles = initializationData
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => (IntPtr)(long)ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();

        if (handles.Length != 8)
        {
            throw new ArgumentException("Expected eight inherited pipe handles.", nameof(initializationData));
        }

        readPipe = new SurfaceAnonymousPipe(handles[0], handles[1], handles[2], handles[3]);
        writePipe = new SurfaceAnonymousPipe(handles[4], handles[5], handles[6], handles[7]);
    }

    public void Close()
    {
        closed = true;
        readPipe.Close();
        writePipe.Close();
    }

    public byte[] ReadMessage()
    {
        byte[] buffer = new byte[4];
        FillBufferFromReadPipe(buffer, 0);
        if (closed)
        {
            return null!;
        }

        int payloadLength = BitConverter.ToInt32(buffer, 0);
        Array.Resize(ref buffer, 4 + payloadLength);
        FillBufferFromReadPipe(buffer, 4);
        if (closed)
        {
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

    private void FillBufferFromReadPipe(byte[] buffer, int startingOffset)
    {
        for (int offset = startingOffset; offset < buffer.Length;)
        {
            int read = readPipe.Read(buffer, offset, buffer.Length - offset);
            if (closed || read == 0)
            {
                return;
            }

            offset += read;
        }
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

    private bool done;
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
        while (waitHandle.WaitOne());

        return false;
    }
}
