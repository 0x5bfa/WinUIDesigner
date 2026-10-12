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

namespace WinUIDesigner.DesignerHost;

// VS selects a shadow-copy worker from the target framework. WinUI is .NET-only,
// so reject other project kinds before using the Core worker implementation.
internal sealed class WinUIShadowCopyWorkerFactory : IShadowCopyWorkerFactory
{
    // Microsoft.WindowsAppSDK 2.5.1 resolves to this Microsoft.WinUI.dll version.
    // The assembly is bundled inside WinUISurface.exe for VSIX single-file deployment.
    private const string VerifiedWinUIRuntimeFileVersion = "3.0.0.2609";

    public IHostShadowCopyWorker CreateWorker(SurfaceProcessInfo surfaceProcessInfo, IHostProject hostProject, IHostTelemetryService hostTelemetryService, IEnumerable<string> controlAssembliesForShadowCopy)
    {
        if (!string.Equals(surfaceProcessInfo.PlatformIdentifier?.TargetFrameworkIdentifier, ".NETCoreApp", StringComparison.Ordinal))
        {
            throw new NotSupportedException("WinUI Designer currently supports .NETCoreApp projects only.");
        }

        if (!string.Equals(surfaceProcessInfo.PlatformIdentifier?.TargetRuntime, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"WinUI Designer requires a managed project. Runtime={surfaceProcessInfo.PlatformIdentifier?.TargetRuntime}, architecture={surfaceProcessInfo.RuntimeArchitecture}.");
        }

        var payloadArchitecture = string.Equals(surfaceProcessInfo.RuntimeArchitecture, "x64", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(surfaceProcessInfo.RuntimeArchitecture, "amd64", StringComparison.OrdinalIgnoreCase)
            ? "x64"
            : string.Equals(surfaceProcessInfo.RuntimeArchitecture, "arm64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(surfaceProcessInfo.RuntimeArchitecture, "aarch64", StringComparison.OrdinalIgnoreCase)
                ? "arm64"
                : throw new NotSupportedException($"WinUI Designer supports x64 and ARM64 managed projects. Architecture: {surfaceProcessInfo.RuntimeArchitecture}.");

        var extensionDirectory = Path.GetDirectoryName(typeof(WinUIShadowCopyWorkerFactory).Assembly.Location);
        var payloadSurface = Path.Combine(extensionDirectory, $"WinUISurface.{payloadArchitecture}.exe");
        if (!File.Exists(payloadSurface))
        {
            throw new FileNotFoundException("The designer process was not found. Try reinstalling the VSIX.", payloadSurface);
        }

        var projectWinUI = hostProject.References.Select(reference => reference.Path)
            .FirstOrDefault(reference => string.Equals(Path.GetFileName(reference), "Microsoft.WinUI.dll", StringComparison.OrdinalIgnoreCase));
        if (projectWinUI is not null && File.Exists(projectWinUI))
        {
            var actual = FileVersionInfo.GetVersionInfo(projectWinUI).FileVersion;

            if (!string.Equals(VerifiedWinUIRuntimeFileVersion, actual, StringComparison.Ordinal))
            {
                throw new NotSupportedException($"WinUI Designer requires Windows App SDK 2.5.1 (Microsoft.WinUI.dll {VerifiedWinUIRuntimeFileVersion}). Runtime: {actual}");
            }
        }

        return new WinUICoreShadowCopyWorker(surfaceProcessInfo, hostProject, controlAssembliesForShadowCopy, payloadSurface);
    }
}
