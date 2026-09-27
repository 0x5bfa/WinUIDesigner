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
#if DEBUG
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();
#endif

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
            WriteDiagnosticTrace($"Package initialization failed: {exception}");
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

#if DEBUG
    [Conditional("DEBUG")]
    private static void WriteDiagnosticTrace(string message)
    {
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
    }

    private static string CreateDiagnosticTracePath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        return Path.Combine(
            basePath,
            "WinUIDesigner",
            "Logs",
            $"WinUIDesigner-{Process.GetCurrentProcess().Id}.log");
    }
#endif
}
