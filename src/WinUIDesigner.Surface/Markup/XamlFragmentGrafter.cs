// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Inserts the root element of one XAML document into another, so that the WinUI XAML reader reads the inserted
/// part as it reads the document on its own.
/// </summary>
/// <remarks>
/// Moving the element is not enough, because of two rules of the reader, measured with Windows App SDK 2.5.1:
/// <list type="bullet">
/// <item><c>mc:Ignorable</c> is read on the root element only. On a nested element the reader looks for a
/// property named <c>Ignorable</c> and fails.</item>
/// <item>A prefix declared again on a nested element keeps the namespace of the outer declaration for the names
/// of elements, while attribute values use the inner one. Visual Studio's cleaner names the prefixes it adds
/// <c>_0</c>, <c>_1</c> and so on in every file, each time for other namespaces, so two prepared files nearly
/// always disagree on some prefix.</item>
/// </list>
/// The inserted element therefore loses what the reader would ignore, and every prefix of it that an enclosing
/// element binds to another namespace is renamed, in its declaration and in the attribute values that use it.
/// </remarks>
internal static class XamlFragmentGrafter
{
    private const string _MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XNamespace _MarkupCompatibility = _MarkupCompatibilityNamespace;
    private static readonly XName _IgnorableName = _MarkupCompatibility + "Ignorable";

    // Directives that only the root element of a document may carry.
    private static readonly XName[] _RootOnlyDirectives =
    [
        XName.Get("Class", _XamlLanguageNamespace),
        XName.Get("ClassModifier", _XamlLanguageNamespace),
        XName.Get("Subclass", _XamlLanguageNamespace),
    ];

    /// <summary>
    /// Prepares the root element of a document to be read as a nested element of another document.
    /// </summary>
    /// <param name="fragment">The root element of the document, detached or not yet inserted.</param>
    /// <remarks>
    /// Removes the elements and attributes in a namespace that <c>mc:Ignorable</c> lists, which the reader skips,
    /// every <c>mc:</c> attribute of the element, and the directives only a root can have, such as <c>x:Class</c>.
    /// </remarks>
    public static void PrepareAsNestedElement(XElement fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        if (fragment.Attribute(_IgnorableName) is { } ignorable)
        {
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (string prefix in ignorable.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                XNamespace? ignored = fragment.GetNamespaceOfPrefix(prefix);
                if (ignored is not null && ignored != XNamespace.None)
                {
                    namespaces.Add(ignored.NamespaceName);
                }
            }

            if (namespaces.Count != 0)
            {
                RemoveMembersOf(fragment, namespaces);
            }
        }

        foreach (XAttribute attribute in fragment.Attributes().Where(attribute => attribute.Name.Namespace == _MarkupCompatibility).ToList())
        {
            attribute.Remove();
        }

        RemoveClassDirectives(fragment);
    }

    /// <summary>
    /// Removes <c>x:Class</c> and the directives that go with it from <paramref name="root"/>. The XAML reader
    /// rejects them: it loads markup, not a class with code behind.
    /// </summary>
    public static void RemoveClassDirectives(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (XName directive in _RootOnlyDirectives)
        {
            root.Attribute(directive)?.Remove();
        }
    }

    /// <summary>
    /// Appends <paramref name="fragment"/> to <paramref name="parent"/> and renames the prefixes of the fragment
    /// that the elements enclosing it bind to other namespaces.
    /// </summary>
    /// <returns>The number of prefix declarations that were renamed.</returns>
    public static int Append(XElement parent, XElement fragment)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(fragment);

        parent.Add(fragment);

