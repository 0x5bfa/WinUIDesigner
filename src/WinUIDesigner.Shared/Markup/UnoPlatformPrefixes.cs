// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// Decides whether the members under an Uno Platform prefix apply to the previewed platform, as in
/// <c>xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation"</c> or
/// <c>xmlns:android="http://uno.ui/android"</c>.
/// </summary>
/// <remarks>
/// The decision is driven by the prefix: <c>win</c>, <c>not_android</c> and the default namespace
/// all share the presentation URI.
/// </remarks>
internal static class UnoPlatformPrefixes
{
    /// <summary>
    /// The platform the designer previews unless told otherwise.
    /// </summary>
    public const string DefaultTargetPlatform = "win";

    /// <summary>
    /// The start of the namespaces Uno assigns to prefixes that are excluded on Windows.
    /// </summary>
    public const string PlatformUriPrefix = "http://uno.ui/";

    /// <summary>
    /// Returns whether <paramref name="namespaceUri"/> is an Uno platform namespace such as <c>http://uno.ui/android</c>.
    /// </summary>
    public static bool IsPlatformUri(string? namespaceUri)
    {
        return namespaceUri is not null && namespaceUri.StartsWith(PlatformUriPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Classifies the members written under <paramref name="prefix"/> for <paramref name="targetPlatform"/>.
    /// </summary>
    /// <param name="prefix">The prefix as written in the document; empty for the default namespace.</param>
    /// <param name="uri">The parsed namespace the prefix is bound to.</param>
    /// <param name="isIgnorable">Whether the prefix is listed in <c>mc:Ignorable</c> where it is used.</param>
    /// <param name="targetPlatform">The previewed platform, for example <see cref="DefaultTargetPlatform"/>.</param>
    /// <returns>
    /// <see cref="XamlPrefixDisposition.Keep"/> for a prefix included on the platform, <see cref="XamlPrefixDisposition.Drop"/>
    /// for an excluded one, and <see cref="XamlPrefixDisposition.Leave"/> for a prefix that is not a platform prefix.
    /// </returns>
    public static XamlPrefixDisposition Classify(string prefix, XamlNamespaceUri uri, bool isIgnorable, string targetPlatform)
    {
        // Platform prefixes are not interpreted yet, so their members reach the XAML reader as written.
        return XamlPrefixDisposition.Leave;
    }
}
