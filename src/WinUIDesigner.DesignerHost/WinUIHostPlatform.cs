using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.DesignTools.WpfDesignerHost;

namespace WinUIDesigner.DesignerHost;

public sealed class WinUIHostPlatform : WpfHostPlatform
{
    protected override IShadowCopyWorkerFactory ShadowCopyWorkerFactory { get; } = new WinUIShadowCopyWorkerFactory();

    public WinUIHostPlatform(IServiceProvider serviceProvider, PlatformIdentifier platformIdentifier)
        : base(serviceProvider, platformIdentifier)
    {
        WriteDiagnosticTrace($"WinUIHostPlatform instantiated for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");
    }

    protected override ISurfaceProcess ActivateSurface(
        IServiceProvider serviceProvider,
        Guid surfaceProcessId,
        string path,
        string tapPath,
        IPipeDataBridge dataBridge,
        bool inhibitStartupWatsons,
        CancellationToken cancelToken)
    {
        using Process currentProcess = Process.GetCurrentProcess();
        string initializationData = dataBridge.Serialize(currentProcess.Id);

        WriteDiagnosticTrace($"Surface activation reached: '{path}'.");
        Process surfaceProcess = StartSurfaceProcess(path, tapPath, initializationData);
        WriteDiagnosticTrace($"WinUISurface.exe started (PID={surfaceProcess.Id}); pipe initialization data passed.");
        return new Win32SurfaceProcess(surfaceProcess, surfaceProcessId);
    }

    private static Process StartSurfaceProcess(string path, string tapPath, string initializationData)
    {
        using Process currentProcess = Process.GetCurrentProcess();
        string arguments = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} {3}",
            currentProcess.Id,
            WpfHostPlatform.ShellEscape(tapPath),
            WpfHostPlatform.ShellEscape(initializationData),
            0);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

#if DEBUG
        string? tracePath = Environment.GetEnvironmentVariable("WINUIDESIGNER_TRACE_PATH");
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            process.StartInfo.EnvironmentVariables["WINUIDESIGNER_SURFACE_TRACE_PATH"] = tracePath + ".child.log";
        }
#endif

        string? visualStudioInstallRoot = GetVisualStudioInstallRoot();
        if (!string.IsNullOrWhiteSpace(visualStudioInstallRoot))
        {
            process.StartInfo.EnvironmentVariables["WINUIDESIGNER_VS_INSTALL_ROOT"] = visualStudioInstallRoot;
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                WriteDiagnosticTrace($"WinUISurface stderr: {e.Data}");
            }
        };
        process.Exited += (_, _) =>
        {
            try
            {
                WriteDiagnosticTrace($"WinUISurface.exe exited (PID={process.Id}, ExitCode={process.ExitCode}).");
            }
            catch (InvalidOperationException)
            {
                WriteDiagnosticTrace("WinUISurface.exe exited before its exit code could be read.");
            }
        };

        if (process.Start())
        {
            process.BeginErrorReadLine();
            return process;
        }

        process.Dispose();
        throw new InvalidProgramException(path);
    }

    private static string? GetVisualStudioInstallRoot()
    {
        string? ideDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        string? commonDirectory = ideDirectory is null ? null : Directory.GetParent(ideDirectory)?.FullName;
        string? installRoot = commonDirectory is null ? null : Directory.GetParent(commonDirectory)?.FullName;

        return installRoot is not null && Directory.Exists(
            Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"))
            ? installRoot
            : null;
    }

    [Conditional("DEBUG")]
    private static void WriteDiagnosticTrace(string message)
    {
#if DEBUG
        Trace.WriteLine($"[WinUIDesigner] {message}");

        string? tracePath = Environment.GetEnvironmentVariable("WINUIDESIGNER_TRACE_PATH");
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            try
            {
                File.AppendAllText(tracePath, $"{DateTime.UtcNow:O} Host: {message}\r\n");
            }
            catch (IOException)
            {
                // Cross-process trace writes can race during surface activation.
            }
        }
#endif
    }
}
