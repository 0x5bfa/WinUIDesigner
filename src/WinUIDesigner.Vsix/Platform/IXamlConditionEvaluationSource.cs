// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Tells the designer's user interface what the conditions of conditional XAML namespaces evaluate to in a surface
/// process, leaving the user's overrides aside, so that an override can be shown next to the value it replaces.
/// </summary>
/// <remarks>
/// The WinUI surface process context implements this interface, like <see cref="IDesignTimeTargetProvider"/>: the
/// answers belong to the surface process, which every document of the project shares.
/// </remarks>
internal interface IXamlConditionEvaluationSource
{
    /// <summary>
    /// Returns whether <paramref name="query"/> holds as written, or <see langword="null"/> when that is not known
    /// yet: before a document of the current surface process was prepared, or before the surface answered.
    /// Never blocks.
    /// </summary>
    bool? EvaluateIgnoringOverrides(XamlConditionQuery query);
}
