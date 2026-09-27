using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideXamlRuntimeDesigner("WinUI")]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class WinUIDesignerPackage : AsyncPackage
{
    public const string PackageGuidString = "4b134b27-b9ee-4f30-a267-cf19aa49f896";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        try
        {
            WinUIPlatformRegistration.Apply();
        }
        catch (Exception exception)
        {
#if DEBUG
            string message = $"[WinUIDesigner] Package initialization failed: {exception}";
            Trace.WriteLine(message);

            string? tracePath = Environment.GetEnvironmentVariable("WINUIDESIGNER_TRACE_PATH");
            if (!string.IsNullOrWhiteSpace(tracePath))
            {
                File.AppendAllText(tracePath, $"{DateTime.UtcNow:O} VSIX: Package initialization failed: {exception}\r\n");
            }
#else
            _ = exception;
#endif

            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WinUIPlatformRegistration.Dispose();
        }

        base.Dispose(disposing);
    }
}
