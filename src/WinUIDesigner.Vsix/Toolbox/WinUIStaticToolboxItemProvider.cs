// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.Shell.Interop;
namespace WinUIDesigner.Toolbox;

internal sealed class WinUIStaticToolboxItemProvider : IVsToolboxItemProvider
{
    public int GetItemContent(string itemId, ushort format, out IntPtr global)
    {
        global = IntPtr.Zero;

        if (format != DataFormats.GetDataFormat(WinUIStandardToolboxItems.ClipboardFormat).Id ||
            !WinUIStandardToolboxItems.ContainsItem(itemId))
        {
            return VSConstants.E_INVALIDARG;
        }

        string typeName = itemId.Substring(0, itemId.IndexOf(','));
        var properties = ToolEncoder.GetToolProperties(typeName, false, new AssemblyName(WinUIStandardToolboxItems.AssemblyIdentity), "[CreationTool]");

        // The shared UWP/WinUI format alone does not distinguish the two runtimes.
        // IsToolSupported checks the project's SDK capabilities before accepting it.
        properties["CreationTypeSdkAppliesTo"] = "WinUI";
        properties["TargetPlatform"] = "Windows, Version=10.0";

        byte[] bytes = ToolEncoder.Encode(properties);

        IntPtr allocation = NativeMethods.GlobalAlloc(0x0002, new UIntPtr((uint)bytes.Length));
        if (allocation == IntPtr.Zero)
        {
            return VSConstants.E_OUTOFMEMORY;
        }

        IntPtr address = NativeMethods.GlobalLock(allocation);
        if (address == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(allocation);
            return VSConstants.E_OUTOFMEMORY;
        }

        try
        {
            Marshal.Copy(bytes, 0, address, bytes.Length);
        }
        catch
        {
            NativeMethods.GlobalUnlock(allocation);
            NativeMethods.GlobalFree(allocation);
            throw;
        }

        NativeMethods.GlobalUnlock(allocation);
        global = allocation; // Ownership passes to the Toolbox; it calls GlobalFree.

        WinUIDesignerLogger.LogDebug("Toolbox", $"Static item content: {typeName}.");

        return VSConstants.S_OK;
    }

}
