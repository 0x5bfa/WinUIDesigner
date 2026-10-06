// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.DesignTools.DesignerHost.Platform;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.Utility;
using MonoMod.RuntimeDetour;
using WinUIDesigner.DesignerHost;
using WinUIDesigner.Platform;

namespace WinUIDesigner;

internal static class WinUIPlatformRegistration
{

    // Visual Studio ships a desktop WinUI configuration but no creator wired to this vsix.
    // Match its stable prefix while allowing SDK/target-version suffixes.
    private const string DesktopWinUISpecificationPrefix =
        "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI";

    private static readonly ConditionalWeakTable<PlatformService, WinUIPlatformCreator> PlatformCreators = new();

    private static Hook? getPlatformCreatorHook;
    private static PlatformConfiguration? modifiedConfiguration;
    private static readonly Dictionary<string, string?> OriginalBindings = [];

    public static void Apply()
    {
        if (getPlatformCreatorHook is not null)
        {
            return;
        }

        string version = FileVersionInfo.GetVersionInfo(typeof(PlatformService).Assembly.Location).FileVersion ?? string.Empty;

        string numericVersion = version.Split('-')[0];
        if (!Version.TryParse(numericVersion, out Version? parsedVersion) || parsedVersion.Major < 17 || !Environment.Is64BitProcess)
        {
            throw new NotSupportedException($"WinUI Designer requires Visual Studio 17.0 or later running as a 64-bit process. Found DesignTools version {version}.");
        }

        WinUIDesignerLogger.LogInformation("VSIX", $"Checking Visual Studio DesignTools {version} in a {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process.");

        // Validate all private entry points before changing process-wide registration.
        _ = typeof(Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views.UwpSceneView).GetField("imageHost", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("UwpSceneView.imageHost");

        MethodInfo method = typeof(PlatformService).GetMethod(
            nameof(PlatformService.GetPlatformCreator), BindingFlags.Public | BindingFlags.Instance, null, [typeof(PlatformIdentifier)], null)
            ?? throw new MissingMethodException(typeof(PlatformService).FullName, nameof(PlatformService.GetPlatformCreator));

        // Use the frontend's existing WinUI configuration. Adding another one can
        // make otherwise identical project contexts resolve to competing creators.
        PlatformConfiguration configuration = PlatformConfigurationService
            .GetConfigurations()
            .SingleOrDefault(candidate => candidate.Specification.StartsWith(DesktopWinUISpecificationPrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Visual Studio's desktop WinUI PlatformConfiguration was not found.");

        foreach (string key in new[] { "PlatformCreatorAssembly", "PlatformCreatorType", "HostPlatformAssembly", "HostPlatformType", "ToolboxPage" })
        {
            OriginalBindings[key] = configuration.Properties.TryGetValue(key, out string? value) ? value : null;
        }

        modifiedConfiguration = configuration;

        try
        {
            configuration.Properties["PlatformCreatorAssembly"] = typeof(WinUIPlatformCreator).Assembly.FullName;
            configuration.Properties["PlatformCreatorType"] = typeof(WinUIPlatformCreator).FullName;
            configuration.Properties["HostPlatformAssembly"] = typeof(WinUIHostPlatform).Assembly.Location;
            configuration.Properties["HostPlatformType"] = typeof(WinUIHostPlatform).FullName;
            configuration.Properties["ToolboxPage"] = typeof(Toolbox.WinUIToolboxItemDiscovery).GUID.ToString("B");

            WinUIDesignerLogger.LogInformation("VSIX", $"Injected WinUI designer bindings into '{configuration.Specification}'.");

            // PlatformService does not consult the configured creator for this runtime
            // in the current VS build. Detour its exact overload as a narrow fallback.
            getPlatformCreatorHook ??= new Hook(method, GetPlatformCreatorHook);
            WinUIDesignerLogger.LogInformation("VSIX", "Installed PlatformService.GetPlatformCreator fallback hook.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static void Dispose()
    {
        getPlatformCreatorHook?.Dispose();
        getPlatformCreatorHook = null;

        if (modifiedConfiguration is not null)
        {
            foreach (var entry in OriginalBindings)
            {
                if (entry.Value is null)
                {
                    modifiedConfiguration.Properties.Remove(entry.Key);
                }
                else
                {
                    modifiedConfiguration.Properties[entry.Key] = entry.Value;
                }
            }
        }

        OriginalBindings.Clear();
        modifiedConfiguration = null;
    }

    private delegate IPlatformCreator? GetPlatformCreatorDelegate(PlatformService instance, PlatformIdentifier platformIdentifier);

    private static IPlatformCreator? GetPlatformCreatorHook(GetPlatformCreatorDelegate original, PlatformService instance, PlatformIdentifier platformIdentifier)
    {
        WinUIDesignerLogger.LogDebug("VSIX", $"GetPlatformCreator called for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");

        if (!string.Equals(platformIdentifier.XamlRuntime, XamlRuntimeNames.WinUI, StringComparison.Ordinal))
        {
            // Leave WPF, UWP, and any future runtime to Visual Studio's own creator.
            return original(instance, platformIdentifier);
        }

        // Cache one creator per PlatformService. The weak table follows VS service
        // lifetime without retaining closed project/platform-service instances.
        var winUICreator = PlatformCreators.GetValue(instance, static platformService => new WinUIPlatformCreator(platformService));

        WinUIDesignerLogger.LogDebug("VSIX", $"Supplied WinUIPlatformCreator for '{platformIdentifier.Identifier}'.");

        return winUICreator;
    }
}
