// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.XamlDesignerHost.Platform.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.Utility.IO;
using Microsoft.VisualStudio.DesignTools.Xaml.LanguageService;

using WinUIDesigner.Vsix;

namespace WinUIDesigner.DesignerHost;

// VS selects a shadow-copy worker from the target framework. WinUI is .NET-only,
// so reject other project kinds before using the Core worker implementation.
internal sealed class WinUIShadowCopyWorkerFactory : IShadowCopyWorkerFactory
{
    public IHostShadowCopyWorker CreateWorker(
        SurfaceProcessInfo surfaceProcessInfo,
        IHostProject hostProject,
        IHostTelemetryService hostTelemetryService,
        IEnumerable<string> controlAssembliesForShadowCopy)
    {
        if (!string.Equals(surfaceProcessInfo.PlatformIdentifier?.TargetFrameworkIdentifier, ".NETCoreApp", StringComparison.Ordinal))
        {
            throw new NotSupportedException("WinUI Designer currently supports .NETCoreApp projects only.");
        }
        if (!string.Equals(surfaceProcessInfo.PlatformIdentifier?.TargetRuntime, "Managed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(surfaceProcessInfo.RuntimeArchitecture, "x64", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"WinUI Designer requires a managed x64 project. Runtime={surfaceProcessInfo.PlatformIdentifier?.TargetRuntime}, architecture={surfaceProcessInfo.RuntimeArchitecture}.");
        string payloadWinUI = Path.Combine(Path.GetDirectoryName(typeof(WinUIShadowCopyWorkerFactory).Assembly.Location)!, "Surface", "Microsoft.WinUI.dll");
        string? projectWinUI = hostProject.References.Select(reference => reference.Path)
            .FirstOrDefault(reference => string.Equals(Path.GetFileName(reference), "Microsoft.WinUI.dll", StringComparison.OrdinalIgnoreCase));
        if (!File.Exists(payloadWinUI)) throw new FileNotFoundException("The designer WinUI runtime was not installed.", payloadWinUI);
        if (projectWinUI is not null && File.Exists(projectWinUI))
        {
            string? expected = FileVersionInfo.GetVersionInfo(payloadWinUI).FileVersion;
            string? actual = FileVersionInfo.GetVersionInfo(projectWinUI).FileVersion;
            if (expected != actual)
                throw new NotSupportedException($"The project WinUI runtime ({actual}) differs from the verified designer runtime ({expected}). Use Microsoft.WindowsAppSDK 2.5.1 for this designer build.");
        }

        return new WinUICoreShadowCopyWorker(surfaceProcessInfo, hostProject, controlAssembliesForShadowCopy);
    }
}

internal sealed class WinUICoreShadowCopyWorker : WpfCoreShadowCopyWorker
{
    private const string SurfaceExecutableName = "WinUISurface.exe";
    private readonly string[] projectAssemblies;
    private readonly IHostProject hostProject;

    public WinUICoreShadowCopyWorker(
        SurfaceProcessInfo surfaceInfo,
        IHostProject hostProject,
        IEnumerable<string> controlAssembliesForShadowCopy)
        : base(surfaceInfo, hostProject, controlAssembliesForShadowCopy)
    {
        this.hostProject = hostProject;
        projectAssemblies = hostProject.References.Select(reference => reference.Path)
            .Concat(new[] { hostProject.TargetAssemblyPath }).Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public override string CopySurfaceProcessPayload(CancellationToken cancelToken)
    {
        // Reuse the existing .NET desktop designer staging behavior (runtime config,
        // project payload conventions, and cache layout), but replace the executable
        // that is launched with the WinUI-specific surface bundled in this VSIX.
        _ = base.CopySurfaceProcessPayload(cancelToken);

        string assemblyDirectory = Path.GetDirectoryName(typeof(WinUICoreShadowCopyWorker).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to locate the WinUIDesigner VSIX assembly.");
        string payloadDirectory = Path.Combine(assemblyDirectory, "Surface");
        if (!Directory.Exists(payloadDirectory))
        {
            throw new DirectoryNotFoundException($"WinUI surface payload was not found: {payloadDirectory}");
        }

        foreach (string file in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories))
        {
            cancelToken.ThrowIfCancellationRequested();
            string relativePath = file.Substring(payloadDirectory.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            SurfaceInfo.ShadowCacheContent.AddItem(file, relativePath, forceCopyNow: true);
        }

        // WPF's Core worker copies assemblies, but WinUI control libraries also
        // carry compiled templates in a sibling PRI. Keep each library's index
        // separate from the surface's primary resource index.
        foreach (string assembly in projectAssemblies)
        {
            cancelToken.ThrowIfCancellationRequested();
            string? directory = Path.GetDirectoryName(assembly);
            if (!Directory.Exists(directory)) continue;
            foreach (string pri in Directory.EnumerateFiles(directory, "*.pri"))
            {
                string target = Path.Combine("ProjectResources", Path.GetFileNameWithoutExtension(assembly), Path.GetFileName(pri));
                SurfaceInfo.ShadowCacheContent.AddItem(pri, target, forceCopyNow: true);
            }
        }

        string? cachedSurface = SurfaceInfo.ShadowCacheContent.FindCachedItem(SurfaceExecutableName);
        if (string.IsNullOrWhiteSpace(cachedSurface))
        {
            throw new FileNotFoundException("WinUISurface.exe was not staged into the designer shadow cache.");
        }

        return cachedSurface;
    }

    public override async Task CopyProjectContentAsync(CancellationToken cancelToken)
    {
        await base.CopyProjectContentAsync(cancelToken).ConfigureAwait(false);

        // UWP stages media even in platform-only mode. WPF's worker only copies
        // the output directory in All mode, so it cannot provide these assets.
        foreach (IHostSourceItem item in hostProject.Items ?? Enumerable.Empty<IHostSourceItem>())
        {
            cancelToken.ThrowIfCancellationRequested();
            if (!MediaFileExtensions.IsMediaFile(item.Path) || !File.Exists(item.Path)) continue;
            string relativePath = UwpUriResolver.GetDeploymentRelativePath(hostProject, item.RelativePath, isForRuntime: false)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            SurfaceInfo.ShadowCacheContent.AddItem(item.Path, relativePath, forceCopyNow: true);
            WinUIDesignerLogger.LogInformation("Host", $"Project media staged: '{item.Path}' -> '{relativePath}'; mode={SurfaceInfo.ShadowCopyType}.");
        }
    }

    public override void EnsureTapAssemblyInFolder(string xamlDiagnosticFolder)
    {
        // Stage the diagnostics files beside the shadow-copied surface. The attach
        // API needs the matching architecture's WinUI3 TAP payload at startup.
        string sourceFolder = SurfaceInfo.TapAssemblyFolder;
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = Path.Combine(xamlDiagnosticFolder, SurfaceInfo.RuntimeArchitecture, "WinUI3");
        }

        foreach (string file in Directory.EnumerateFiles(sourceFolder))
        {
            string fileName = Path.GetFileName(file);
            SurfaceInfo.ShadowCacheContent.AddItem(file, fileName, forceCopyNow: true);
        }

        SurfaceInfo.TapAssemblyFolder = SurfaceInfo.ShadowCacheContent.ShadowCacheFolder;
    }
}
