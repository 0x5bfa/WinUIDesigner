// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.DesignTools.WpfDesignerHost;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace WinUIDesigner.DesignerHost;

// WinUI and WPF both host desktop XAML surfaces in isolated processes, so WinUI can
// reuse WPF's process-lifetime and IPC services. UWP uses its DesignerAppView-based
// hosting flow and therefore has a separate host implementation.
public sealed class WinUIHostPlatform : WpfHostPlatform
{
    protected override IShadowCopyWorkerFactory ShadowCopyWorkerFactory { get; } = new WinUIShadowCopyWorkerFactory();

    public WinUIHostPlatform(IServiceProvider serviceProvider, PlatformIdentifier platformIdentifier)
        : base(serviceProvider, platformIdentifier)
    {
        WinUIDesignerLogger.LogInformation("Host", $"WinUIHostPlatform instantiated for '{platformIdentifier.Identifier}' (XamlRuntime={platformIdentifier.XamlRuntime}).");
    }

    protected override ISurfaceProcess ActivateSurface(IServiceProvider serviceProvider, Guid surfaceProcessId, string path, string tapPath, IPipeDataBridge dataBridge, bool inhibitStartupWatsons, CancellationToken cancelToken)
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

        var ideDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        var commonDirectory = ideDirectory is null ? null : Directory.GetParent(ideDirectory)?.FullName;
        var installRoot = commonDirectory is null ? null : Directory.GetParent(commonDirectory)?.FullName;

        installRoot = installRoot is not null && Directory.Exists(Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"))
            ? installRoot
            : throw new FileNotFoundException("The Visual Studio installation root did not found.");

        // Keep this argument order in sync with WinUIDesignerSurface.exe!Program.Main:
        // 1. VS host PID
        // 2. Path to the TAP dll
        // 3. serialized pipe initialization data
        // 4. inherited DPI awareness context
        // 5. Path to the VS install root for private contract DLL resolution
        var arguments = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} {3} {4}",
            currentProcess.Id,
            ShellEscape(tapPath),
            ShellEscape(initializationData),
            DpiHelper.GetProcessDpiAwarenessContext().ToInt64(),
            ShellEscape(installRoot));

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
                var exitMessage = $"WinUISurface.exe exited (PID={process.Id}, ExitCode={process.ExitCode}).";

                if (process.ExitCode == 0)
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
}
