// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.DesignTools.DesignerHost.Platform;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MonoMod.RuntimeDetour;
using WinUIDesigner.DesignerHost;
using WinUIDesigner.Platform;
using WinUIDesigner.Toolbox;

namespace WinUIDesigner;

/// <summary>
/// Register the WinUI runtime with VS's XAML editor, then install the platform
/// creator hook while the shared designer services are available. Load for both
/// solution and no-solution contexts because the editor can initialize either way.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideXamlRuntimeDesigner(XamlRuntimeNames.WinUI)]
[ProvideStaticWinUIToolboxItems]
[ProvideToolboxItemDiscovery("WinUI 3", "WinUIComponents", typeof(WinUIToolboxItemDiscovery), typeof(WinUIToolboxItemCreator), new[] { ".NETCoreApp" }, AppDomainCreatorType = typeof(WinUIToolboxAppDomainControl))]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class WinUIDesignerPackage : AsyncPackage, IVsToolboxItemProvider
{
    // Visual Studio has a desktop WinUI configuration but no creator wired to this VSIX.
    // Match its stable prefix while allowing SDK and target-version suffixes.
    private const string DesktopWinUISpecificationPrefix =
        "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI";

    private readonly WinUIStaticToolboxItemProvider toolboxItemProvider = new();
    private static readonly ConditionalWeakTable<PlatformService, WinUIPlatformCreator> PlatformCreators = new();
    private static readonly Dictionary<string, string?> OriginalBindings = [];

    private static Hook? getPlatformCreatorHook;
    private static PlatformConfiguration? modifiedConfiguration;

    public const string PackageGuidString = "4b134b27-b9ee-4f30-a267-cf19aa49f896";

    int IVsToolboxItemProvider.GetItemContent(string itemId, ushort format, out IntPtr global)
    {
        return toolboxItemProvider.GetItemContent(itemId, format, out global);
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        try
        {
            // Populate VS's existing WinUI configuration before a XAML document
            // asks PlatformService to create the designer backend.
            ApplyPlatformRegistration();
        }
        catch (Exception exception)
        {
            ActivityLog.LogError(nameof(WinUIDesignerPackage), exception.ToString());
            WinUIDesignerLogger.LogCritical("VSIX", "Package initialization failed.", exception);

            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // RuntimeDetour changes a process-wide VS method; restore it with the
            // package lifetime so unloading the extension leaves no active hook.
            RestorePlatformRegistration();
        }

        base.Dispose(disposing);
    }

    private static void ApplyPlatformRegistration()
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

        WinUIDesignerLogger.LogInformation("VSIX", $"Checking Visual Studio DesignTools {version} in a 64-bit process.");

        // Validate all private entry points before changing process-wide registration.
        _ = typeof(Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views.UwpSceneView).GetField("imageHost", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("UwpSceneView.imageHost");

        MethodInfo method = typeof(PlatformService).GetMethod(
            nameof(PlatformService.GetPlatformCreator), BindingFlags.Public | BindingFlags.Instance, null, [typeof(PlatformIdentifier)], null)
            ?? throw new MissingMethodException(typeof(PlatformService).FullName, nameof(PlatformService.GetPlatformCreator));

        // Reuse Visual Studio's existing WinUI configuration to avoid competing creators.
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
            configuration.Properties["ToolboxPage"] = typeof(WinUIToolboxItemDiscovery).GUID.ToString("B");

            WinUIDesignerLogger.LogInformation("VSIX", $"Injected WinUI designer bindings into '{configuration.Specification}'.");

            // This VS build bypasses the configured creator for this runtime, so install a narrow fallback hook.
            getPlatformCreatorHook ??= new Hook(method, GetPlatformCreatorHook);
            WinUIDesignerLogger.LogInformation("VSIX", "Installed PlatformService.GetPlatformCreator fallback hook.");
        }
        catch
        {
            RestorePlatformRegistration();
            throw;
        }
    }

    private static void RestorePlatformRegistration()
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
            // Let Visual Studio handle WPF, UWP, and any future XAML runtime.
            return original(instance, platformIdentifier);
        }

        var creator = PlatformCreators.GetValue(instance, static platformService => new WinUIPlatformCreator(platformService));
        WinUIDesignerLogger.LogDebug("VSIX", $"Supplied WinUIPlatformCreator for '{platformIdentifier.Identifier}'.");
        return creator;
    }
}
