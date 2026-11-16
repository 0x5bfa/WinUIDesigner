// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// One line of the conditional XAML switches: a question of the current document, or an override of the project
/// that the document does not ask, with what the preview shows for it and why.
/// </summary>
/// <remarks>
/// This is the part of the drop-down that does not need WPF, so that it can be tested: which value a switch shows,
/// whether it is overridden, and the texts the user and a screen reader get.
/// </remarks>
internal sealed class ConditionalXamlSwitchRow
{
    private ConditionalXamlSwitchRow(
        string key,
        string title,
        bool isInDocument,
        bool isSwitchable,
        bool? evaluatedValue,
        bool? overrideValue,
        string branches,
        string status)
    {
        Key = key;
        Title = title;
        IsInDocument = isInDocument;
        IsSwitchable = isSwitchable;
        EvaluatedValue = evaluatedValue;
        OverrideValue = overrideValue;
        Branches = branches;
        Status = status;
    }

    /// <summary>
    /// Gets the key the override of the question is stored under.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the predicate, or the key of an override the document does not ask.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets a value indicating whether the current document asks the question.
    /// </summary>
    public bool IsInDocument { get; }

    /// <summary>
    /// Gets a value indicating whether the user can force an answer.
    /// </summary>
    public bool IsSwitchable { get; }

    /// <summary>
    /// Gets what the question evaluates to, or <see langword="null"/> when that is not known yet.
    /// </summary>
    public bool? EvaluatedValue { get; }

    /// <summary>
    /// Gets the answer the user forces, or <see langword="null"/> when the question is evaluated.
    /// </summary>
    public bool? OverrideValue { get; }

    /// <summary>
    /// Gets the answer the preview uses: the override, otherwise the evaluated one.
    /// </summary>
    public bool? Value => OverrideValue ?? EvaluatedValue;

    /// <summary>
    /// Gets a value indicating whether the user forces the answer.
    /// </summary>
    public bool IsOverridden => OverrideValue.HasValue;

    /// <summary>
    /// Gets which prefixes the question decides and whether their members are shown, for example
    /// <c>contract7Present: shown, contract7NotPresent: hidden</c>; empty for an override the document does not ask.
    /// </summary>
    public string Branches { get; }

    /// <summary>
    /// Gets where the answer comes from, for example <c>Overridden: does not hold. Evaluated: holds.</c>
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Creates the rows of the drop-down: one per question of the document, in document order, followed by the
    /// overrides of the project that the document does not ask, in the order of their keys.
    /// </summary>
    /// <param name="switches">The questions of the document; see <see cref="XamlConditionSwitches.Collect"/>.</param>
    /// <param name="overrides">The answers the user forces in the project.</param>
    /// <param name="evaluate">
    /// Returns what a question evaluates to without overrides, or <see langword="null"/> when that is not known;
    /// <see langword="null"/> when nothing is known.
    /// </param>
    public static IReadOnlyList<ConditionalXamlSwitchRow> Create(
        IReadOnlyList<XamlConditionSwitch> switches,
        XamlConditionOverrides? overrides,
        Func<XamlConditionQuery, bool?>? evaluate)
    {
        if (switches is null)
        {
            throw new ArgumentNullException(nameof(switches));
        }

        XamlConditionOverrides effective = overrides ?? XamlConditionOverrides.Empty;
        var rows = new List<ConditionalXamlSwitchRow>(switches.Count + effective.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (XamlConditionSwitch entry in switches)
        {
            keys.Add(entry.Key);
            rows.Add(CreateRow(entry, effective, evaluate));
        }

        foreach (string key in effective.Keys)
        {
            if (keys.Add(key) && effective.TryGetValue(key, out bool holds))
            {
                rows.Add(new ConditionalXamlSwitchRow(
                    key,
                    key,
                    isInDocument: false,
                    isSwitchable: true,
                    evaluatedValue: null,
                    overrideValue: holds,
                    string.Empty,
                    $"Overridden: {Describe(holds)}. This document does not use it."));
            }
        }

        return rows;
    }

    /// <summary>
    /// Returns the label of the drop-down button.
    /// </summary>
    /// <param name="overrideCount">The number of questions the user forces in the project.</param>
    public static string FormatButtonLabel(int overrideCount)
    {
        return overrideCount <= 0
            ? "Conditions"
            : string.Format(CultureInfo.InvariantCulture, "Conditions ({0} overridden)", overrideCount);
    }

    /// <summary>
    /// Returns the sentence under the rows that says how many questions the user forces in the project.
    /// </summary>
    public static string FormatOverrideSummary(int overrideCount)
    {
        return overrideCount switch
        {
            <= 0 => "No condition is overridden in this project.",
            1 => "1 condition is overridden in this project.",
            _ => string.Format(CultureInfo.InvariantCulture, "{0} conditions are overridden in this project.", overrideCount),
        };
    }

    private static ConditionalXamlSwitchRow CreateRow(XamlConditionSwitch entry, XamlConditionOverrides overrides, Func<XamlConditionQuery, bool?>? evaluate)
    {
        if (!entry.IsSwitchable)
        {
            return new ConditionalXamlSwitchRow(
                entry.Key,
                entry.DisplayText,
                isInDocument: true,
                isSwitchable: false,
                evaluatedValue: false,
                overrideValue: null,
                DescribeBranches(entry, holds: null, isSkipped: true),
                $"Cannot be switched: {entry.Error}. Its members are skipped.");
        }

        bool? overrideValue = overrides.TryGetValue(entry.Key, out bool forced) ? forced : null;
        bool? evaluatedValue = null;
        if (evaluate is not null)
        {
            try
            {
                evaluatedValue = evaluate(entry.Query);
            }
            catch (Exception)
            {
                // The value is shown as unknown; the preview does not depend on what is shown here.
            }
        }

        string evaluated = evaluatedValue.HasValue ? Describe(evaluatedValue.Value) + "." : "not known yet.";
        string status = overrideValue.HasValue
            ? $"Overridden: {Describe(overrideValue.Value)}. Evaluated: {evaluated}"
            : evaluatedValue.HasValue ? $"Evaluated: {evaluated}" : "Not evaluated yet.";

        return new ConditionalXamlSwitchRow(
            entry.Key,
            entry.DisplayText,
            isInDocument: true,
            isSwitchable: true,
            evaluatedValue,
            overrideValue,
            DescribeBranches(entry, overrideValue ?? evaluatedValue, isSkipped: false),
            status);
    }

    private static string DescribeBranches(XamlConditionSwitch entry, bool? holds, bool isSkipped)
    {
        var text = new StringBuilder();
        foreach (XamlConditionSwitchPrefix prefix in entry.Prefixes)
        {
            if (text.Length != 0)
            {
                text.Append(", ");
            }

            text.Append(prefix.Prefix).Append(": ");
            if (isSkipped)
            {
                text.Append("skipped");
            }
            else if (holds.HasValue)
            {
                text.Append(prefix.Applies(holds.Value) ? "shown" : "hidden");
            }
            else
            {
                text.Append(prefix.IsNegated ? "shown when it does not hold" : "shown when it holds");
            }
        }

        return text.ToString();
    }

    private static string Describe(bool holds)
    {
        return holds ? "holds" : "does not hold";
    }
}
