// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Markup;

/// <summary>
/// A condition together with the namespace its name resolves in: everything that is needed to evaluate it away
/// from the document it was written in, and to recognize the same question when it is asked again.
/// </summary>
internal sealed class XamlConditionQuery
{
    private const string _ClrNamespaceScheme = "clr-namespace:";

    private XamlConditionQuery(XamlCondition condition, string? predicateNamespace, string? typeName, string key)
    {
        Condition = condition;
        PredicateNamespace = predicateNamespace;
        TypeName = typeName;
        Key = key;
    }

    /// <summary>
    /// Gets the parsed condition. A built-in method that was written with a prefix of the presentation
    /// namespace is represented as that built-in method.
    /// </summary>
    public XamlCondition Condition { get; }

    /// <summary>
    /// Gets the namespace the name of a custom condition resolves in, without a condition of its own.
    /// It is <see langword="null"/> for the other kinds and when the prefix is not declared.
    /// </summary>
    public string? PredicateNamespace { get; }

    /// <summary>
    /// Gets the full name of the type of a custom condition, or <see langword="null"/> when its namespace
    /// does not name a CLR namespace.
    /// </summary>
    public string? TypeName { get; }

    /// <summary>
    /// Gets the identity of the question, without the negation of a <c>...NotPresent</c> method. Conditions
    /// with the same key have the same answer, or exactly opposite answers when one of them is negated.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Resolves a condition where it is declared.
    /// </summary>
    /// <param name="condition">The raw text after the <c>?</c> of the namespace.</param>
    /// <param name="resolveNamespace">
    /// Returns the namespace bound to an <c>xmlns</c> prefix where the condition is declared; the empty prefix
    /// stands for the default namespace. Only custom conditions need it.
    /// </param>
    public static XamlConditionQuery Create(string? condition, Func<string, string?>? resolveNamespace)
    {
        XamlCondition parsed = XamlCondition.Parse(condition);
        if (parsed.Kind != XamlConditionKind.Custom)
        {
            return CreateBuiltIn(parsed);
        }

        // The namespace of the condition type may carry a condition of its own, which says nothing about the type.
        string? declared = resolveNamespace?.Invoke(parsed.Prefix);
        string? predicateNamespace = declared is null ? null : XamlNamespaceUri.Parse(declared).BaseUri;

        // The six built-in methods are types of the presentation namespace as far as the XAML reader is
        // concerned, so it also finds them through a prefix that is bound to that namespace.
        if (predicateNamespace == XamlNamespaceUri.PresentationNamespace && XamlCondition.IsBuiltInMethod(parsed.Name))
        {
            return CreateBuiltIn(parsed.WithoutPrefix());
        }

        string? clrNamespace = GetClrNamespace(predicateNamespace);
        string? typeName = clrNamespace is null ? null : clrNamespace + "." + parsed.Name;
        string key = (predicateNamespace ?? "?" + parsed.Prefix) + "|" + parsed.PositiveKey;
        return new XamlConditionQuery(parsed, predicateNamespace, typeName, key);
    }

    /// <summary>
    /// Recreates a query from its two parts, as they travel between the designer processes.
    /// </summary>
    /// <param name="condition">The text of <see cref="Condition"/>.</param>
    /// <param name="predicateNamespace">The value of <see cref="PredicateNamespace"/>.</param>
    public static XamlConditionQuery Create(string? condition, string? predicateNamespace)
    {
        return Create(condition, _ => predicateNamespace);
    }

    private static XamlConditionQuery CreateBuiltIn(XamlCondition condition)
    {
        // A text that is not a condition is its own question: nothing else has the same answer.
        string key = condition.Kind == XamlConditionKind.Unknown ? "!" + condition.Text : condition.PositiveKey;
        return new XamlConditionQuery(condition, predicateNamespace: null, typeName: null, key);
    }

    private static string? GetClrNamespace(string? namespaceUri)
    {
        if (namespaceUri is null)
        {
            return null;
        }

        string? name = null;
        if (namespaceUri.StartsWith(XamlNamespaceUri.UsingScheme, StringComparison.Ordinal))
        {
            name = namespaceUri.Substring(XamlNamespaceUri.UsingScheme.Length);
        }
        else if (namespaceUri.StartsWith(_ClrNamespaceScheme, StringComparison.Ordinal))
        {
            // "clr-namespace:My.App;assembly=My.App" names the namespace up to the first ';'.
            name = namespaceUri.Substring(_ClrNamespaceScheme.Length);
            int separator = name.IndexOf(';');
            if (separator >= 0)
            {
                name = name.Substring(0, separator);
            }
        }

        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
