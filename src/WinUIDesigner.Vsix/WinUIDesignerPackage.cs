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
[Guid("4b134b27-b9ee-4f30-a267-cf19aa49f896")]
[ProvideXamlRuntimeDesigner(XamlRuntimeNames.WinUI)]
[ProvideStaticWinUIToolboxItems]
[ProvideToolboxItemDiscovery("WinUI 3", "WinUIComponents", typeof(WinUIToolboxItemDiscovery), typeof(WinUIToolboxItemCreator), [".NETCoreApp"], AppDomainCreatorType = typeof(WinUIToolboxAppDomainControl))]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class WinUIDesignerPackage : AsyncPackage, IVsToolboxItemProvider
{
    // Visual Studio has a desktop WinUI configuration but no creator wired to this VSIX.
    // Match its stable prefix while allowing SDK and target-version suffixes.
    private const string _DesktopWinUISpecificationPrefix = "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI";

    private static readonly ConditionalWeakTable<PlatformService, WinUIPlatformCreator> _PlatformCreators = new();

    private static readonly Dictionary<string, string?> _OriginalBindings = [];

    private static Hook? _getPlatformCreatorHook;

    private static PlatformConfiguration? _modifiedConfiguration;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (_getPlatformCreatorHook is not null)
        {
            return;
        }

        try
        {
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
                .SingleOrDefault(candidate => candidate.Specification.StartsWith(_DesktopWinUISpecificationPrefix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Visual Studio's desktop WinUI PlatformConfiguration was not found.");

            foreach (string key in new[] { "PlatformCreatorAssembly", "PlatformCreatorType", "HostPlatformAssembly", "HostPlatformType", "ToolboxPage" })
            {
                _OriginalBindings[key] = configuration.Properties.TryGetValue(key, out string? value) ? value : null;
            }

            _modifiedConfiguration = configuration;

            try
            {
                // Populate VS's existing WinUI configuration before a XAML document
                // asks PlatformService to create the designer backend.
                configuration.Properties["PlatformCreatorAssembly"] = typeof(WinUIPlatformCreator).Assembly.FullName;
                configuration.Properties["PlatformCreatorType"] = typeof(WinUIPlatformCreator).FullName;
                configuration.Properties["HostPlatformAssembly"] = typeof(WinUIHostPlatform).Assembly.Location;
                configuration.Properties["HostPlatformType"] = typeof(WinUIHostPlatform).FullName;
                configuration.Properties["ToolboxPage"] = typeof(WinUIToolboxItemDiscovery).GUID.ToString("B");

                WinUIDesignerLogger.LogInformation("VSIX", $"Injected WinUI designer bindings into '{configuration.Specification}'.");

                // This VS build bypasses the configured creator for this runtime, so install a narrow fallback hook.
                _getPlatformCreatorHook ??= new Hook(method, GetPlatformCreatorHook);
                WinUIDesignerLogger.LogInformation("VSIX", "Installed PlatformService.GetPlatformCreator fallback hook.");
            }
            catch
            {
                RestorePlatformRegistration();
                throw;
            }
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

    private static void RestorePlatformRegistration()
    {
        _getPlatformCreatorHook?.Dispose();
        _getPlatformCreatorHook = null;

        if (_modifiedConfiguration is not null)
        {
            foreach (var entry in _OriginalBindings)
            {
                if (entry.Value is null)
                {
                    _modifiedConfiguration.Properties.Remove(entry.Key);
                }
                else
                {
                    _modifiedConfiguration.Properties[entry.Key] = entry.Value;
                }
            }
        }

        _OriginalBindings.Clear();
        _modifiedConfiguration = null;
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

        var creator = _PlatformCreators.GetValue(instance, static platformService => new WinUIPlatformCreator(platformService));

        WinUIDesignerLogger.LogDebug("VSIX", $"Supplied WinUIPlatformCreator for '{platformIdentifier.Identifier}'.");

        return creator;
    }

    int IVsToolboxItemProvider.GetItemContent(string szItemID, ushort format, out IntPtr pGlobal)
    {
        pGlobal = IntPtr.Zero;

        int separatorIndex = szItemID.IndexOf(",", StringComparison.Ordinal);
        if (separatorIndex == -1)
        {
            return VSConstants.E_FAIL;
        }

        string typeName = szItemID.Substring(0, separatorIndex).Trim();
        string assemblyName = szItemID.Substring(separatorIndex + 1).Trim();
        IDictionary<string, object> properties = ToolEncoder.GetToolProperties(typeName, false, new AssemblyName(assemblyName), "[CreationTool]");

        // This format is shared with UWP, so mark the WinUI SDK and target platform explicitly.
        properties["CreationTypeSdkAppliesTo"] = "WinUI";
        properties["TargetPlatform"] = "Windows, Version=10.0";

        byte[] bytes = ToolEncoder.Encode(properties);
        IntPtr allocation = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, new UIntPtr((uint)bytes.Length));
        IntPtr address = NativeMethods.GlobalLock(allocation);
        try
        {
            Marshal.Copy(bytes, 0, address, bytes.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(allocation);
        }

        pGlobal = allocation;

        return VSConstants.S_OK;
    }
}
