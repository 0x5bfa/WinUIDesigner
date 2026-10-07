// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Serializes a rectangle included in a surface-bounds-changed event.
/// </summary>
[DataContract]
internal sealed class RectContract
{
    [DataMember(Name = "_x")]
    public double X { get; set; }

    [DataMember(Name = "_y")]
    public double Y { get; set; }

    [DataMember(Name = "_width")]
    public double Width { get; set; }

    [DataMember(Name = "_height")]
    public double Height { get; set; }

    public static RectContract FromRect(Rect rect)
    {
        return new() { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };
    }
}