        // The fragment may have been copied on the way in; continue with the element that is in the document now.
        return RenameShadowingPrefixes((XElement)parent.LastNode!);
    }

    /// <summary>
    /// Renames every prefix declared in <paramref name="element"/> or below it that an enclosing element binds to
    /// another namespace, so that each prefix means one namespace along every path from the root.
    /// </summary>
    /// <returns>The number of prefix declarations that were renamed.</returns>
    public static int RenameShadowingPrefixes(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        int renamed = 0;
        HashSet<string>? taken = null;

        // Top-down: an enclosing declaration is final before the ones inside it are compared with it.
        foreach (XElement current in element.DescendantsAndSelf().ToList())
        {
            foreach (XAttribute declaration in current.Attributes().Where(IsPrefixDeclaration).ToList())
            {
                string prefix = declaration.Name.LocalName;
                XNamespace? enclosing = current.Parent?.GetNamespaceOfPrefix(prefix);
                if (enclosing is null || string.Equals(enclosing.NamespaceName, declaration.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                taken ??= CollectDeclaredPrefixes(element.AncestorsAndSelf().Last());
                string replacement = CreatePrefix(prefix, taken);
                taken.Add(replacement);
                RenamePrefix(current, declaration, replacement);
                renamed++;
            }
        }

        return renamed;
    }

    private static void RemoveMembersOf(XElement root, HashSet<string> namespaces)
    {
        // The reader skips an ignorable element together with its content.
        foreach (XElement element in root.Descendants().Where(element => namespaces.Contains(element.Name.NamespaceName)).ToList())
        {
            element.Remove();
        }

        foreach (XElement element in root.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && namespaces.Contains(attribute.Name.NamespaceName)).ToList())
            {
                attribute.Remove();
            }
        }
    }

    private static bool IsPrefixDeclaration(XAttribute attribute)
    {
        // "xmlns" declares the default namespace, which the reader does let nested elements declare again.
        return attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.Xmlns;
    }

    private static HashSet<string> CollectDeclaredPrefixes(XElement root)
    {
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement element in root.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (IsPrefixDeclaration(attribute))
                {
                    prefixes.Add(attribute.Name.LocalName);
                }
            }
        }

        return prefixes;
    }

    private static string CreatePrefix(string prefix, HashSet<string> taken)
    {
        for (int number = 1; ; number++)
        {
            string candidate = prefix + "_" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Renames the prefix that <paramref name="declaration"/> declares on <paramref name="scope"/>: the declaration
    /// itself, and the uses of the prefix in attribute values where that declaration is in scope.
    /// </summary>
    private static void RenamePrefix(XElement scope, XAttribute declaration, string replacement)
    {
        string prefix = declaration.Name.LocalName;
        string namespaceName = declaration.Value;
        declaration.Remove();

        // LINQ to XML writes element and attribute names with a prefix that is in scope for their namespace,
        // so the names follow the new declaration by themselves; attribute values do not.
        scope.Add(new XAttribute(XNamespace.Xmlns + replacement, namespaceName));

        Regex reference = CreateReferencePattern(prefix);
        var pending = new Stack<XElement>();
        pending.Push(scope);
        while (pending.Count != 0)
        {
            XElement element = pending.Pop();
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                attribute.Value = attribute.Name.Namespace == _MarkupCompatibility
                    ? RenameListedPrefix(attribute.Value, prefix, replacement)
                    : RenameReferences(attribute.Value, prefix, replacement, reference);
            }

            foreach (XElement child in element.Elements())
            {
                // A child that declares the prefix again starts a scope of its own.
                if (child.Attribute(XNamespace.Xmlns + prefix) is null)
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>
    /// Rewrites the uses of a prefix in an attribute value: <c>p:Type</c>, <c>{p:Extension ...}</c>,
    /// <c>(p:Owner.Property)</c> and the like. A use is the prefix followed by a colon and a name, where the prefix
    /// is not the end of a longer name or of a URI scheme.
    /// </summary>
    /// <remarks>
    /// The value of an attribute is not parsed as a markup extension, so literal text that happens to read like a
    /// qualified name with this prefix, such as <c>Text="p:A"</c>, changes as well. Only prefixes that two
    /// documents bind to different namespaces are renamed, which keeps this rare.
    /// </remarks>
    internal static string RenameReferences(string value, string prefix, string replacement)
    {
        return RenameReferences(value, prefix, replacement, CreateReferencePattern(prefix));
    }

    private static string RenameReferences(string value, string prefix, string replacement, Regex reference)
    {
        return value.IndexOf(prefix + ":", StringComparison.Ordinal) < 0
            ? value
            : reference.Replace(value, replacement + ":");
    }

    private static Regex CreateReferencePattern(string prefix)
    {
        return new Regex(@"(?<![\w.:\-])" + Regex.Escape(prefix) + @":(?=[\p{L}_])", RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Rewrites a list of the markup-compatibility attributes: prefixes (<c>mc:Ignorable</c>) or qualified names
    /// (<c>mc:ProcessContent</c>), separated by white space.
    /// </summary>
    private static string RenameListedPrefix(string value, string prefix, string replacement)
    {
        string[] tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool changed = false;
        var text = new StringBuilder(value.Length + 8);
        foreach (string token in tokens)
        {
            string renamed = token;
            if (string.Equals(token, prefix, StringComparison.Ordinal))
            {
                renamed = replacement;
            }
            else if (token.StartsWith(prefix + ":", StringComparison.Ordinal))
            {
                renamed = replacement + token[prefix.Length..];
            }

            changed |= !ReferenceEquals(renamed, token);
            text.Append(text.Length == 0 ? string.Empty : " ").Append(renamed);
        }

        return changed ? text.ToString() : value;
    }
}
