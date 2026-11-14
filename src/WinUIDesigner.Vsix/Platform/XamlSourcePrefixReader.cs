// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Markup.Xaml;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// The <c>xmlns</c> prefix a document node or one of its properties was written with.
/// </summary>
internal readonly struct XamlSourcePrefix
{
    private readonly string? _prefix;
    private readonly XmlElementReference? _scope;

    public XamlSourcePrefix(string prefix, XmlNamespace? xmlNamespace, bool isIgnorable, XmlElementReference scope)
    {
        _prefix = prefix;
        _scope = scope;
        IsKnown = true;
        Namespace = xmlNamespace;
        IsIgnorable = isIgnorable;

        // The designer splits the condition off the namespace; the policies work with the declared value.
        Uri = xmlNamespace is null
            ? default
            : XamlNamespaceUri.Parse(string.IsNullOrEmpty(xmlNamespace.Condition)
                ? xmlNamespace.Value
                : xmlNamespace.Value + "?" + xmlNamespace.Condition);
    }

    /// <summary>
    /// Gets the result for a member whose source text is unavailable: the member has "no prefix".
    /// </summary>
    public static XamlSourcePrefix None => default;

    /// <summary>
    /// Gets a value indicating whether the source text of the member was read.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Gets the prefix; empty for a member written without one.
    /// </summary>
    public string Prefix => _prefix ?? string.Empty;

    /// <summary>
    /// Gets the namespace the prefix is bound to where the member is written, with its
    /// <see cref="XmlNamespace.Condition"/>. It is <see langword="null"/> for an unprefixed attribute
    /// and for a prefix that is not declared.
    /// </summary>
    public XmlNamespace? Namespace { get; }

    /// <summary>
    /// Gets <see cref="Namespace"/> in the form the namespace policies work with.
    /// </summary>
    public XamlNamespaceUri Uri { get; }

    /// <summary>
    /// Gets a value indicating whether the namespace is listed in <c>mc:Ignorable</c> where the member is written.
    /// </summary>
    public bool IsIgnorable { get; }

    /// <summary>
    /// Returns the namespace another prefix is bound to where the member is written, or <see langword="null"/>.
    /// </summary>
    public string? ResolveNamespace(string prefix)
    {
        try
        {
            return _scope?.GetXmlNamespace(XmlnsPrefix.ToPrefix(prefix))?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads the prefix a document node was written with from the XAML source text.
/// </summary>
/// <remarks>
/// The document model resolves a prefix to its namespace while parsing and does not keep it, yet
/// <c>win</c>, <c>not_android</c> and the default namespace all resolve to the same namespace.
/// The prefix is therefore read back from the text range the node was parsed from.
/// This is the only place that depends on the source-reference types of the designer.
/// </remarks>
internal static class XamlSourcePrefixReader
{
    // A qualified name is short; the limit bounds what is read for a start tag of any length.
    private const int _MaxNameLength = 512;

    /// <summary>
    /// Returns the prefix the element of <paramref name="node"/> was written with.
    /// </summary>
    /// <returns>The prefix, or <see cref="XamlSourcePrefix.None"/> when it cannot be read for any reason.</returns>
    public static XamlSourcePrefix ReadElementPrefix(DocumentNode? node)
    {
        try
        {
            return node?.SourceContext is XmlElementReference element
                ? ReadElementPrefix(element)
                : XamlSourcePrefix.None;
        }
        catch (Exception)
        {
            return XamlSourcePrefix.None;
        }
    }

    /// <summary>
    /// Returns the prefix <paramref name="property"/> was written with on <paramref name="parentNode"/>,
    /// either as an attribute (<c>win:FontSize="24"</c>) or as a property element (<c>&lt;win:Border.Child&gt;</c>).
    /// </summary>
    /// <returns>The prefix, or <see cref="XamlSourcePrefix.None"/> when it cannot be read for any reason.</returns>
    public static XamlSourcePrefix ReadPropertyPrefix(DocumentCompositeNode? parentNode, IProperty? property)
    {
        try
        {
            if (parentNode is null || property is null)
            {
                return XamlSourcePrefix.None;
            }

            // A property set through element content has no container of its own.
            return parentNode.GetContainerContext(property) switch
            {
                XmlElementReference propertyElement => ReadElementPrefix(propertyElement),
                XmlAttributeReference attribute => ReadAttributePrefix(attribute),
                _ => XamlSourcePrefix.None,
            };
        }
        catch (Exception)
        {
            return XamlSourcePrefix.None;
        }
    }

    private static XamlSourcePrefix ReadElementPrefix(XmlElementReference element)
    {
        // An element with content tracks its start tag separately; a self-closing element is its own start tag.
        string? prefix = XamlNamespaceScanner.ReadElementPrefix(ReadText(element, element.StartTagRange ?? element.TextRange));
        return prefix is null ? XamlSourcePrefix.None : Resolve(prefix, element);
    }

    private static XamlSourcePrefix ReadAttributePrefix(XmlAttributeReference attribute)
    {
        if (attribute.Parent is not XmlElementReference owner)
        {
            return XamlSourcePrefix.None;
        }

        string? prefix = XamlNamespaceScanner.ReadAttributePrefix(ReadText(attribute, attribute.TextRange));
        if (prefix is null)
        {
            return XamlSourcePrefix.None;
        }

        // The default namespace applies to elements only: an unprefixed attribute is in no namespace.
        return prefix.Length == 0
            ? new XamlSourcePrefix(prefix, xmlNamespace: null, isIgnorable: false, owner)
            : Resolve(prefix, owner);
    }

    private static XamlSourcePrefix Resolve(string prefix, XmlElementReference scope)
    {
        XmlNamespace? xmlNamespace = scope.GetXmlNamespace(XmlnsPrefix.ToPrefix(prefix));
        bool isIgnorable = xmlNamespace is not null && scope.IsIgnorable(xmlNamespace);
        return new XamlSourcePrefix(prefix, xmlNamespace, isIgnorable, scope);
    }

    private static string? ReadText(XamlSourceContext context, ITextRange? range)
    {
        IReadableSelectableTextBuffer? buffer = context.TextBuffer;
        if (buffer is null || range is null)
        {
            return null;
        }

        int offset = range.Offset;
        if (offset < 0)
        {
            return null;
        }

        int length = Math.Min(Math.Min(range.Length, _MaxNameLength), buffer.Length - offset);
        return length > 0 ? buffer.GetText(offset, length) : null;
    }
}
