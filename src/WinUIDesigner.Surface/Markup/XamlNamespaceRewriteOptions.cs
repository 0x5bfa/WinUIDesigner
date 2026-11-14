// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Xml.Linq;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Configures one run of <see cref="XamlNamespaceRewriter"/>.
/// </summary>
internal sealed class XamlNamespaceRewriteOptions
{
    private string _targetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;

    /// <summary>
    /// Gets or sets the text the document was parsed from. LINQ to XML does not keep the prefix a member
    /// was written with, so the rewriter reads it back from this text; without it, members written under
    /// different prefixes bound to one namespace cannot be told apart and are left untouched.
    /// </summary>
    public string? SourceXaml { get; set; }

    /// <summary>
    /// Gets or sets the evaluator for conditional namespaces. Without one, their members are left untouched.
    /// </summary>
    public XamlConditionEvaluator? EvaluateCondition { get; set; }

    /// <summary>
    /// Gets or sets the platform whose members are kept; the others are dropped.
    /// </summary>
    public string TargetPlatform
    {
        get => _targetPlatform;
        set => _targetPlatform = string.IsNullOrEmpty(value) ? UnoPlatformPrefixes.DefaultTargetPlatform : value;
    }

    /// <summary>
    /// Gets or sets a filter for attributes that became plain attributes of their element. It receives the
    /// owning element, the plain attribute name and the value, and returns <see langword="true"/> to remove
    /// the attribute instead, for example an event handler that the XAML reader cannot attach.
    /// </summary>
    public Func<XElement, XName, string, bool>? ShouldDropNormalizedAttribute { get; set; }

    /// <summary>
    /// Gets or sets the receiver of diagnostic messages.
    /// </summary>
    public Action<string>? Report { get; set; }

    /// <summary>
    /// Gets or sets the receiver of messages about markup that is most likely a mistake of its author, such as
    /// one member that two attributes set. Without one, these messages go to <see cref="Report"/>.
    /// </summary>
    public Action<string>? ReportWarning { get; set; }

    /// <summary>
    /// Gets or sets a classification that takes precedence over the namespace policies. It receives the prefix,
    /// its parsed namespace and whether the prefix is ignorable; <see langword="null"/> defers to the policies.
    /// Unit tests use it to exercise the rewrite mechanics independently of the policies.
    /// </summary>
    internal Func<string, XamlNamespaceUri, bool, XamlPrefixDisposition?>? ClassifyOverride { get; set; }
}
