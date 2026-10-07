// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Windows;

namespace WinUIDesigner.Platform;

internal static class WinUIOverlayWindow
{
    internal static void PositionAtHost(IntPtr overlay, IntPtr host, Rect bounds)
    {
        // The shared designer can also use a top-level overlay. Its own screen
        // positioning remains authoritative; only correct the child HWND variant.
        if ((NativeMethods.GetWindowLong(overlay, -16) & 0x40000000) == 0 || NativeMethods.GetParent(overlay) != NativeMethods.GetParent(host))
        {
            return;
        }

        // HwndHost supplies device-pixel bounds relative to the common parent.
        // Preserve the shared overlay's z-order and keyboard activation policy.
        NativeMethods.SetWindowPos(overlay, IntPtr.Zero,
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE
    }
}
