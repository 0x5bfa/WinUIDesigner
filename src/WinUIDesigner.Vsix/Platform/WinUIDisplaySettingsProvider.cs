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

/// <summary>
/// Provides display settings for WinUI projects by extending <see cref="UwpDisplaySettingsProvider"/>,
/// which retrieves XML files from the directory specified by the UAP's <c>PlatformInstalledPath</c> property.
/// However, since WinUI projects do not supply that property, this class overrides the method to locate
/// UWP SDK device definitions through the installed Windows SDK instead.
/// </summary>
/// <param name="platformService">The platform service used to access platform-specific functionality.</param>
internal sealed class WinUIDisplaySettingsProvider(IPlatformService platformService)
    : UwpDisplaySettingsProvider(platformService)
{
    protected override string[] GetXmlFilesFromDirectory()
    {
        var files = base.GetXmlFilesFromDirectory(); // Unlikely to work.
        if (files is { Length: > 0 })
        {
            return files;
        }

        using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installedRoots = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
        if (installedRoots?.GetValue("KitsRoot10") is not string sdkRoot)
        {
            return files;
        }

        var devicesDirectory = LocalizationHelper.FindFolderForCulture(CultureInfo.CreateSpecificCulture("en-US"), Path.Combine(sdkRoot, @"DesignTime\UAP\Devices"), useLcidFormat: true);
        if (string.IsNullOrEmpty(devicesDirectory) || !Directory.Exists(devicesDirectory))
        {
            return files;
        }

        WinUIDesignerLogger.LogTrace("Platform", $"UWP device definitions loaded from '{devicesDirectory}'.");

        return AccessHelper.AccessService.DirectoryGetFiles(devicesDirectory, "*.xml");
    }
}
