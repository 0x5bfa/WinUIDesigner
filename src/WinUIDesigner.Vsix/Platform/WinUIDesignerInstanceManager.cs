// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Live;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed class WinUIDesignerInstanceManager(
    ISurfaceProcessMarkupProvider markupProvider, ISurfaceProcessContext surfaceProcessContext, IInstanceBuilderPlatform platform, IProtocolHandler protocolHandler)
    : DesignerInstanceManager(markupProvider, surfaceProcessContext, platform, protocolHandler)
{
    protected override LiveMarkupLinkResult TryLinkDocumentNode(ILiveNode liveNode, bool canDelay)
    {
        LiveMarkupLinkResult result = base.TryLinkDocumentNode(liveNode, canDelay);
        return result;
    }

    protected override Task<CreateSurfaceRequestInfo> CreateRequestAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        bool includeInstanceBuildingActions = SurfaceProcessContext.ResetParseLoadDocumentFailure(targetDocument.DocumentId);
        includeInstanceBuildingActions |= SurfaceProcessContext.InstanceBuildingFailures.HasFailures;

        var request = new CreateSurfaceRequestInfo()
        {
            Document = MakeCreateDocumentInfo(targetDocument, includeInstanceBuildingActions),
        };

        if (MarkupProvider is WinUISurfaceProcessMarkupProvider provider)
        {
            var appXaml = provider.AppXaml;
            if (includeInstanceBuildingActions)
            {
                if (appXaml is null)
                {
                    provider.PrepareApplicationDocumentsForLoading(ignoreAppXbf: true);
                    appXaml = provider.AppXaml;
                }

                if (appXaml is not null)
                {
                    request.Application = MakeCreateDocumentInfo(appXaml, includeInstanceBuildingActions: true);
                }
            }
            else if (appXaml is not null && appXaml.ConstructionState <= ConstructionState.Prepared)
            {
                request.Application = MakeCreateDocumentInfo(appXaml, provider.IsAppXamlEvicted);
            }

            if (provider.DesignTimeResources is { } resources
                && resources.ConstructionState == ConstructionState.Prepared)
            {
                request.DesignTimeResources = MakeCreateDocumentInfo(resources);
            }
        }

        return Task.FromResult(request);
    }
}
