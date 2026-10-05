// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly WinUIStaticToolboxItemProvider toolboxItemProvider = new();

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
            WinUIPlatformRegistration.Apply();
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
            WinUIPlatformRegistration.Dispose();
        }

        base.Dispose(disposing);
    }
}
