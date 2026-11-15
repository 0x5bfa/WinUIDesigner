// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WinUIDesigner.Protocol;

/// <summary>
/// The surface's reply to <see cref="EvaluateXamlConditionsRequest"/>.
/// </summary>
[DataContract]
internal sealed class EvaluateXamlConditionsResponse
{
    /// <summary>
    /// Gets or sets, for each condition of the request, whether it holds as written;
    /// <see langword="null"/> where the surface could not tell.
    /// </summary>
    [DataMember]
    public List<bool?>? Outcomes { get; set; }

    /// <summary>
    /// Gets or sets why the request as a whole could not be answered. <see cref="Outcomes"/> is empty then.
    /// </summary>
    [DataMember(EmitDefaultValue = false)]
    public string? Error { get; set; }
}
