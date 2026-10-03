// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

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

// Reuse VS's WPF/.NET host services for process lifetime and IPC, while supplying
// our own WinUI surface executable and payload staging policy.
public sealed class WinUIHostPlatform : WpfHostPlatform
{
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();

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
        cancelToken.ThrowIfCancellationRequested();
        using Process currentProcess = Process.GetCurrentProcess();
        string initializationData = dataBridge.Serialize(currentProcess.Id);

        WriteDiagnosticTrace($"Surface activation reached: '{path}'.");
        Process surfaceProcess = StartSurfaceProcess(path, tapPath, initializationData);
        if (cancelToken.IsCancellationRequested)
        {
            try { surfaceProcess.Kill(); }
            finally { surfaceProcess.Dispose(); }
            cancelToken.ThrowIfCancellationRequested();
        }
        WriteDiagnosticTrace($"WinUISurface.exe started (PID={surfaceProcess.Id}); pipe initialization data passed.");
        return new Win32SurfaceProcess(surfaceProcess, surfaceProcessId);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    private static Process StartSurfaceProcess(string path, string tapPath, string initializationData)
    {
        using Process currentProcess = Process.GetCurrentProcess();
        // Keep this argument order in sync with Surface.Program.Main: VS host PID,
        // diagnostics TAP path, serialized pipe handles, then the inherited flag.
        string arguments = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} {3}",
            currentProcess.Id,
            WpfHostPlatform.ShellEscape(tapPath),
            WpfHostPlatform.ShellEscape(initializationData),
            GetThreadDpiAwarenessContext().ToInt64());

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
        // The isolated Surface process is outside devenv's probing paths. Pass the
        // discovered VS root so it can resolve the private Designer contract DLLs.
        string? ideDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        string? commonDirectory = ideDirectory is null ? null : Directory.GetParent(ideDirectory)?.FullName;
        string? installRoot = commonDirectory is null ? null : Directory.GetParent(commonDirectory)?.FullName;

        return installRoot is not null && Directory.Exists(
            Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"))
            ? installRoot
            : null;
    }

    private static void WriteDiagnosticTrace(string message)
    {
        Trace.WriteLine($"[WinUIDesigner] {message}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticTracePath)!);
            File.AppendAllText(DiagnosticTracePath, $"{DateTime.UtcNow:O} Host: {message}\r\n");
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
}
