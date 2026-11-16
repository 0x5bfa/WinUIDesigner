// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// The questions about the running system that <see cref="XamlConditionService"/> cannot answer itself.
/// A probe that is not set makes the conditions that need it unevaluable.
/// </summary>
internal sealed class XamlConditionProbeSet
{
    /// <summary>
    /// Gets or sets the probe for <c>IsApiContractPresent</c>. It receives the contract name, the major
    /// version, and the minor version when one was written.
    /// </summary>
    public Func<string, int, int?, bool>? IsApiContractPresent { get; set; }

    /// <summary>
    /// Gets or sets the probe for <c>IsTypePresent</c> with a type of the operating system (<c>Windows.*</c>).
    /// </summary>
    public Func<string, bool>? IsWindowsTypePresent { get; set; }

    /// <summary>
    /// Gets or sets the probe for <c>IsPropertyPresent</c> with a type of the operating system (<c>Windows.*</c>).
    /// It receives the type name and the property name.
    /// </summary>
    public Func<string, string, bool>? IsWindowsPropertyPresent { get; set; }

    /// <summary>
    /// Gets or sets the probe for <c>IsTypePresent</c> with any other type name. Such a type is not part of
    /// the operating system: it comes with the previewed application, as the Windows App SDK does.
    /// </summary>
    public Func<string, bool>? IsManagedTypePresent { get; set; }

    /// <summary>
    /// Gets or sets the probe for <c>IsPropertyPresent</c> with any other type name.
    /// It receives the type name and the property name.
    /// </summary>
    public Func<string, string, bool>? IsManagedPropertyPresent { get; set; }

    /// <summary>
    /// Gets or sets the probe for custom conditions. It receives the full name of the condition type and the
    /// argument exactly as written, and returns <see langword="null"/> when the condition cannot be evaluated.
    /// </summary>
    public Func<string, string, bool?>? EvaluateCustom { get; set; }
}
