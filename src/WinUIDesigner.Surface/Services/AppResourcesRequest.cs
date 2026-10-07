// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Serializes a request to load project resources into the surface application.
/// </summary>
[DataContract]
internal sealed class AppResourcesRequest
{
    [DataMember]
    public bool HasXamlControlsResources { get; set; }

    [DataMember]
    public string? RequestedTheme { get; set; }
}
