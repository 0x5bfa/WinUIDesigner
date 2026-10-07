// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Serializes a rectangle in the Visual Studio hit-test request wire shape.
/// </summary>
[DataContract]
internal sealed class HitTestRectContract
{
    [DataMember(Name = "_x")]
    public double X { get; set; }

    [DataMember(Name = "_y")]
    public double Y { get; set; }

    [DataMember(Name = "_width")]
    public double Width { get; set; }

    [DataMember(Name = "_height")]
    public double Height { get; set; }
}
