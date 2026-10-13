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
        if ((NativeMethods.GetWindowLong(overlay, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) == 0 || NativeMethods.GetParent(overlay) != NativeMethods.GetParent(host))
        {
            return;
        }

        // HwndHost supplies device-pixel bounds relative to the common parent.
        // Preserve the shared overlay's z-order and keyboard activation policy.
        NativeMethods.SetWindowPos(overlay, IntPtr.Zero,
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }
}
