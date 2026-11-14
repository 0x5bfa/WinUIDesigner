// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// Decides whether the members under a conditional XAML namespace apply, as in
/// <c>xmlns:c7="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)"</c>.
/// </summary>
internal static class ConditionalNamespacePolicy
{
    /// <summary>
    /// Classifies the members written under a prefix bound to <paramref name="uri"/>.
    /// </summary>
    /// <param name="uri">The parsed namespace of the prefix; <see cref="XamlNamespaceUri.Condition"/> holds the predicate.</param>
    /// <param name="evaluateCondition">
    /// Evaluates the raw predicate text and returns <see langword="null"/> when it cannot tell.
    /// It is <see langword="null"/> when no evaluator is available.
    /// </param>
    /// <returns>
    /// <see cref="XamlPrefixDisposition.Keep"/> for a predicate that holds, <see cref="XamlPrefixDisposition.Drop"/> for one
    /// that does not, and <see cref="XamlPrefixDisposition.Leave"/> for an unconditional or unevaluated namespace.
    /// </returns>
    public static XamlPrefixDisposition Classify(XamlNamespaceUri uri, Func<string, bool?>? evaluateCondition)
    {
        // Conditional namespaces are not interpreted yet, so their members reach the XAML reader as written.
        return XamlPrefixDisposition.Leave;
    }
}
