using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using Microsoft.VisualStudio.DesignTools.XamlDesignerHost.Platform.ShadowCopy;

namespace WinUIDesigner.DesignerHost;

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

        return new WinUICoreShadowCopyWorker(surfaceProcessInfo, hostProject, controlAssembliesForShadowCopy);
    }
}

internal sealed class WinUICoreShadowCopyWorker : WpfCoreShadowCopyWorker
{
    private const string SurfaceExecutableName = "WinUISurface.exe";

    public WinUICoreShadowCopyWorker(
        SurfaceProcessInfo surfaceInfo,
        IHostProject hostProject,
        IEnumerable<string> controlAssembliesForShadowCopy)
        : base(surfaceInfo, hostProject, controlAssembliesForShadowCopy)
    {
    }

    public override string CopySurfaceProcessPayload(CancellationToken cancelToken)
    {
        // Reuse the existing .NET desktop designer staging behavior (runtime config,
        // project payload conventions, and cache layout), but replace the executable
        // that is launched with the WinUI-specific surface bundled in this VSIX.
        _ = base.CopySurfaceProcessPayload(cancelToken);

        string assemblyDirectory = Path.GetDirectoryName(typeof(WinUICoreShadowCopyWorker).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to locate WinUIDesigner.DesignerHost.");
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

        string? cachedSurface = SurfaceInfo.ShadowCacheContent.FindCachedItem(SurfaceExecutableName);
        if (string.IsNullOrWhiteSpace(cachedSurface))
        {
            throw new FileNotFoundException("WinUISurface.exe was not staged into the designer shadow cache.");
        }

        return cachedSurface;
    }

    public override void EnsureTapAssemblyInFolder(string xamlDiagnosticFolder)
    {
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
