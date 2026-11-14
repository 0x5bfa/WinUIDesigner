// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// Combines the namespace policies into one verdict per <c>xmlns</c> prefix.
/// Both the surface (prepared XAML) and the Visual Studio side (construction actions) classify through
/// this type, so a member is treated the same way on either path.
/// </summary>
internal static class XamlNamespacePolicies
{
    /// <summary>
    /// Classifies the members written under <paramref name="prefix"/>.
    /// </summary>
    /// <param name="prefix">The prefix as written in the document; empty for the default namespace.</param>
    /// <param name="uri">The parsed namespace the prefix is bound to.</param>
    /// <param name="isIgnorable">Whether the prefix is listed in <c>mc:Ignorable</c> where it is used.</param>
    /// <param name="targetPlatform">The previewed platform.</param>
    /// <param name="evaluateCondition">
    /// Evaluates the raw predicate of a conditional namespace, or <see langword="null"/> when no evaluator is available.
    /// </param>
    public static XamlPrefixDisposition Classify(
        string prefix,
        XamlNamespaceUri uri,
        bool isIgnorable,
        string targetPlatform,
        Func<string, bool?>? evaluateCondition)
    {
        // The platform decides first: a member excluded from the previewed platform
        // is dropped whatever its condition evaluates to.
        XamlPrefixDisposition platform = UnoPlatformPrefixes.Classify(prefix, uri, isIgnorable, targetPlatform);
        if (platform == XamlPrefixDisposition.Drop)
        {
            return XamlPrefixDisposition.Drop;
        }

        XamlPrefixDisposition conditional = ConditionalNamespacePolicy.Classify(uri, evaluateCondition);
        return Combine(platform, conditional, uri.IsConditional);
    }

    /// <summary>
    /// Combines the verdict of the platform policy with the verdict of the conditional policy.
    /// </summary>
    /// <param name="platform">The verdict of <see cref="UnoPlatformPrefixes"/>.</param>
    /// <param name="conditional">The verdict of <see cref="ConditionalNamespacePolicy"/>.</param>
    /// <param name="isConditionalNamespace">Whether the namespace carries a conditional-XAML predicate.</param>
    public static XamlPrefixDisposition Combine(
        XamlPrefixDisposition platform,
        XamlPrefixDisposition conditional,
        bool isConditionalNamespace)
    {
        if (platform == XamlPrefixDisposition.Drop)
        {
            return XamlPrefixDisposition.Drop;
        }

        if (conditional != XamlPrefixDisposition.Leave)
        {
            return conditional;
        }

        // Keeping normalizes a member to its bare namespace, which would strip a predicate nobody evaluated.
        return isConditionalNamespace ? XamlPrefixDisposition.Leave : platform;
    }

    /// <summary>
    /// Returns whether any namespace declared in <paramref name="scan"/> is classified as something other than
    /// <see cref="XamlPrefixDisposition.Leave"/>. Documents without such a namespace need no further work.
    /// </summary>
    /// <param name="scan">The namespace facts of the document.</param>
    /// <param name="targetPlatform">The previewed platform.</param>
    /// <param name="evaluateCondition">The evaluator for conditional namespaces, if any.</param>
    public static bool MayAffect(XamlNamespaceScanResult scan, string targetPlatform, XamlConditionEvaluator? evaluateCondition)
    {
        IReadOnlyList<XamlNamespaceDeclaration> declarations = scan.Declarations;
        for (int index = 0; index < declarations.Count; index++)
        {
            XamlNamespaceDeclaration declaration = declarations[index];
            Func<string, bool?>? evaluate = null;
            if (evaluateCondition is not null && declaration.Uri.IsConditional)
            {
                evaluate = condition => evaluateCondition(condition, scan.FindNamespace);
            }

            bool isIgnorable = scan.IsIgnorable(declaration.Prefix);
            if (Classify(declaration.Prefix, declaration.Uri, isIgnorable, targetPlatform, evaluate) != XamlPrefixDisposition.Leave)
            {
                return true;
            }

            // mc:Ignorable is scoped to the element that carries it, so a prefix listed on a
            // nested element is not ignorable where it is used elsewhere in the document.
            if (isIgnorable
                && Classify(declaration.Prefix, declaration.Uri, isIgnorable: false, targetPlatform, evaluate) != XamlPrefixDisposition.Leave)
            {
                return true;
            }
        }

        return false;
    }
}
