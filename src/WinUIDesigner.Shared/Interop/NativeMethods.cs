// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner;

/// <summary>
/// Declares native APIs used by the Visual Studio extension and the isolated surface process.
/// </summary>
/// <remarks>
/// Uses DllImport so these declarations also compile for the VSIX's .NET Framework target.
/// </remarks>
internal static class NativeMethods
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    internal static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekNamedPipe(SafePipeHandle handle, [Out] byte[]? buffer, int bufferSize, ref int bytesRead, ref int bytesAvailable, ref int bytesLeftThisMessage);

    [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static extern int WinUIDesignerDiagnostics_Initialize();

    [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static extern int WinUIDesignerDiagnostics_GetPropertySource(IntPtr instance, [MarshalAs(UnmanagedType.LPWStr)] string propertyName, out int valueSource);

    [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static extern int WinUIDesignerDiagnostics_SetRenderingEnabled([MarshalAs(UnmanagedType.Bool)] bool enabled);
}
