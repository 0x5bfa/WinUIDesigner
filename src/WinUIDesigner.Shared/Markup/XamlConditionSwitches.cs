// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// One prefix of a document whose members depend on a <see cref="XamlConditionSwitch"/>.
/// </summary>
internal sealed class XamlConditionSwitchPrefix
{
    public XamlConditionSwitchPrefix(string prefix, bool isNegated, int lineNumber)
    {
        Prefix = prefix;
        IsNegated = isNegated;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Gets the prefix as it is declared.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets a value indicating whether the prefix is declared with the <c>...NotPresent</c> form of the question,
    /// so that its members apply when the question does not hold.
    /// </summary>
    public bool IsNegated { get; }

    /// <summary>
    /// Gets the 1-based line of the first declaration of the prefix with this question, or 0 when unknown.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Returns whether the members written under this prefix apply when the question has the answer <paramref name="holds"/>.
    /// </summary>
    public bool Applies(bool holds)
    {
        return holds != IsNegated;
    }
}

/// <summary>
/// One question that the conditional XAML namespaces of a document ask, as the user can switch it in the
/// designer: <c>xmlns:c7</c> with <c>IsApiContractPresent(X,7)</c> and <c>xmlns:c7n</c> with
/// <c>IsApiContractNotPresent(X,7)</c> make one switch with two prefixes.
/// </summary>
internal sealed class XamlConditionSwitch
{
    public XamlConditionSwitch(XamlConditionQuery query, string displayText, IReadOnlyList<XamlConditionSwitchPrefix> prefixes)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        DisplayText = displayText ?? throw new ArgumentNullException(nameof(displayText));
        Prefixes = prefixes ?? throw new ArgumentNullException(nameof(prefixes));
    }

    /// <summary>
    /// Gets the identity of the question, the key its override is stored under.
    /// </summary>
    public string Key => Query.Key;

    /// <summary>
    /// Gets the question in its positive form: the built-in <c>Is...Present</c> method, or the custom condition
    /// as written. Its answer is the value of the switch.
    /// </summary>
    public XamlConditionQuery Query { get; }

    /// <summary>
    /// Gets the predicate to show: the positive form of a built-in method without insignificant white space,
    /// or a custom condition as it is written.
    /// </summary>
    public string DisplayText { get; }

    /// <summary>
    /// Gets the prefixes that ask the question, in the order of their first declaration.
    /// </summary>
    public IReadOnlyList<XamlConditionSwitchPrefix> Prefixes { get; }

    /// <summary>
    /// Gets a value indicating whether the user can force an answer. A malformed predicate cannot be switched:
    /// its members are skipped on both loading paths whatever the user wants.
    /// </summary>
    public bool IsSwitchable => XamlConditionOverrides.CanOverride(Query);

    /// <summary>
    /// Gets why the predicate cannot be switched, or <see langword="null"/> when it can.
    /// </summary>
    public string? Error => IsSwitchable ? null : Query.Condition.Error ?? "the predicate cannot be evaluated";
}

/// <summary>
/// Lists the questions that the conditional XAML namespaces of a document ask, one entry per question.
/// </summary>
internal static class XamlConditionSwitches
{
    /// <summary>
    /// Collects the switches of a document from the namespace declarations found by <see cref="XamlNamespaceScanner"/>.
    /// </summary>
    /// <remarks>
    /// The keys are built the way the designer builds them when it evaluates a document: the name of a custom
    /// condition resolves through the prefix it is qualified with. The scan does not record where a declaration
    /// is in scope, so a custom condition is listed once for every namespace its prefix is bound to in the document;
    /// usually that is one. A namespace whose predicate is empty is unconditional and is not listed.
    /// </remarks>
    public static IReadOnlyList<XamlConditionSwitch> Collect(XamlNamespaceScanResult? scan)
    {
        if (scan is null || !scan.HasConditionalNamespaces)
        {
            return [];
        }

        var switches = new List<Builder>();
        var byKey = new Dictionary<string, Builder>(StringComparer.Ordinal);
        IReadOnlyList<XamlNamespaceDeclaration> declarations = scan.Declarations;
        for (int index = 0; index < declarations.Count; index++)
        {
            XamlNamespaceDeclaration declaration = declarations[index];
            string? condition = declaration.Uri.Condition;
            if (condition is null)
            {
                continue;
            }

            XamlCondition parsed = XamlCondition.Parse(condition);
            if (parsed.Kind == XamlConditionKind.Empty)
            {
                continue;
            }

            foreach (XamlConditionQuery query in CreateQueries(condition, parsed, declarations))
            {
                if (!byKey.TryGetValue(query.Key, out Builder? builder))
                {
                    builder = new Builder(CreatePositiveQuery(query));
                    byKey.Add(query.Key, builder);
                    switches.Add(builder);
                }

                builder.Add(declaration, query.Condition.IsNegated);
            }
        }

        var result = new XamlConditionSwitch[switches.Count];
        for (int index = 0; index < switches.Count; index++)
        {
            result[index] = switches[index].Build();
        }

        return result;
    }

    private static IEnumerable<XamlConditionQuery> CreateQueries(string condition, XamlCondition parsed, IReadOnlyList<XamlNamespaceDeclaration> declarations)
    {
        if (parsed.Kind != XamlConditionKind.Custom)
        {
            yield return XamlConditionQuery.Create(condition, resolveNamespace: null);
            yield break;
        }

        bool isDeclared = false;
        for (int index = 0; index < declarations.Count; index++)
        {
            if (string.Equals(declarations[index].Prefix, parsed.Prefix, StringComparison.Ordinal))
            {
                string predicateNamespace = declarations[index].Uri.Original;
                isDeclared = true;
                yield return XamlConditionQuery.Create(condition, _ => predicateNamespace);
            }
        }

        if (!isDeclared)
        {
            yield return XamlConditionQuery.Create(condition, resolveNamespace: null);
        }
    }

    /// <summary>
    /// Returns the positive form of a question. A built-in method written as <c>...NotPresent</c>, or with a prefix
    /// of the presentation namespace, becomes the plain <c>...Present</c> method, whose text is its key.
    /// </summary>
    private static XamlConditionQuery CreatePositiveQuery(XamlConditionQuery query)
    {
        if (IsBuiltIn(query)
            && (query.Condition.IsNegated || !string.Equals(query.Condition.Text, query.Key, StringComparison.Ordinal)))
        {
            XamlConditionQuery positive = XamlConditionQuery.Create(query.Key, resolveNamespace: null);
            if (string.Equals(positive.Key, query.Key, StringComparison.Ordinal) && !positive.Condition.IsNegated)
            {
                return positive;
            }
        }

        return query;
    }

    private static bool IsBuiltIn(XamlConditionQuery query)
    {
        return query.Condition.Kind is XamlConditionKind.ApiContract or XamlConditionKind.Type or XamlConditionKind.Property;
    }

    private sealed class Builder(XamlConditionQuery query)
    {
        private readonly List<XamlConditionSwitchPrefix> _prefixes = [];
        private readonly HashSet<(string Prefix, bool IsNegated)> _seen = [];

        public void Add(XamlNamespaceDeclaration declaration, bool isNegated)
        {
            if (_seen.Add((declaration.Prefix, isNegated)))
            {
                _prefixes.Add(new XamlConditionSwitchPrefix(declaration.Prefix, isNegated, declaration.LineNumber));
            }
        }

        public XamlConditionSwitch Build()
        {
            return new XamlConditionSwitch(query, IsBuiltIn(query) ? query.Key : query.Condition.Text, _prefixes.ToArray());
        }
    }
}
