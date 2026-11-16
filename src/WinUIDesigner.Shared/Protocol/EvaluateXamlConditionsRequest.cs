// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WinUIDesigner.Protocol;

/// <summary>
/// Asks the surface whether conditions of conditional XAML namespaces hold
/// (<see cref="DesignerMessageId.EvaluateXamlConditions"/>).
/// </summary>
/// <remarks>
/// Only the surface process can evaluate every condition: it runs the Windows App SDK and loads the assemblies
/// of the previewed project. Visual Studio asks it so that documents built from construction actions use the
/// answers the surface uses for prepared XAML.
/// </remarks>
[DataContract]
internal sealed class EvaluateXamlConditionsRequest
{
    /// <summary>
    /// Gets or sets the conditions: for each one, the text after the <c>?</c> of its namespace.
    /// </summary>
    [DataMember]
    public List<string?>? Conditions { get; set; }

    /// <summary>
    /// Gets or sets, for each entry of <see cref="Conditions"/>, the namespace the name of a custom condition
    /// resolves in, or <see langword="null"/> for a condition that needs none.
    /// </summary>
    [DataMember]
    public List<string?>? PredicateNamespaces { get; set; }
}
