// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinUIDesigner.Surface.Services;

namespace WinUIDesigner.Surface;

// Entry point for the out-of-process WinUI island launched by the VS designer host.
internal static class Program
{
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();
    internal static string ContentDirectory { get; } = GetContentDirectory();

    [STAThread]
    private static int Main(string[] args)
    {
        RegisterVisualStudioAssemblyResolver();

        ProjectRuntimeResolver.Initialize(ContentDirectory);

        WriteDiagnosticTrace($"Main entered with {args.Length} arguments.");

        Assembly surfaceAssembly = typeof(Program).Assembly;

        WriteDiagnosticTrace($"Surface assembly='{surfaceAssembly.Location}'; content directory='{ContentDirectory}'; MVID={surfaceAssembly.ManifestModule.ModuleVersionId:D}.");

        if (args.Length != 4 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hostProcessId))
        {
            WriteDiagnosticTrace("Invalid startup arguments.");
            return 1;
        }

        WriteDiagnosticTrace($"WinUISurface started for host PID {hostProcessId}; TAP='{args[1]}'.");

        SurfaceApplication? surfaceApplication = null;

        try
        {
            if (!long.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long dpiContext))
            {
                throw new ArgumentException("Invalid DPI awareness context.");
            }

            if (SetThreadDpiAwarenessContext((nint)(dpiContext == 0 ? -4 : dpiContext)) == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();

            WriteDiagnosticTrace("CsWinRT COM wrappers initialized; starting Microsoft.UI.Xaml.Application.");

            Application.Start(_ =>
            {
                DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                    ?? throw new InvalidOperationException("WinUI DispatcherQueue was not created for the application thread.");

                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcherQueue));
                surfaceApplication = new SurfaceApplication(hostProcessId, args[1], args[2], dispatcherQueue);
                surfaceApplication.Initialize();
            });

            return 0;
        }
        catch (Exception ex)
        {
            WriteDiagnosticTrace($"Surface startup failed: {ex}");
            return ex.HResult != 0 ? ex.HResult : 1;
        }
        finally
        {
            surfaceApplication?.Dispose();
            WriteDiagnosticTrace("WinUISurface exiting.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    private static void RegisterVisualStudioAssemblyResolver()
    {
        string? installRoot = Environment.GetEnvironmentVariable("WINUIDESIGNER_VS_INSTALL_ROOT");
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return;
        }

        string[] assemblyDirectories =
        [
            Path.Combine(installRoot, "Common7", "IDE", "PrivateAssemblies"),
            Path.Combine(installRoot, "Common7", "IDE", "PublicAssemblies"),
            Path.Combine(installRoot, "Common7", "IDE"),
        ];

        // Private VS contract assemblies are intentionally not copied into the VSIX.
        // Resolve them from the same Visual Studio installation that launched us.
        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            string fileName = $"{assemblyName.Name}.dll";
            foreach (string directory in assemblyDirectories)
            {
                string assemblyPath = Path.Combine(directory, fileName);
                if (File.Exists(assemblyPath))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
                }
            }

            return null;
        };
    }

    internal static void WriteDiagnosticTrace(string message)
    {
        Trace.WriteLine($"[WinUIDesigner] {message}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticTracePath)!);
            File.AppendAllText(DiagnosticTracePath, $"{DateTime.UtcNow:O} Surface: {message}\r\n");
        }
        catch (IOException)
        {
            // Diagnostic logging must not terminate the surface.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostic logging must not terminate the surface.
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
            $"WinUIDesigner-{Environment.ProcessId}.log");
    }

    private static string GetContentDirectory()
    {
        // In single-file mode, AppContext.BaseDirectory can point to the bundle
        // extraction folder. Project assemblies, PRI files, and prepared XAML live
        // beside the shadow-copied WinUISurface.exe instead.
        var executablePath = Environment.ProcessPath;

        return !string.IsNullOrWhiteSpace(executablePath)
            ? Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
    }
}
