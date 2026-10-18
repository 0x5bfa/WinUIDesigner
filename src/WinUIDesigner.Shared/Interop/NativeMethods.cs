// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinUIDesigner;

/// <summary>
/// Declares native APIs and constants used by the Visual Studio extension and isolated surface process.
/// </summary>
/// <remarks>
/// Uses DllImport so these declarations also compile for the VSIX's .NET Framework target.
/// </remarks>
internal static class NativeMethods
{
    // HRESULT values used by native diagnostics and designer protocol responses.
    internal const int S_OK = 0;
    internal const int E_PENDING = unchecked((int)0x8000000A);
    internal const int E_INVALIDARG = unchecked((int)0x80070057);

    // Win32 window styles and the GWL_STYLE index.
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint WS_CLIPSIBLINGS = 0x04000000;
    internal const int GWL_STYLE = -16;

    // SetWindowPos flags and the global-memory allocation flag.
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint GMEM_MOVEABLE = 0x0002;

    // Native values surfaced by Win32 and XAML Diagnostics.
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int XAML_DIAGNOSTICS_VALUE_SOURCE_VISUAL_STATE = 14;

    // Special HWND and DPI-awareness pseudo handles.
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

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

    [DllImport("WinUIDesigner.DiagnosticsTap.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int WinUIDesignerDiagnostics_GetPropertySources(
        IntPtr instance,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 2)] string[] propertyNames,
        uint propertyCount,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] int[] valueSources,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] int[] found);

    [DllImport("WinUIDesigner.DiagnosticsTap.dll")]
    internal static extern int WinUIDesignerDiagnostics_SetRenderingEnabled([MarshalAs(UnmanagedType.Bool)] bool enabled);
}
