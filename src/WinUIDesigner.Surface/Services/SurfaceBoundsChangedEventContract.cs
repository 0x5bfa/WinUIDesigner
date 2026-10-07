// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Serializes a surface-bounds-changed event for the designer host.
/// </summary>
[DataContract]
internal sealed class SurfaceBoundsChangedEventContract
{
    [DataMember]
    public RectContract ContentBounds { get; set; } = new();

    [DataMember]
    public RectContract DocumentBounds { get; set; } = new();

    [DataMember]
    public int DocumentId { get; set; }
}
