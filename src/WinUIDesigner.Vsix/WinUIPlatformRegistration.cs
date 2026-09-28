// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
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

namespace WinUIDesigner.Vsix;

internal static class WinUIPlatformRegistration
{
#if DEBUG
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();
#endif

    // Visual Studio ships a desktop WinUI configuration but no creator wired to this vsix.
    // Match its stable prefix while allowing SDK/target-version suffixes.
    private const string DesktopWinUISpecificationPrefix =
        "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI";

    private static readonly ConditionalWeakTable<PlatformService, WinUIPlatformCreator> PlatformCreators = new();

    private static Hook? getPlatformCreatorHook;

    public static void Apply()
    {
        // Use the frontend's existing WinUI configuration. Adding another one can
        // make otherwise identical project contexts resolve to competing creators.
        PlatformConfiguration configuration = PlatformConfigurationService
            .GetConfigurations()
            .SingleOrDefault(candidate => candidate.Specification.StartsWith(DesktopWinUISpecificationPrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Visual Studio's desktop WinUI PlatformConfiguration was not found.");

        configuration.Properties["PlatformCreatorAssembly"] = typeof(WinUIPlatformCreator).Assembly.FullName;
        configuration.Properties["PlatformCreatorType"] = typeof(WinUIPlatformCreator).FullName;
        configuration.Properties["HostPlatformAssembly"] = typeof(WinUIHostPlatform).Assembly.Location;
        configuration.Properties["HostPlatformType"] = typeof(WinUIHostPlatform).FullName;

        WriteDiagnosticTrace($"Injected WinUI designer bindings into '{configuration.Specification}'.");

        // PlatformService does not consult the configured creator for this runtime
        // in the current VS build. Detour its exact overload as a narrow fallback.
        MethodInfo method = typeof(PlatformService).GetMethod(
            nameof(PlatformService.GetPlatformCreator), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(PlatformIdentifier) }, null)
            ?? throw new MissingMethodException(typeof(PlatformService).FullName, nameof(PlatformService.GetPlatformCreator));

        getPlatformCreatorHook ??= new Hook(method, GetPlatformCreatorHook);
        WriteDiagnosticTrace("Installed PlatformService.GetPlatformCreator fallback hook.");
    }

    public static void Dispose()
    {
        getPlatformCreatorHook?.Dispose();
        getPlatformCreatorHook = null;
    }

    private delegate IPlatformCreator? GetPlatformCreatorDelegate(PlatformService instance, PlatformIdentifier platformIdentifier);

    private static IPlatformCreator? GetPlatformCreatorHook(GetPlatformCreatorDelegate original, PlatformService instance, PlatformIdentifier platformIdentifier)
    {
        WriteDiagnosticTrace($"GetPlatformCreator called for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");
        if (!string.Equals(platformIdentifier.XamlRuntime, XamlRuntimeNames.WinUI, StringComparison.Ordinal))
        {
            // Leave WPF, UWP, and any future runtime to Visual Studio's own creator.
            return original(instance, platformIdentifier);
        }

        // Cache one creator per PlatformService. The weak table follows VS service
        // lifetime without retaining closed project/platform-service instances.
        WinUIPlatformCreator winUICreator = PlatformCreators.GetValue(instance, static platformService => new WinUIPlatformCreator(platformService));

        WriteDiagnosticTrace($"Supplied WinUIPlatformCreator for '{platformIdentifier.Identifier}'.");
        return winUICreator;
    }

    [Conditional("DEBUG")]
    private static void WriteDiagnosticTrace(string message)
    {
#if DEBUG
        Trace.WriteLine($"[WinUIDesigner] {message}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticTracePath)!);
            File.AppendAllText(DiagnosticTracePath, $"{DateTime.UtcNow:O} VSIX: {message}\r\n");
        }
        catch (IOException)
        {
            // Multiple designer processes write to diagnostic traces.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostic logging must not interrupt designer activation.
        }
#endif
    }

#if DEBUG
    private static string CreateDiagnosticTracePath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        return Path.Combine(basePath, "WinUIDesigner", "Logs", $"WinUIDesigner-{Process.GetCurrentProcess().Id}.log");
    }
#endif
}
