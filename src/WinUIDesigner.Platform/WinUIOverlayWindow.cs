// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WinUIDesigner.Platform;

internal static class WinUIOverlayWindow
{
    internal static void PositionAtHost(IntPtr overlay, IntPtr host, Rect bounds)
    {
        // The shared designer can also use a top-level overlay. Its own screen
        // positioning remains authoritative; only correct the child HWND variant.
        if ((GetWindowLong(overlay, -16) & 0x40000000) == 0
            || GetParent(overlay) != GetParent(host))
        {
            return;
        }

        // HwndHost supplies device-pixel bounds relative to the common parent.
        // Preserve the shared overlay's z-order and keyboard activation policy.
        SetWindowPos(overlay, IntPtr.Zero,
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
