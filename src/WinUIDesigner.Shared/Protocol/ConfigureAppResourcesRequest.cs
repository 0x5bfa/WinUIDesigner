// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;

namespace WinUIDesigner.Protocol;

/// <summary>
/// Carries the subset of App.xaml resource settings needed by the surface process.
/// </summary>
[DataContract]
internal sealed class ConfigureAppResourcesRequest
{
    [DataMember]
    public bool HasXamlControlsResources { get; set; }

    [DataMember]
    public string? RequestedTheme { get; set; }
}
