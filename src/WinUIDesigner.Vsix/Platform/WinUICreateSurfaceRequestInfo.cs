// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Platform;

/// <summary>
/// The CreateSurface (516) request of the WinUI designer: Visual Studio's request plus the design-time options.
/// </summary>
/// <remarks>
/// Visual Studio builds the serializer of a protocol message from the runtime type of its payload, so a member
/// declared here travels in the same message; its UWP designer extends the request the same way. The surface reads
/// the member through a contract of its own that declares the same member name.
/// </remarks>
[DataContract]
internal sealed class WinUICreateSurfaceRequestInfo : CreateSurfaceRequestInfo
{
    private static volatile bool _isTransportEnabled;

    /// <summary>
    /// Gets whether requests carry the design-time options. This is off until <see cref="ValidateTransport"/>
    /// succeeds. While it is off the designer sends Visual Studio's own request and previews with the default
    /// options on both sides, exactly as it did before the options existed.
    /// </summary>
    public static bool IsTransportEnabled => _isTransportEnabled;

    /// <summary>
    /// Gets or sets the text form of the <see cref="DesignTimeOptions"/> the documents of this request are previewed with.
    /// </summary>
    [DataMember(Name = DesignTimeOptions.WireMemberName, EmitDefaultValue = false)]
    public string? SerializedDesignTimeOptions { get; set; }

    /// <summary>
    /// Sends a request through Visual Studio's message serializer and back, and turns the transport on when the
    /// design-time options survive the trip; it is off after any other outcome.
    /// </summary>
    /// <returns><see langword="null"/> when the transport works; otherwise what went wrong.</returns>
    /// <remarks>
    /// The caller has to expect an exception, for example from a build of Visual Studio whose message type changed.
    /// </remarks>
    public static string? ValidateTransport()
    {
        bool works = false;
        try
        {
            string? failure = FindTransportFailure();
            works = failure is null;
            return failure;
        }
        finally
        {
            _isTransportEnabled = works;
        }
    }

    /// <summary>
    /// Returns the request to send in place of <paramref name="request"/>: a copy that also carries the design-time
    /// options of <paramref name="surfaceProcessContext"/>, or <paramref name="request"/> itself while the
    /// transport is off.
    /// </summary>
    /// <param name="request">Visual Studio's request, as far as it has been filled in.</param>
    /// <param name="surfaceProcessContext">The surface process context the request is sent for.</param>
    public static CreateSurfaceRequestInfo WithDesignTimeOptions(CreateSurfaceRequestInfo request, ISurfaceProcessContext? surfaceProcessContext)
    {
        if (!_isTransportEnabled)
        {
            return request;
        }

        return new WinUICreateSurfaceRequestInfo()
        {
            Document = request.Document,
            Application = request.Application,
            DesignTimeResources = request.DesignTimeResources,
            SerializedDesignTimeOptions = DesignTimeTargetProvider.GetDesignTimeOptions(surfaceProcessContext).Serialize(),
        };
    }

    // The only member that names Visual Studio's Message type. It is not inlined, so a build of Visual Studio
    // without a matching Message fails when this method is compiled, inside ValidateTransport.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? FindTransportFailure()
    {
        const int documentId = 516;

        // The overrides carry arbitrary text of custom conditions, so the probe carries some as well.
        string options = new DesignTimeOptions(
            DesignTimeTargetPlatform.AndroidSkia,
            7,
            XamlConditionOverrides.Empty.With("using:Probe|Flag( é ∑ \"q\" \\ / %;=, (x) )", holds: true)).Serialize();
        var request = new WinUICreateSurfaceRequestInfo()
        {
            Document = new CreateDocumentInfo() { DocumentId = documentId },
            SerializedDesignTimeOptions = options,
        };

        // ProtocolHandler.SendMessageAsync wraps the payload in a Message exactly like this.
        var sent = new Message((int)DesignerMessageId.CreateSurface, request);
        var received = new Message(sent.BufferForSend);

        // The surface reads the message into another type that declares the member under the same name.
        if (received.ConvertPayloadToModel<TransportProbe>() is not { } probe)
        {
            return "the request cannot be read back";
        }

        if (!string.Equals(probe.SerializedDesignTimeOptions, options, StringComparison.Ordinal))
        {
            return $"the member '{DesignTimeOptions.WireMemberName}' is not part of the serialized request";
        }

        if (probe.Document?.DocumentId != documentId)
        {
            return "the document is not part of the serialized request";
        }

        // A reader that only knows Visual Studio's request must not be disturbed by the member.
        if (received.ConvertPayloadToModel<CreateSurfaceRequestInfo>()?.Document?.DocumentId != documentId)
        {
            return "the request can no longer be read as Visual Studio's own request";
        }

        return null;
    }

    /// <summary>
    /// Stands in for the contract the surface deserializes the request into.
    /// </summary>
    [DataContract]
    private sealed class TransportProbe
    {
        [DataMember]
        public CreateDocumentInfo? Document { get; set; }

        [DataMember(Name = DesignTimeOptions.WireMemberName)]
        public string? SerializedDesignTimeOptions { get; set; }
    }
}
