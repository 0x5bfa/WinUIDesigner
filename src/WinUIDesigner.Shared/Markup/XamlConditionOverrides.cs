// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinUIDesigner.Markup;

/// <summary>
/// The answers the user forces on conditions of conditional XAML namespaces, so that the preview shows the
/// branch of a condition that the designer would not show by itself.
/// </summary>
/// <remarks>
/// <para>
/// An override is kept per question, by <see cref="XamlConditionQuery.Key"/>: <c>IsApiContractPresent(X,7)</c> and
/// <c>IsApiContractNotPresent(X,7)</c> share one override, so the two branches of an if/else pair switch together.
/// The value says whether the positive form of the question holds.
/// </para>
/// <para>
/// Both designer processes consult the overrides before they evaluate a condition: the surface when it rewrites
/// prepared XAML, Visual Studio when it builds a document from construction actions. A condition that cannot be
/// evaluated because it is malformed is never overridden.
/// </para>
/// <para>
/// The value is immutable and compares by content. Its text form, which travels inside the design-time options,
/// is a comma-separated list of <c>key=1</c> (holds) and <c>key=0</c> (does not hold) entries in ordinal order of
/// the keys. The characters that structure the design-time options (<c>%</c>, <c>;</c>, <c>,</c> and <c>=</c>), white
/// space and control characters are written as <c>%XX</c>, or <c>%uXXXX</c> above U+00FF; everything else in a key,
/// including the parentheses, colons and arbitrary text of a custom condition, is written as it is.
/// </para>
/// </remarks>
internal sealed class XamlConditionOverrides : IEquatable<XamlConditionOverrides>
{
    private const char _EntrySeparator = ',';
    private const char _ValueSeparator = '=';
    private const char _Escape = '%';
    private const string _Holds = "1";
    private const string _DoesNotHold = "0";

    private readonly SortedDictionary<string, bool> _values;

    private XamlConditionOverrides(SortedDictionary<string, bool> values)
    {
        _values = values;
    }

    /// <summary>
    /// Gets the set without overrides: every condition is evaluated.
    /// </summary>
    public static XamlConditionOverrides Empty { get; } = new(new SortedDictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the number of overridden questions.
    /// </summary>
    public int Count => _values.Count;

    /// <summary>
    /// Gets a value indicating whether no question is overridden.
    /// </summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>
    /// Gets the keys of the overridden questions in ordinal order.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    public static bool operator ==(XamlConditionOverrides? left, XamlConditionOverrides? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(XamlConditionOverrides? left, XamlConditionOverrides? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Creates a set from <see cref="XamlConditionQuery.Key"/> and value pairs; a key given twice keeps its last value.
    /// </summary>
    public static XamlConditionOverrides Create(IEnumerable<KeyValuePair<string, bool>> values)
    {
        var copy = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> value in values ?? throw new ArgumentNullException(nameof(values)))
        {
            if (!string.IsNullOrEmpty(value.Key))
            {
                copy[value.Key] = value.Value;
            }
        }

        return copy.Count == 0 ? Empty : new XamlConditionOverrides(copy);
    }

    /// <summary>
    /// Returns whether a condition can be overridden: it has to be a well-formed predicate. Malformed text is
    /// decided by the grammar both processes share, and a namespace without a predicate always applies.
    /// </summary>
    public static bool CanOverride(XamlConditionQuery? query)
    {
        return query is not null && query.Condition.Kind is not (XamlConditionKind.Unknown or XamlConditionKind.Empty);
    }

    /// <summary>
    /// Reads the text form written by <see cref="Format"/>.
    /// </summary>
    /// <param name="text">The text form; <see langword="null"/> or empty reads as <see cref="Empty"/>.</param>
    /// <param name="overrides">The entries that could be read; an entry that cannot be read is skipped.</param>
    /// <returns>Whether every entry could be read.</returns>
    public static bool TryParse(string? text, out XamlConditionOverrides overrides)
    {
        overrides = Empty;
        if (text is null || text.Trim().Length == 0)
        {
            return true;
        }

        bool isComplete = true;
        var values = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (string entry in text.Split(_EntrySeparator))
        {
            int separator = entry.LastIndexOf(_ValueSeparator);
            string value = separator < 0 ? string.Empty : entry.Substring(separator + 1).Trim();
            bool? holds = value switch
            {
                _Holds => true,
                _DoesNotHold => false,
                _ => null,
            };

            if (separator <= 0 || !holds.HasValue || !TryUnescape(entry.Substring(0, separator).Trim(), out string key) || key.Length == 0)
            {
                // A stray separator, for example a trailing comma, is not worth a complaint.
                isComplete &= entry.Trim().Length == 0;
                continue;
            }

            values[key] = holds.Value;
        }

        overrides = values.Count == 0 ? Empty : new XamlConditionOverrides(values);
        return isComplete;
    }

