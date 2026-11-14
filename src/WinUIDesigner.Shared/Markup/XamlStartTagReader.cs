// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;

namespace WinUIDesigner.Markup;

/// <summary>
/// The qualified name of an attribute as it is written in XAML text.
/// </summary>
internal readonly struct XamlAttributeName
{
    private readonly string? _prefix;
    private readonly string? _localName;

    public XamlAttributeName(string prefix, string localName)
    {
        _prefix = prefix;
        _localName = localName;
    }

    /// <summary>
    /// Gets the prefix; empty for an attribute written without one.
    /// </summary>
    public string Prefix => _prefix ?? string.Empty;

    /// <summary>
    /// Gets the name without the prefix, for example <c>CornerRadius</c> or <c>Grid.Row</c>.
    /// </summary>
    public string LocalName => _localName ?? string.Empty;

    /// <inheritdoc/>
    public override string ToString()
    {
        return Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;
    }
}

/// <summary>
/// Reads the attribute names of an element start tag from its text, without building a document model.
/// </summary>
/// <remarks>
/// The designer's document model keeps one value per property. When a property is written twice under
/// different prefixes, as an if/else pair of conditional attributes is, only the text still shows both.
/// </remarks>
internal static class XamlStartTagReader
{
    /// <summary>
    /// Returns the names of the attributes in <paramref name="startTagText"/>, in the order they are written.
    /// Never throws: the names read up to malformed text are returned.
    /// </summary>
    /// <param name="startTagText">Text that begins with an element start tag, such as <c>&lt;Border c7:CornerRadius="4" /&gt;</c>.</param>
    public static IReadOnlyList<XamlAttributeName> ReadAttributeNames(string? startTagText)
    {
        var names = new List<XamlAttributeName>();
        if (startTagText is null)
        {
            return names;
        }

        int index = SkipWhitespace(startTagText, 0);
        if (index >= startTagText.Length || startTagText[index] != '<')
        {
            return names;
        }

        // The element name runs up to the first white space or the end of the tag.
        index++;
        while (index < startTagText.Length && !char.IsWhiteSpace(startTagText[index]) && startTagText[index] != '>' && startTagText[index] != '/')
        {
            index++;
        }

        while (true)
        {
            index = SkipWhitespace(startTagText, index);
            if (index >= startTagText.Length || startTagText[index] == '>' || startTagText[index] == '/')
            {
                return names;
            }

            int nameStart = index;
            while (index < startTagText.Length && !char.IsWhiteSpace(startTagText[index]) && startTagText[index] != '=' && startTagText[index] != '>' && startTagText[index] != '/')
            {
                index++;
            }

            string qualifiedName = startTagText.Substring(nameStart, index - nameStart);

            index = SkipWhitespace(startTagText, index);
            if (index >= startTagText.Length || startTagText[index] != '=')
            {
                return names;
            }

            index = SkipWhitespace(startTagText, index + 1);
            if (index >= startTagText.Length || (startTagText[index] != '"' && startTagText[index] != '\''))
            {
                return names;
            }

            // The value may contain '>' and the other kind of quote; it ends at the quote it began with.
            int valueEnd = startTagText.IndexOf(startTagText[index], index + 1);
            if (valueEnd < 0)
            {
                return names;
            }

            index = valueEnd + 1;

            if (!TryParseName(qualifiedName, out XamlAttributeName name))
            {
                // Not a qualified name; XML would reject the tag, so nothing after it can be trusted.
                return names;
            }

            names.Add(name);
        }
    }

    /// <summary>
    /// Reads the name of an attribute from its text, such as <c>c7:CornerRadius="4"</c>.
    /// </summary>
    /// <returns><see langword="false"/> when the text does not begin with an attribute name.</returns>
    public static bool TryReadAttributeName(string? attributeText, out XamlAttributeName name)
    {
        name = default;
        if (attributeText is null)
        {
            return false;
        }

        int start = SkipWhitespace(attributeText, 0);
        int end = start;
        while (end < attributeText.Length && !char.IsWhiteSpace(attributeText[end]) && attributeText[end] != '=' && attributeText[end] != '>' && attributeText[end] != '/')
        {
            end++;
        }

        return TryParseName(attributeText.Substring(start, end - start), out name);
    }

    private static bool TryParseName(string qualifiedName, out XamlAttributeName name)
    {
        int colon = qualifiedName.IndexOf(':');
        if (qualifiedName.Length == 0 || colon == 0 || colon == qualifiedName.Length - 1 || colon != qualifiedName.LastIndexOf(':'))
        {
            name = default;
            return false;
        }

        name = colon < 0
            ? new XamlAttributeName(string.Empty, qualifiedName)
            : new XamlAttributeName(qualifiedName.Substring(0, colon), qualifiedName.Substring(colon + 1));
        return true;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }
}
