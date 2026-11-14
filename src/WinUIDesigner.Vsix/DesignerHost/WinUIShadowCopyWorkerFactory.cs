// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.ShadowCopy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinUIDesigner.DesignerHost;

// VS selects a shadow-copy worker from the target framework. WinUI is .NET-only,
// so reject other project kinds before using the Core worker implementation.
internal sealed class WinUIShadowCopyWorkerFactory : IShadowCopyWorkerFactory
{
    public IHostShadowCopyWorker CreateWorker(SurfaceProcessInfo surfaceProcessInfo, IHostProject hostProject, IHostTelemetryService hostTelemetryService, IEnumerable<string> controlAssembliesForShadowCopy)
    {
        try
        {
            return CreateWorkerCore(surfaceProcessInfo, hostProject, controlAssembliesForShadowCopy);
        }
        catch (Exception exception)
        {
            // Visual Studio shows this failure on the design surface but writes nothing to this extension's log.
            WinUIDesignerLogger.LogError("Host", $"The surface process for '{hostProject?.ProjectName}' cannot be prepared.", exception);

            throw;
        }
    }

    private static IHostShadowCopyWorker CreateWorkerCore(SurfaceProcessInfo surfaceProcessInfo, IHostProject hostProject, IEnumerable<string> controlAssembliesForShadowCopy)
    {
        // The designer pane normally rejects such projects earlier with a readable message. Repeat the checks
        // here because the surface process can belong to another project than the one that owns the document.
        if (WinUIProjectRequirements.GetUnsupportedReason(surfaceProcessInfo.PlatformIdentifier, hostProject.References?.Select(reference => reference.Path)) is { } reason)
        {
            throw new NotSupportedException(reason);
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

        return new WinUICoreShadowCopyWorker(surfaceProcessInfo, hostProject, controlAssembliesForShadowCopy, payloadSurface);
    }
}
