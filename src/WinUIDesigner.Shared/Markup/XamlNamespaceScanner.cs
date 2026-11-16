// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace WinUIDesigner.Markup;

/// <summary>
/// Reads the namespace facts of XAML text without building a document model: the <c>xmlns</c> declarations,
/// the <c>mc:Ignorable</c> prefixes, and the prefix a tag or attribute was written with.
/// </summary>
internal static class XamlNamespaceScanner
{
    private const string _XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private const string _MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _IgnorableAttribute = "Ignorable";

    /// <summary>
    /// Scans <paramref name="xaml"/> for namespace declarations and platform-prefixed elements.
    /// Never throws: malformed XML ends the scan and yields the facts collected so far.
    /// </summary>
    public static XamlNamespaceScanResult Scan(string? xaml)
    {
        if (xaml is null || xaml.Length == 0)
        {
            return XamlNamespaceScanResult.Empty;
        }

        var declarations = new List<XamlNamespaceDeclaration>();
        var ignorablePrefixes = new HashSet<string>(StringComparer.Ordinal);
        bool hasConditionalNamespaces = false;
        bool hasPlatformNamespaces = false;
        bool hasPlatformPrefixedObjectElements = false;
        bool isComplete = false;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };

            using var text = new StringReader(xaml);
            using XmlReader reader = XmlReader.Create(text, settings);
            IXmlLineInfo? lineInfo = reader as IXmlLineInfo;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                int depth = reader.Depth;

                // The reader resolves the prefix against the declarations in scope, so a prefix
                // redeclared on a nested element is attributed to the right namespace.
                if (reader.Prefix.Length != 0
                    && reader.LocalName.IndexOf('.') < 0
                    && UnoPlatformPrefixes.IsPlatformUri(reader.NamespaceURI))
                {
                    hasPlatformPrefixedObjectElements = true;
                }

                if (!reader.MoveToFirstAttribute())
                {
                    continue;
                }

                do
                {
                    if (reader.NamespaceURI == _XmlnsNamespace)
                    {
                        // "xmlns" alone declares the default namespace; "xmlns:p" declares prefix "p".
                        string prefix = reader.Prefix.Length == 0 ? string.Empty : reader.LocalName;
                        XamlNamespaceUri uri = XamlNamespaceUri.Parse(reader.Value);
                        bool hasLineInfo = lineInfo is not null && lineInfo.HasLineInfo();
                        declarations.Add(new XamlNamespaceDeclaration(
                            prefix,
                            uri,
                            depth,
                            hasLineInfo ? lineInfo!.LineNumber : 0,
                            hasLineInfo ? lineInfo!.LinePosition : 0));

                        hasConditionalNamespaces |= uri.IsConditional;
                        hasPlatformNamespaces |= UnoPlatformPrefixes.IsPlatformUri(uri.BaseUri);
                    }
                    else if (reader.LocalName == _IgnorableAttribute && reader.NamespaceURI == _MarkupCompatibilityNamespace)
                    {
                        AddPrefixes(ignorablePrefixes, reader.Value);
                    }
                }
                while (reader.MoveToNextAttribute());

                reader.MoveToElement();
            }

            isComplete = true;
        }
        catch (Exception)
        {
            // Malformed or unreadable XAML: report what was read before the failure.
        }

        return new XamlNamespaceScanResult(
            declarations,
            ignorablePrefixes,
            hasConditionalNamespaces,
            hasPlatformNamespaces,
            hasPlatformPrefixedObjectElements,
            isComplete);
    }

    /// <summary>
    /// Returns the prefix an element was written with, given the text of its start tag (<c>&lt;win:Border ...</c>).
    /// </summary>
    /// <returns>
    /// The prefix, an empty string for an unprefixed element, or <see langword="null"/> when the text
    /// does not begin with an element start tag.
    /// </returns>
    public static string? ReadElementPrefix(string? startTagText)
    {
        if (startTagText is null)
        {
            return null;
        }

        int index = SkipWhitespace(startTagText, 0);
        if (index >= startTagText.Length || startTagText[index] != '<')
        {
            return null;
        }

        return ReadNamePrefix(startTagText, index + 1);
    }

    /// <summary>
    /// Returns the prefix an attribute was written with, given its text (<c>win:FontSize="24"</c>).
    /// </summary>
    /// <returns>
    /// The prefix (<c>xmlns</c> for a namespace declaration), an empty string for an unprefixed attribute,
    /// or <see langword="null"/> when the text does not begin with an attribute name.
    /// </returns>
    public static string? ReadAttributePrefix(string? attributeText)
    {
        if (attributeText is null)
        {
            return null;
        }

        return ReadNamePrefix(attributeText, SkipWhitespace(attributeText, 0));
    }

    private static string? ReadNamePrefix(string text, int start)
    {
        if (start >= text.Length || !IsNameStart(text[start]))
        {
            return null;
        }

        // A qualified name runs up to the first whitespace, '=', '/' or '>'.
        int colon = -1;
        int end = start;
        for (; end < text.Length; end++)
        {
            char character = text[end];
            if (char.IsWhiteSpace(character) || character == '=' || character == '/' || character == '>')
            {
                break;
            }

            if (character == ':')
            {
                if (colon >= 0)
                {
                    return null;
                }

                colon = end;
            }
        }

        if (colon < 0)
        {
            return string.Empty;
        }

        // "prefix:" without a local name is not a usable qualified name.
        if (colon == end - 1)
        {
            return null;
        }

        return text.Substring(start, colon - start);
    }

    private static bool IsNameStart(char character)
    {
        return char.IsLetter(character) || character == '_';
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static void AddPrefixes(HashSet<string> prefixes, string value)
    {
        int index = 0;
        while (index < value.Length)
        {
            index = SkipWhitespace(value, index);
            int start = index;
            while (index < value.Length && !char.IsWhiteSpace(value[index]))
            {
                index++;
            }

            if (index > start)
            {
                prefixes.Add(value.Substring(start, index - start));
            }
        }
    }
}