    /// <summary>
    /// Returns an evaluator that answers an overridden question with its override and asks
    /// <paramref name="evaluate"/> about every other one. Both designer processes evaluate through it.
    /// </summary>
    /// <param name="overrides">The overrides in effect; <see langword="null"/> stands for <see cref="Empty"/>.</param>
    /// <param name="evaluate">Evaluates a question that is not overridden.</param>
    public static XamlConditionEvaluator CreateEvaluator(XamlConditionOverrides? overrides, Func<XamlConditionQuery, bool?> evaluate)
    {
        if (evaluate is null)
        {
            throw new ArgumentNullException(nameof(evaluate));
        }

        XamlConditionOverrides effective = overrides ?? Empty;
        return (condition, resolveNamespace) =>
        {
            XamlConditionQuery query = XamlConditionQuery.Create(condition, resolveNamespace);
            return effective.Evaluate(query) ?? evaluate(query);
        };
    }

    /// <summary>
    /// Returns whether the positive form of the question <paramref name="key"/> is forced to hold.
    /// </summary>
    public bool TryGetValue(string key, out bool holds)
    {
        holds = false;
        return key is not null && _values.TryGetValue(key, out holds);
    }

    /// <summary>
    /// Returns the forced answer for a condition as it is written, with the negation of a <c>...NotPresent</c>
    /// method applied, or <see langword="null"/> when the condition is not overridden or cannot be.
    /// </summary>
    public bool? Evaluate(XamlConditionQuery? query)
    {
        if (!CanOverride(query) || !_values.TryGetValue(query!.Key, out bool holds))
        {
            return null;
        }

        return query.Condition.IsNegated ? !holds : holds;
    }

    /// <summary>
    /// Returns a copy in which the question <paramref name="key"/> is forced to <paramref name="holds"/>.
    /// </summary>
    public XamlConditionOverrides With(string key, bool holds)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A condition key is required.", nameof(key));
        }

        if (_values.TryGetValue(key, out bool current) && current == holds)
        {
            return this;
        }

        var copy = new SortedDictionary<string, bool>(_values, StringComparer.Ordinal)
        {
            [key] = holds,
        };
        return new XamlConditionOverrides(copy);
    }

    /// <summary>
    /// Returns a copy in which the question <paramref name="key"/> is evaluated again.
    /// </summary>
    public XamlConditionOverrides Without(string key)
    {
        if (key is null || !_values.ContainsKey(key))
        {
            return this;
        }

        var copy = new SortedDictionary<string, bool>(_values, StringComparer.Ordinal);
        copy.Remove(key);
        return copy.Count == 0 ? Empty : new XamlConditionOverrides(copy);
    }

    /// <summary>
    /// Returns the text form, for example <c>IsApiContractPresent(Windows.Foundation.UniversalApiContract%2C7)=0</c>;
    /// an empty string when nothing is overridden.
    /// </summary>
    public string Format()
    {
        var text = new StringBuilder();
        foreach (KeyValuePair<string, bool> value in _values)
        {
            if (text.Length != 0)
            {
                text.Append(_EntrySeparator);
            }

            AppendEscaped(text, value.Key);
            text.Append(_ValueSeparator).Append(value.Value ? _Holds : _DoesNotHold);
        }

        return text.ToString();
    }

    public bool Equals(XamlConditionOverrides? other)
    {
        if (other is null || other._values.Count != _values.Count)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        foreach (KeyValuePair<string, bool> value in _values)
        {
            if (!other._values.TryGetValue(value.Key, out bool otherValue) || otherValue != value.Value)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as XamlConditionOverrides);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (KeyValuePair<string, bool> value in _values)
            {
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(value.Key);
                hash = (hash * 31) + (value.Value ? 1 : 2);
            }

            return hash;
        }
    }

    public override string ToString()
    {
        return Format();
    }

    private static bool NeedsEscape(char character)
    {
        return character is _Escape or ';' or _EntrySeparator or _ValueSeparator
            || char.IsWhiteSpace(character)
            || char.IsControl(character);
    }

    private static void AppendEscaped(StringBuilder text, string key)
    {
        foreach (char character in key)
        {
            if (!NeedsEscape(character))
            {
                text.Append(character);
            }
            else if (character <= 'ÿ')
            {
                text.Append(_Escape).Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(_Escape).Append('u').Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool TryUnescape(string text, out string value)
    {
        value = text;
        if (text.IndexOf(_Escape) < 0)
        {
            return true;
        }

        var builder = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character != _Escape)
            {
                builder.Append(character);
                continue;
            }

            bool isWide = index + 1 < text.Length && text[index + 1] == 'u';
            int start = isWide ? index + 2 : index + 1;
            int length = isWide ? 4 : 2;
            if (start + length > text.Length
                || !int.TryParse(text.Substring(start, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
            {
                return false;
            }

            builder.Append((char)code);
            index = start + length - 1;
        }

        value = builder.ToString();
        return true;
    }
}
