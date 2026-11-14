// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// Splits the value of an <c>xmlns</c> declaration into the parts the namespace policies work with:
/// the bare namespace, an optional conditional-XAML predicate, and optional Uno <c>#using:</c> namespaces.
/// </summary>
/// <remarks>
/// <c>http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)</c>
/// has a condition, and <c>http://uno.ui/android#using:My.Ns1;My.Ns2</c> has two using namespaces.
/// The default value represents an empty namespace.
/// </remarks>
internal readonly struct XamlNamespaceUri
{
    /// <summary>
    /// The default WinUI presentation namespace.
    /// </summary>
    public const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// The scheme of a CLR-namespace URI such as <c>using:My.Controls</c>.
    /// </summary>
    public const string UsingScheme = "using:";

    private const char _ConditionSeparator = '?';
    private const string _UsingSuffix = "#using:";

    private static readonly string[] _NoNamespaces = [];

    private readonly string? _original;
    private readonly string? _baseUri;
    private readonly string? _condition;
    private readonly string[]? _usingNamespaces;

    private XamlNamespaceUri(string original, string baseUri, string? condition, string[] usingNamespaces)
    {
        _original = original;
        _baseUri = baseUri;
        _condition = condition;
        _usingNamespaces = usingNamespaces;
    }

    /// <summary>
    /// Gets the namespace exactly as it was declared.
    /// </summary>
    public string Original => _original ?? string.Empty;

    /// <summary>
    /// Gets the namespace without its condition and without its <c>#using:</c> suffix.
    /// </summary>
    public string BaseUri => _baseUri ?? string.Empty;

    /// <summary>
    /// Gets the raw text after <c>?</c>, or <see langword="null"/> when the namespace is unconditional.
    /// </summary>
    public string? Condition => _condition;

    /// <summary>
    /// Gets the CLR namespaces listed after <c>#using:</c>, in declaration order.
    /// </summary>
    public IReadOnlyList<string> UsingNamespaces => _usingNamespaces ?? _NoNamespaces;

    /// <summary>
    /// Gets a value indicating whether the namespace carries a conditional-XAML predicate.
    /// </summary>
    public bool IsConditional => _condition is not null;

    /// <summary>
    /// Parses the value of an <c>xmlns</c> declaration. Never throws; unrecognized text is kept as the base URI.
    /// </summary>
    public static XamlNamespaceUri Parse(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return default;
        }

        int conditionIndex = value.IndexOf(_ConditionSeparator);
        int usingIndex = value.IndexOf(_UsingSuffix, StringComparison.Ordinal);
        if (conditionIndex < 0 && usingIndex < 0)
        {
            return new XamlNamespaceUri(value, value, null, _NoNamespaces);
        }

        // Either part can come first; each one ends where the other begins.
        int baseLength = value.Length;
        if (conditionIndex >= 0)
        {
            baseLength = conditionIndex;
        }

        if (usingIndex >= 0 && usingIndex < baseLength)
        {
            baseLength = usingIndex;
        }

        string? condition = null;
        if (conditionIndex >= 0)
        {
            int start = conditionIndex + 1;
            int end = usingIndex > conditionIndex ? usingIndex : value.Length;
            condition = value.Substring(start, end - start);
        }

        string[] usingNamespaces = _NoNamespaces;
        if (usingIndex >= 0)
        {
            int start = usingIndex + _UsingSuffix.Length;
            int end = conditionIndex > usingIndex ? conditionIndex : value.Length;
            usingNamespaces = SplitNamespaces(value.Substring(start, end - start));
        }

        return new XamlNamespaceUri(value, value.Substring(0, baseLength), condition, usingNamespaces);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Original;
    }

    private static string[] SplitNamespaces(string value)
    {
        string[] parts = value.Split(';');
        var namespaces = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string name = part.Trim();
            if (name.Length != 0)
            {
                namespaces.Add(name);
            }
        }

        return namespaces.Count == 0 ? _NoNamespaces : namespaces.ToArray();
    }
}
