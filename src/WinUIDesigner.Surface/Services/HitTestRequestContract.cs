// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Serializes the Visual Studio hit-test request wire shape.
/// </summary>
[DataContract]
internal sealed class HitTestRequestContract
{
    [DataMember]
    public long RootHandle { get; set; }

    [DataMember(EmitDefaultValue = false)]
    public PointContract? Point { get; set; }

    [DataMember(EmitDefaultValue = false)]
    public HitTestRectContract? Rect { get; set; }
}
