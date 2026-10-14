// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Live;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed class WinUIDesignerInstanceManager(
    ISurfaceProcessMarkupProvider markupProvider,
    ISurfaceProcessContext surfaceProcessContext,
    IInstanceBuilderPlatform platform,
    IProtocolHandler protocolHandler)
    : DesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler)
{
    protected override LiveMarkupLinkResult TryLinkDocumentNode(ILiveNode liveNode, bool canDelay)
    {
        LiveMarkupLinkResult result = base.TryLinkDocumentNode(liveNode, canDelay);
        SourceInfo? source = liveNode.SourceInfo;

        WinUIDesignerLogger.LogTrace("Platform",
            $"Live markup link: handle={liveNode.Handle}, type={liveNode.TypeFullName}, " +
            $"file={source?.FileName ?? "<null>"}, line={source?.LineNumber ?? 0}, column={source?.ColumnNumber ?? 0}, " +
            $"markup={source?.MarkupHandle ?? 0}, changeVersion={source?.ChangeVersion ?? 0}, canDelay={canDelay}, " +
            $"result={result}." );

        return result;
    }

    protected override Task<CreateSurfaceRequestInfo> CreateRequestAsync(
        ISurfaceProcessDocument targetDocument,
        IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        var provider = MarkupProvider as WinUISurfaceProcessMarkupProvider;

        var projectPath = targetDocument.Document.DocumentContext?.Project?.ProjectPath;
        var projectDirectory = Path.GetDirectoryName(projectPath);
        var appXamlPath = projectDirectory is null ? null : Path.Combine(projectDirectory, "App.xaml");

        if (provider is { AppXaml: null } && appXamlPath is not null && File.Exists(appXamlPath))
        {
            provider.PrepareApplicationDocumentsForLoading(ignoreAppXbf: true);
        }

        bool includeInstanceBuildingActions = SurfaceProcessContext.ResetParseLoadDocumentFailure(targetDocument.DocumentId);
        includeInstanceBuildingActions |= SurfaceProcessContext.InstanceBuildingFailures.HasFailures;

        var request = new CreateSurfaceRequestInfo
        {
            Document = MakeCreateDocumentInfo(targetDocument, includeInstanceBuildingActions),
        };

        if (provider is not null)
        {
            if (provider.AppXaml is { } application)
                request.Application = MakeCreateDocumentInfo(application, includeInstanceBuildingActions);

            if (provider.DesignTimeResources is { } resources)
                request.DesignTimeResources = MakeCreateDocumentInfo(resources, includeInstanceBuildingActions);
        }

        return Task.FromResult(request);
    }
}
