// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;

namespace WinUIDesigner.Markup;

/// <summary>
/// Evaluates the predicates of conditional XAML namespaces and remembers the answers.
/// </summary>
/// <remarks>
/// An answer is kept per question, not per predicate: <c>IsApiContractPresent(X,7)</c> and
/// <c>IsApiContractNotPresent(X,7)</c> ask the same question, so the two branches of an if/else pair can
/// never both apply or both be skipped. The XAML reader keeps its answers for the lifetime of the process
/// as well. Instances are safe to use from any thread; a question may be probed twice when two threads ask
/// it for the first time at the same moment.
/// </remarks>
internal sealed class XamlConditionService
{
    private const string _WindowsNamespacePrefix = "Windows.";

    private readonly XamlConditionProbeSet _probes;
    private readonly Action<string>? _report;
    private readonly ConcurrentDictionary<string, bool?> _outcomes = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="XamlConditionService"/> class.
    /// </summary>
    /// <param name="probes">The questions this process can answer about the running system.</param>
    /// <param name="report">Receives a message, once per question, when a condition counts as not satisfied because of a problem.</param>
    public XamlConditionService(XamlConditionProbeSet probes, Action<string>? report = null)
    {
        _probes = probes ?? throw new ArgumentNullException(nameof(probes));
        _report = report;
    }

    /// <summary>
    /// Evaluates a condition where it is declared. The signature is that of <see cref="XamlConditionEvaluator"/>.
    /// </summary>
    /// <returns>
    /// Whether the condition holds, or <see langword="null"/> when this process has no probe that could tell.
    /// A condition that is malformed or fails counts as not satisfied.
    /// </returns>
    public bool? Evaluate(string condition, Func<string, string?> resolveNamespace)
    {
        return Evaluate(XamlConditionQuery.Create(condition, resolveNamespace));
    }

    /// <summary>
    /// Evaluates a resolved condition.
    /// </summary>
    public bool? Evaluate(XamlConditionQuery query)
    {
        XamlCondition condition = query.Condition;
        if (condition.Kind == XamlConditionKind.Empty)
        {
            // "namespace?" without a predicate: the XAML reader applies such members unconditionally.
            return true;
        }

        if (!_outcomes.TryGetValue(query.Key, out bool? outcome))
        {
            outcome = _outcomes.GetOrAdd(query.Key, Probe(query));
        }

        return outcome.HasValue && condition.IsNegated ? !outcome.Value : outcome;
    }

    /// <summary>
    /// Records the answer to a question as another process gave it; it replaces what this process found out itself.
    /// </summary>
    /// <param name="key">The <see cref="XamlConditionQuery.Key"/> of the question.</param>
    /// <param name="outcome">Whether the positive form of the condition holds.</param>
    /// <returns>Whether the answer differs from what was known before.</returns>
    public bool SetOutcome(string key, bool outcome)
    {
        bool changed = !_outcomes.TryGetValue(key, out bool? known) || known != outcome;
        _outcomes[key] = outcome;
        return changed;
    }

    private bool? Probe(XamlConditionQuery query)
    {
        XamlCondition condition = query.Condition;
        try
        {
            switch (condition.Kind)
            {
                case XamlConditionKind.ApiContract:
                    return _probes.IsApiContractPresent?.Invoke(condition.Arguments[0], condition.MajorVersion, condition.MinorVersion);

                case XamlConditionKind.Type:
                    Func<string, bool>? isTypePresent = IsWindowsName(condition.Arguments[0])
                        ? _probes.IsWindowsTypePresent
                        : _probes.IsManagedTypePresent;
                    return isTypePresent?.Invoke(condition.Arguments[0]);

                case XamlConditionKind.Property:
                    Func<string, string, bool>? isPropertyPresent = IsWindowsName(condition.Arguments[0])
                        ? _probes.IsWindowsPropertyPresent
                        : _probes.IsManagedPropertyPresent;
                    return isPropertyPresent?.Invoke(condition.Arguments[0], condition.Arguments[1]);

                case XamlConditionKind.Custom:
                    return ProbeCustom(query);

                default:
                    Report($"'{condition.Text}' is not a condition the XAML reader accepts: {condition.Error}. Its members are skipped.");
                    return false;
            }
        }
        catch (Exception exception)
        {
            Report($"Evaluating the condition '{condition.Text}' failed; it counts as not satisfied. {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool? ProbeCustom(XamlConditionQuery query)
    {
        XamlCondition condition = query.Condition;
        if (query.PredicateNamespace is null)
        {
            Report(condition.Prefix.Length == 0
                ? $"'{condition.Name}' in '{condition.Text}' is not a conditional XAML method, and there is no default namespace to find a condition type of that name in. The condition counts as not satisfied."
                : $"The prefix '{condition.Prefix}' of the condition '{condition.Text}' is not declared. The condition counts as not satisfied.");
            return false;
        }

        if (query.TypeName is null)
        {
            Report(condition.Prefix.Length == 0
                ? $"'{condition.Name}' in '{condition.Text}' is not a conditional XAML method, and the default namespace '{query.PredicateNamespace}' names no CLR namespace (using:...) that could contain a condition type of that name. The condition counts as not satisfied."
                : $"The namespace '{query.PredicateNamespace}' of the condition '{condition.Text}' names no CLR namespace (using:...). The condition counts as not satisfied.");
            return false;
        }

        Func<string, string, bool?>? evaluateCustom = _probes.EvaluateCustom;
        if (evaluateCustom is null)
        {
            // Only a process that can load the condition type can tell.
            return null;
        }

        bool? outcome = evaluateCustom(query.TypeName, condition.Argument);
        if (outcome is null)
        {
            Report($"The condition type '{query.TypeName}' of '{condition.Text}' could not be evaluated. The condition counts as not satisfied.");
            return false;
        }

        return outcome;
    }

    private static bool IsWindowsName(string typeName)
    {
        return typeName.StartsWith(_WindowsNamespacePrefix, StringComparison.Ordinal);
    }

    private void Report(string message)
    {
        try
        {
            _report?.Invoke(message);
        }
        catch (Exception)
        {
            // A diagnostics receiver must not change the outcome of a condition.
        }
    }
}
