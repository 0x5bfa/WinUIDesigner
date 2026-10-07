// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner.Surface;

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetParent(nint child, nint newParent);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hwnd, int index, nint newValue);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InvalidateRect(nint hwnd, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekNamedPipe(SafePipeHandle handle, byte[]? buffer, int bufferSize, ref int bytesRead, ref int bytesAvailable, ref int bytesLeftThisMessage);

    [LibraryImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static partial int WinUIDesignerDiagnostics_Initialize();

    [LibraryImport("WinUIDesigner.DiagnosticsTap.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WinUIDesignerDiagnostics_GetPropertySource(nint instance, string propertyName, out int valueSource);

    [LibraryImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static partial int WinUIDesignerDiagnostics_SetRenderingEnabled([MarshalAs(UnmanagedType.Bool)] bool enabled);
}
