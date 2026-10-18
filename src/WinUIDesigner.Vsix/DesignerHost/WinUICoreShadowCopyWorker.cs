// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.Utility.IO;
using Microsoft.VisualStudio.DesignTools.Xaml.LanguageService;
using Microsoft.VisualStudio.DesignTools.XamlDesignerHost.Platform.ShadowCopy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIDesigner.DesignerHost;

// Reuse the generic desktop shadow-copy mechanics without WpfCore's separate
// WPF payload and framework-dependent runtimeconfig/deps handling.
internal sealed class WinUICoreShadowCopyWorker(SurfaceProcessInfo surfaceInfo, IHostProject hostProject, IEnumerable<string> controlAssembliesForShadowCopy, string payloadSurface)
    : WpfHostShadowCopyWorker(surfaceInfo, hostProject, controlAssembliesForShadowCopy)
{
    private const string _SurfaceRuntimeDirectoryName = "_WinUIDesignerSurfaceRuntime";

    private readonly string[] _projectAssemblies = [.. hostProject.References.Select(reference => reference.Path)
        .Concat([hostProject.TargetAssemblyPath]).Where(path => !string.IsNullOrEmpty(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)];

    private readonly IHostProject _hostProject = hostProject;
    private readonly string _payloadSurface = payloadSurface;

    protected override string RuntimeMoniker => ".NETCoreApp";

    protected override string RuntimeVersion => SurfaceInfo.PlatformIdentifier?.TargetFrameworkVersion?.ToString() ?? string.Empty;

    public override string CopySurfaceProcessPayload(CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();

        if (!File.Exists(_payloadSurface))
        {
            throw new FileNotFoundException($"WinUI surface payload was not found: {_payloadSurface}", _payloadSurface);
        }

        cancelToken.ThrowIfCancellationRequested();
        string relativeSurfacePath = Path.Combine(_SurfaceRuntimeDirectoryName, "WinUISurface.exe");
        SurfaceInfo.ShadowCacheContent.AddItem(_payloadSurface, relativeSurfacePath, forceCopyNow: true);

        // ProjectRuntimeResolver loads these as fallback resource maps when a
        // project/control resource is absent from the surface's primary map.
        foreach (var assembly in _projectAssemblies)
        {
            cancelToken.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(assembly);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var pri in Directory.EnumerateFiles(directory, "*.pri"))
            {
                var target = Path.Combine("ProjectResources", Path.GetFileNameWithoutExtension(assembly), Path.GetFileName(pri));

                SurfaceInfo.ShadowCacheContent.AddItem(pri, target, forceCopyNow: true);
            }
        }

        var cachedSurface = SurfaceInfo.ShadowCacheContent.FindCachedItem(relativeSurfacePath);
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
        foreach (IHostSourceItem item in _hostProject.Items ?? Enumerable.Empty<IHostSourceItem>())
        {
            cancelToken.ThrowIfCancellationRequested();

            if (!MediaFileExtensions.IsMediaFile(item.Path) || !File.Exists(item.Path))
            {
                continue;
            }

            var relativePath = UwpUriResolver.GetDeploymentRelativePath(_hostProject, item.RelativePath, isForRuntime: false)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            SurfaceInfo.ShadowCacheContent.AddItem(item.Path, relativePath, forceCopyNow: true);

        }
    }

    protected override bool ShouldCopy(string file)
    {
        // The self-contained surface does not use the target project's runtime files.
        if (file.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".runtimeconfig.dev.json", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public override void EnsureTapAssemblyInFolder(string xamlDiagnosticFolder)
    {
        // Stage the diagnostics files beside the shadow-copied surface. The attach
        // API needs the matching architecture's WinUI3 TAP payload at startup.
        var sourceFolder = SurfaceInfo.TapAssemblyFolder;
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            sourceFolder = Path.Combine(xamlDiagnosticFolder, SurfaceInfo.RuntimeArchitecture, "WinUI3");
        }

        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            var fileName = Path.GetFileName(file);
            SurfaceInfo.ShadowCacheContent.AddItem(file, fileName, forceCopyNow: true);
        }

        SurfaceInfo.TapAssemblyFolder = SurfaceInfo.ShadowCacheContent.ShadowCacheFolder;
    }
}
