// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WinUIDesigner.Vsix.Toolbox;

namespace WinUIDesigner.Vsix;

/// <summary>
/// Register the WinUI runtime with VS's XAML editor, then install the platform
/// creator hook while the shared designer services are available. Load for both
/// solution and no-solution contexts because the editor can initialize either way.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideXamlRuntimeDesigner("WinUI")]
[ProvideStaticWinUIToolboxItems]
[ProvideToolboxItemDiscovery("WinUI 3", "WinUIComponents", typeof(WinUIToolboxItemDiscovery), typeof(WinUIToolboxItemCreator), new[] { ".NETCoreApp" }, AppDomainCreatorType = typeof(WinUIToolboxAppDomainControl))]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class WinUIDesignerPackage : AsyncPackage, IVsToolboxItemProvider
{
    private readonly WinUIStaticToolboxItemProvider toolboxItemProvider = new();
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();

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
            WriteDiagnosticTrace($"Package initialization failed: {exception}");

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

    private static void WriteDiagnosticTrace(string message)
    {
        Trace.WriteLine($"[WinUIDesigner] {message}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticTracePath)!);
            File.AppendAllText(DiagnosticTracePath, $"{DateTime.UtcNow:O} VSIX: {message}\r\n");
        }
        catch (IOException) { } // Multiple designer processes write to diagnostic traces.
        catch (UnauthorizedAccessException) { } // Diagnostic logging must not interrupt designer activation.
    }

    private static string CreateDiagnosticTracePath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        return Path.Combine(basePath, "WinUIDesigner", "Logs", $"WinUIDesigner-{Process.GetCurrentProcess().Id}.log");
    }
}
