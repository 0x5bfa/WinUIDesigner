// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.DesignTime;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Deserializes the CreateSurface (516) request: Visual Studio's request plus the design-time options the
/// extension adds to it on the Visual Studio side.
/// </summary>
/// <remarks>
/// The member is optional. A request without it, which is what Visual Studio's own request type produces, reads as
/// "no options", and the documents are then previewed with the defaults.
/// </remarks>
[DataContract]
internal sealed class CreateSurfaceRequestContract : CreateSurfaceRequestInfo
{
    /// <summary>
    /// Gets or sets the text form of the <see cref="DesignTimeOptions"/> the documents of this request are previewed with.
    /// </summary>
    [DataMember(Name = DesignTimeOptions.WireMemberName, EmitDefaultValue = false)]
    public string? SerializedDesignTimeOptions { get; set; }
}
