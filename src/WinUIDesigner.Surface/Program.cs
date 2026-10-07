// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.Surface;

/// <summary>
/// Starts the isolated WinUI surface process and initializes its runtime environment.
/// Serves as the entry point launched out of process by the Visual Studio designer host.
/// </summary>
internal static class Program
{
    internal static string ContentDirectory { get; } = GetContentDirectory();

    [STAThread]
    private static int Main(string[] args)
    {
        RegisterVisualStudioAssemblyResolver();

        ProjectRuntimeResolver.Initialize(ContentDirectory);

        WinUIDesignerLogger.LogTrace("Surface", $"Main entered with {args.Length} arguments.");

        Assembly surfaceAssembly = typeof(Program).Assembly;

        WinUIDesignerLogger.LogTrace("Surface", $"Surface assembly='{surfaceAssembly.Location}'; content directory='{ContentDirectory}'; MVID={surfaceAssembly.ManifestModule.ModuleVersionId:D}.");

        if (args.Length != 4 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hostProcessId))
        {
            WinUIDesignerLogger.LogTrace("Surface", "Invalid startup arguments.");
            return 1;
        }

        WinUIDesignerLogger.LogTrace("Surface", $"WinUISurface started for host PID {hostProcessId}; TAP='{args[1]}'.");

        App? app = null;

        try
        {
            if (!long.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long dpiContext))
            {
                throw new ArgumentException("Invalid DPI awareness context.");
            }

            if (NativeMethods.SetThreadDpiAwarenessContext((nint)(dpiContext == 0 ? -4 : dpiContext)) == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();

            WinUIDesignerLogger.LogTrace("Surface", "CsWinRT COM wrappers initialized; starting Microsoft.UI.Xaml.Application.");

            Application.Start(_ =>
            {
                DispatcherQueue dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                    ?? throw new InvalidOperationException("WinUI DispatcherQueue was not created for the application thread.");

                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcherQueue));
                app = new App(hostProcessId, args[1], args[2], dispatcherQueue);
                app.Initialize();
            });

            return 0;
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Surface", $"Surface startup failed: {ex}");
            return ex.HResult != 0 ? ex.HResult : 1;
        }
        finally
        {
            app?.Dispose();
            WinUIDesignerLogger.LogTrace("Surface", "WinUISurface exiting.");
        }
    }

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
            // Diagnostic logging must not terminate the surface.
            // Diagnostic logging must not terminate the surface.

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
