using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Live;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed class WinUIDesignerInstanceManager : DesignerInstanceManager
{
    private const int ConfigureAppResourcesMessage = 6001;
    private readonly IProtocolHandler protocolHandler;

    public WinUIDesignerInstanceManager(
        ISurfaceProcessMarkupProvider markupProvider,
        ISurfaceProcessContext surfaceProcessContext,
        IInstanceBuilderPlatform platform,
        IProtocolHandler protocolHandler)
        : base(markupProvider, surfaceProcessContext, platform, protocolHandler)
    {
        this.protocolHandler = protocolHandler;
    }

    protected override LiveMarkupLinkResult TryLinkDocumentNode(ILiveNode liveNode, bool canDelay)
    {
        LiveMarkupLinkResult result = base.TryLinkDocumentNode(liveNode, canDelay);
        SourceInfo? source = liveNode.SourceInfo;
        WinUIPlatform.WriteDiagnosticTrace(
            $"Live markup link: handle={liveNode.Handle}, type={liveNode.TypeFullName}, " +
            $"file={source?.FileName ?? "<null>"}, line={source?.LineNumber ?? 0}, column={source?.ColumnNumber ?? 0}, " +
            $"markup={source?.MarkupHandle ?? 0}, changeVersion={source?.ChangeVersion ?? 0}, canDelay={canDelay}, " +
            $"result={result}." );
        return result;
    }

    protected override async Task<CreateSurfaceRequestInfo> CreateRequestAsync(
        ISurfaceProcessDocument targetDocument,
        IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        _ = preparedDocuments;

        try
        {
            string? projectPath = targetDocument.Document.DocumentContext?.Project?.ProjectPath;
            string? projectDirectory = Path.GetDirectoryName(projectPath);
            string? appXamlPath = projectDirectory is null ? null : Path.Combine(projectDirectory, "App.xaml");
            XDocument? appXaml = appXamlPath is not null && File.Exists(appXamlPath)
                ? XDocument.Load(appXamlPath) : null;
            bool hasXamlControlsResources = appXaml is not null &&
                appXaml.Descendants()
                    .Any(element => element.Name.LocalName == "XamlControlsResources" &&
                        element.Name.NamespaceName == "using:Microsoft.UI.Xaml.Controls");
            string? requestedTheme = (string?)appXaml?.Root?.Attribute("RequestedTheme");

            ResponseWithError response = await protocolHandler.SendMessageAsync<ResponseWithError>(
                ConfigureAppResourcesMessage,
                new AppResourcesRequest { HasXamlControlsResources = hasXamlControlsResources, RequestedTheme = requestedTheme }).ConfigureAwait(false);
            WinUIPlatform.WriteDiagnosticTrace(
                $"App.xaml resources configured: XamlControlsResources={hasXamlControlsResources}, RequestedTheme={requestedTheme ?? "Default"}, result=0x{response.HResult:X8}.");
        }
        catch (Exception ex)
        {
            WinUIPlatform.WriteDiagnosticTrace($"App.xaml resource configuration failed: {ex}");
        }

        bool includeInstanceBuildingActions = SurfaceProcessContext.ResetParseLoadDocumentFailure(targetDocument.DocumentId);
        includeInstanceBuildingActions |= SurfaceProcessContext.InstanceBuildingFailures.HasFailures;

        var request = new CreateSurfaceRequestInfo
        {
            Document = MakeCreateDocumentInfo(targetDocument, includeInstanceBuildingActions),
        };

        return request;
    }

    [DataContract]
    private sealed class AppResourcesRequest
    {
        [DataMember]
        public bool HasXamlControlsResources { get; set; }

        [DataMember]
        public string? RequestedTheme { get; set; }
    }
}
