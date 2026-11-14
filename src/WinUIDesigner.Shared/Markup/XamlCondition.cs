// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinUIDesigner.Markup;

/// <summary>
/// What the predicate of a conditional XAML namespace asks.
/// </summary>
internal enum XamlConditionKind
{
    /// <summary>
    /// The text is not a predicate the XAML reader accepts; <see cref="XamlCondition.Error"/> says why.
    /// </summary>
    Unknown,

    /// <summary>
    /// There is no predicate after the <c>?</c>. The XAML reader treats such a namespace as unconditional.
    /// </summary>
    Empty,

    /// <summary>
    /// <c>IsApiContractPresent(contract, major[, minor])</c> or <c>IsApiContractNotPresent(...)</c>.
    /// </summary>
    ApiContract,

    /// <summary>
    /// <c>IsTypePresent(type)</c> or <c>IsTypeNotPresent(type)</c>.
    /// </summary>
    Type,

    /// <summary>
    /// <c>IsPropertyPresent(type, property)</c> or <c>IsPropertyNotPresent(type, property)</c>.
    /// </summary>
    Property,

    /// <summary>
    /// <c>prefix:ConditionType(argument)</c>: a type implementing <c>Microsoft.UI.Xaml.Markup.IXamlCondition</c>.
    /// </summary>
    Custom,
}

/// <summary>
/// The parsed predicate of a conditional XAML namespace: the text after the <c>?</c> in
/// <c>xmlns:c7="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)"</c>.
/// </summary>
/// <remarks>
/// The grammar is the one the WinUI XAML reader accepts, measured with Windows App SDK 2.5.1: a name, optionally
/// qualified by an <c>xmlns</c> prefix, directly followed by an argument list that ends the text. The reader does
/// not report most other texts as errors; it terminates the process. Those texts are <see cref="XamlConditionKind.Unknown"/> here.
/// </remarks>
internal sealed class XamlCondition
{
    private const string _ApiContractPresent = "IsApiContractPresent";
    private const string _ApiContractNotPresent = "IsApiContractNotPresent";
    private const string _TypePresent = "IsTypePresent";
    private const string _TypeNotPresent = "IsTypeNotPresent";
    private const string _PropertyPresent = "IsPropertyPresent";
    private const string _PropertyNotPresent = "IsPropertyNotPresent";

    private static readonly string[] _NoArguments = new string[0];
    private static readonly XamlCondition _Empty = new(
        string.Empty, XamlConditionKind.Empty, isNegated: false, string.Empty, string.Empty, string.Empty, _NoArguments, 0, null, string.Empty, error: null);

    private readonly string[] _arguments;

    private XamlCondition(
        string text,
        XamlConditionKind kind,
        bool isNegated,
        string prefix,
        string name,
        string argument,
        string[] arguments,
        int majorVersion,
        int? minorVersion,
        string positiveKey,
        string? error)
    {
        Text = text;
        Kind = kind;
        IsNegated = isNegated;
        Prefix = prefix;
        Name = name;
        Argument = argument;
        _arguments = arguments;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        PositiveKey = positiveKey;
        Error = error;
    }

    /// <summary>
    /// Gets the predicate exactly as it was written.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets what the predicate asks.
    /// </summary>
    public XamlConditionKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the predicate is the <c>...NotPresent</c> form of a built-in method.
    /// </summary>
    public bool IsNegated { get; }

    /// <summary>
    /// Gets the <c>xmlns</c> prefix the name is qualified with; empty when it has none. A custom condition
    /// without a prefix names a type in the default namespace.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets the method name of a built-in predicate as written, or the type name of a custom condition.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the text between the parentheses, untouched. It is what <c>IXamlCondition.Evaluate</c> receives.
    /// </summary>
    public string Argument { get; }

    /// <summary>
    /// Gets the arguments of a built-in predicate, without surrounding white space. Empty for the other kinds.
    /// </summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>
    /// Gets the major version of an API contract predicate.
    /// </summary>
    public int MajorVersion { get; }

    /// <summary>
    /// Gets the minor version of an API contract predicate, or <see langword="null"/> when none was written.
    /// </summary>
    public int? MinorVersion { get; }

    /// <summary>
    /// Gets a normalized form of the question the predicate asks, without the negation of a
    /// <c>...NotPresent</c> method and without insignificant white space. The two branches of an if/else pair
    /// share it, so one answer decides both. For a custom condition it is the type name with its argument;
    /// the namespace of the type is added by <see cref="XamlConditionQuery"/>.
    /// </summary>
    public string PositiveKey { get; }

    /// <summary>
    /// Gets what is wrong with the text when <see cref="Kind"/> is <see cref="XamlConditionKind.Unknown"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Returns whether <paramref name="name"/> is one of the six methods the XAML reader implements itself.
    /// </summary>
    public static bool IsBuiltInMethod(string? name)
    {
        return name is _ApiContractPresent or _ApiContractNotPresent
            or _TypePresent or _TypeNotPresent
            or _PropertyPresent or _PropertyNotPresent;
    }

