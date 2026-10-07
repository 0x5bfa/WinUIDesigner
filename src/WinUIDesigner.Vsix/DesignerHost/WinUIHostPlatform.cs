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
    protected override IShadowCopyWorkerFactory ShadowCopyWorkerFactory { get; } = new WinUIShadowCopyWorkerFactory();

    public WinUIHostPlatform(IServiceProvider serviceProvider, PlatformIdentifier platformIdentifier)
        : base(serviceProvider, platformIdentifier)
    {
        WinUIDesignerLogger.LogInformation("Host", $"WinUIHostPlatform instantiated for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");
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
        var initializationData = dataBridge.Serialize(currentProcess.Id);

        WinUIDesignerLogger.LogInformation("Host", $"Surface activation reached: '{path}'.");

        Process surfaceProcess = StartSurfaceProcess(path, tapPath, initializationData);

        if (cancelToken.IsCancellationRequested)
        {
            try
            {
                surfaceProcess.Kill();
            }
            finally
            {
                surfaceProcess.Dispose();
            }

            cancelToken.ThrowIfCancellationRequested();
        }

        WinUIDesignerLogger.LogInformation("Host", $"WinUISurface.exe started (PID={surfaceProcess.Id}); pipe initialization data passed.");

        return new Win32SurfaceProcess(surfaceProcess, surfaceProcessId);
    }

    private static Process StartSurfaceProcess(string path, string tapPath, string initializationData)
    {
        using Process currentProcess = Process.GetCurrentProcess();
        // Keep this argument order in sync with Surface.Program.Main: VS host PID,
        // diagnostics TAP path, serialized pipe handles, then the inherited flag.
        var arguments = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} {3}",
            currentProcess.Id,
            WpfHostPlatform.ShellEscape(tapPath),
            WpfHostPlatform.ShellEscape(initializationData),
            NativeMethods.GetThreadDpiAwarenessContext().ToInt64());

        var process = new Process()
        {
            StartInfo = new(path, arguments)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        var visualStudioInstallRoot = GetVisualStudioInstallRoot();
        if (!string.IsNullOrWhiteSpace(visualStudioInstallRoot))
        {
            process.StartInfo.EnvironmentVariables["WINUIDESIGNER_VS_INSTALL_ROOT"] = visualStudioInstallRoot;
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                WinUIDesignerLogger.LogWarning("Host", $"WinUISurface stderr: {e.Data}");
            }
        };
        process.Exited += (_, _) =>
        {
            try
            {
                int exitCode = process.ExitCode;
                var exitMessage = $"WinUISurface.exe exited (PID={process.Id}, ExitCode={exitCode}).";

                if (exitCode == 0)
                {
                    WinUIDesignerLogger.LogInformation("Host", exitMessage);
                }
                else
                {
                    WinUIDesignerLogger.LogError("Host", exitMessage);
                }
            }
            catch (InvalidOperationException)
            {
                WinUIDesignerLogger.LogWarning("Host", "WinUISurface.exe exited before its exit code could be read.");
            }
        };

        if (process.Start())
        {
            process.BeginErrorReadLine();
            return process;
        }

        process.Dispose();

        WinUIDesignerLogger.LogError("Host", $"Failed to start WinUISurface.exe at '{path}'.");

        throw new InvalidProgramException(path);
    }

    private static string? GetVisualStudioInstallRoot()
    {
        // The isolated Surface process is outside devenv's probing paths. Pass the
        // discovered VS root so it can resolve the private Designer contract DLLs.
        var ideDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        var commonDirectory = ideDirectory is null ? null : Directory.GetParent(ideDirectory)?.FullName;
        var installRoot = commonDirectory is null ? null : Directory.GetParent(commonDirectory)?.FullName;

        return installRoot is not null && Directory.Exists(Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"))
            ? installRoot
            : null;
    }
}
