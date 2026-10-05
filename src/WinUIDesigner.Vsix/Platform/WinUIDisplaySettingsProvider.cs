// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.Utility.Globalization;
using Microsoft.VisualStudio.DesignTools.Utility.IO;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PlatformPane;
using Microsoft.Win32;

namespace WinUIDesigner.Platform;

// Retain the UWP SDK device parser, ordering, qualifiers and default settings.
// WinUI projects do not supply UAP's PlatformInstalledPath, so locate that same
// device directory through the installed Windows SDK when necessary.
internal sealed class WinUIDisplaySettingsProvider(IPlatformService platformService)
    : UwpDisplaySettingsProvider(platformService)
{
    protected override string[] GetXmlFilesFromDirectory()
    {
        string[] files = base.GetXmlFilesFromDirectory();
        if (files is { Length: > 0 }) return files;

        using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using RegistryKey? installedRoots = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
        if (installedRoots?.GetValue("KitsRoot10") is not string sdkRoot) return files;

        string devicesDirectory = LocalizationHelper.FindFolderForCulture(
            CultureInfo.CreateSpecificCulture("en-US"), Path.Combine(sdkRoot, @"DesignTime\UAP\Devices"), useLcidFormat: true);
        if (string.IsNullOrEmpty(devicesDirectory) || !Directory.Exists(devicesDirectory)) return files;

        WinUIPlatform.WriteDiagnosticTrace($"UWP device definitions loaded from '{devicesDirectory}'.");
        return AccessHelper.AccessService.DirectoryGetFiles(devicesDirectory, "*.xml");
    }
}