    /// <summary>
    /// Parses the text after the <c>?</c> of a conditional namespace. Never throws.
    /// </summary>
    public static XamlCondition Parse(string? text)
    {
        if (text is null || text.Length == 0)
        {
            return _Empty;
        }

        int open = text.IndexOf('(');
        if (open < 0)
        {
            return Invalid(text, "the argument list is missing: a condition is written as Name(arguments)");
        }

        if (text[text.Length - 1] != ')')
        {
            return Invalid(text, text.IndexOf(')', open) < 0
                ? "the closing parenthesis is missing"
                : "there is text after the closing parenthesis");
        }

        // The argument list runs to the last parenthesis, so the argument of a custom condition may contain parentheses.
        string qualifiedName = text.Substring(0, open);
        string argument = text.Substring(open + 1, text.Length - open - 2);

        string prefix = string.Empty;
        string name = qualifiedName;
        int colon = qualifiedName.IndexOf(':');
        if (colon >= 0)
        {
            prefix = qualifiedName.Substring(0, colon);
            name = qualifiedName.Substring(colon + 1);
            if (!IsName(prefix, isPrefix: true))
            {
                return Invalid(text, $"'{prefix}' is not an xmlns prefix");
            }
        }

        if (!IsName(name, isPrefix: false))
        {
            // White space around the name is not skipped by the XAML reader either: it looks for a type of that name.
            return Invalid(text, $"'{name}' is not a condition name");
        }

        if (prefix.Length != 0 || !IsBuiltInMethod(name))
        {
            // The argument is opaque to everyone but the condition type, so no white space is dropped from it.
            // The namespace of the type is not part of the key: it depends on where the prefix is declared.
            string key = name + "(" + argument + ")";
            return new XamlCondition(text, XamlConditionKind.Custom, isNegated: false, prefix, name, argument, _NoArguments, 0, null, key, error: null);
        }

        return ParseBuiltIn(text, name, argument);
    }

    /// <summary>
    /// Returns this condition as the built-in method its name spells, ignoring the prefix. The XAML reader finds
    /// the built-in methods through any prefix that is bound to the presentation namespace.
    /// </summary>
    public XamlCondition WithoutPrefix()
    {
        return Prefix.Length == 0 ? this : Parse(Name + "(" + Argument + ")");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Text;
    }

    private static XamlCondition ParseBuiltIn(string text, string name, string argument)
    {
        string[] arguments = argument.Split(',');
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = arguments[index].Trim();
            if (arguments[index].Length == 0)
            {
                return Invalid(text, arguments.Length == 1 ? $"{name} needs arguments" : $"argument {(index + 1).ToString(CultureInfo.InvariantCulture)} of {name} is empty");
            }
        }

        switch (name)
        {
            case _ApiContractPresent:
            case _ApiContractNotPresent:
                if (arguments.Length != 2 && arguments.Length != 3)
                {
                    return Invalid(text, $"{name} takes a contract name, a major version and an optional minor version");
                }

                if (!TryParseVersion(arguments[1], out int major))
                {
                    return Invalid(text, $"'{arguments[1]}' is not a contract version: a whole number from 0 to 65535 is expected");
                }

                int? minor = null;
                if (arguments.Length == 3)
                {
                    if (!TryParseVersion(arguments[2], out int minorValue))
                    {
                        return Invalid(text, $"'{arguments[2]}' is not a contract version: a whole number from 0 to 65535 is expected");
                    }

                    minor = minorValue;
                }

                string contractKey = _ApiContractPresent + "(" + arguments[0] + "," + major.ToString(CultureInfo.InvariantCulture)
                    + (minor.HasValue ? "," + minor.Value.ToString(CultureInfo.InvariantCulture) : string.Empty) + ")";
                return new XamlCondition(text, XamlConditionKind.ApiContract, name == _ApiContractNotPresent, string.Empty, name, argument, arguments, major, minor, contractKey, error: null);

            case _TypePresent:
            case _TypeNotPresent:
                if (arguments.Length != 1)
                {
                    return Invalid(text, $"{name} takes one argument, a type name");
                }

                return new XamlCondition(text, XamlConditionKind.Type, name == _TypeNotPresent, string.Empty, name, argument, arguments, 0, null, _TypePresent + "(" + arguments[0] + ")", error: null);

            default:
                if (arguments.Length != 2)
                {
                    return Invalid(text, $"{name} takes two arguments, a type name and a property name");
                }

                return new XamlCondition(text, XamlConditionKind.Property, name == _PropertyNotPresent, string.Empty, name, argument, arguments, 0, null, _PropertyPresent + "(" + arguments[0] + "," + arguments[1] + ")", error: null);
        }
    }

    private static XamlCondition Invalid(string text, string error)
    {
        return new XamlCondition(text, XamlConditionKind.Unknown, isNegated: false, string.Empty, string.Empty, string.Empty, _NoArguments, 0, null, text, error);
    }

    /// <summary>
    /// Parses a contract version. The XAML reader is more forgiving: it reads leading digits and ignores what
    /// follows them, and ends the process when there is no digit at all. Only plain numbers are accepted here.
    /// </summary>
    private static bool TryParseVersion(string text, out int version)
    {
        version = 0;
        if (text.Length == 0 || text.Length > 5)
        {
            return false;
        }

        foreach (char character in text)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }

            version = (version * 10) + (character - '0');
        }

        return version <= ushort.MaxValue;
    }

    private static bool IsName(string text, bool isPrefix)
    {
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }

        for (int index = 1; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsLetterOrDigit(character) || character == '_')
            {
                continue;
            }

            // An xmlns prefix may contain '.' and '-'. A condition name is a simple type name:
            // the namespace of the type comes from the prefix.
            if (isPrefix && (character == '.' || character == '-'))
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
