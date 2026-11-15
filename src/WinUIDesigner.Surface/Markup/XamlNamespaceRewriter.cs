// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Applies the namespace policies to prepared XAML before it reaches the XAML reader: members under a dropped
/// prefix are removed, and members under a kept prefix become plain members of their bare namespace.
/// </summary>
/// <remarks>
/// The rewriter only knows the document model. It decides nothing itself: every prefix is classified by
/// <see cref="XamlNamespacePolicies"/>, and a document whose prefixes are all left alone is not touched.
/// </remarks>
internal static class XamlNamespaceRewriter
{
    private const string _XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly XName _IgnorableName = XName.Get("Ignorable", _MarkupCompatibilityNamespace);

    /// <summary>
    /// Rewrites <paramref name="document"/> in place according to the namespace policies.
    /// </summary>
    /// <param name="document">The parsed XAML. Elements that survive keep their identity and line information.</param>
    /// <param name="options">The policy inputs and callbacks of this run.</param>
    /// <returns>What changed. With no policy namespace in the document nothing changes.</returns>
    /// <remarks>
    /// A failing callback is reported and treated as having no opinion. Should the method itself throw,
    /// the document may be partly rewritten and must be discarded.
    /// </remarks>
    public static XamlNamespaceRewriteResult Rewrite(XDocument document, XamlNamespaceRewriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        XElement? root = document.Root;
        if (root is null)
        {
            return XamlNamespaceRewriteResult.Unchanged;
        }

        return new Session(document, options).Run(root);
    }

    /// <summary>
    /// Records on every element and attribute of <paramref name="document"/> the prefix it was written with
    /// in <paramref name="xaml"/>, the text the document was parsed from.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the text does not match the document; nothing is recorded in that case.
    /// </returns>
    /// <remarks>
    /// LINQ to XML keeps the namespace of a name but not its prefix, so <c>&lt;win:Border&gt;</c> and
    /// <c>&lt;Border&gt;</c> are indistinguishable once parsed when both prefixes are bound to one namespace.
    /// </remarks>
    public static bool CaptureSourcePrefixes(XDocument document, string xaml)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(xaml);

        // Prefixes recorded earlier would otherwise shadow the ones read now.
        ClearSourcePrefixes(document);

        try
        {
            // Read the text the same way XDocument.Parse does, so both see the same elements and attributes.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Parse,
                MaxCharactersFromEntities = 10_000_000,
                XmlResolver = null,
            };

            var prefixes = new Dictionary<string, SourcePrefix>(StringComparer.Ordinal);
            using var text = new StringReader(xaml);
            using XmlReader reader = XmlReader.Create(text, settings);
            using IEnumerator<XElement> elements = document.Descendants().GetEnumerator();

            bool matches = true;
            while (matches && reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!elements.MoveNext())
                {
                    matches = false;
                    break;
                }

                XElement element = elements.Current;
                if (element.Name.LocalName != reader.LocalName || element.Name.NamespaceName != reader.NamespaceURI)
                {
                    matches = false;
                    break;
                }

                element.AddAnnotation(GetSourcePrefix(prefixes, reader.Prefix));

                XAttribute? attribute = element.FirstAttribute;
                if (reader.MoveToFirstAttribute())
                {
                    do
                    {
                        if (attribute is null || attribute.Name.LocalName != reader.LocalName)
                        {
                            matches = false;
                            break;
                        }

                        attribute.AddAnnotation(GetSourcePrefix(prefixes, reader.Prefix));
                        attribute = attribute.NextAttribute;
                    }
                    while (reader.MoveToNextAttribute());

                    reader.MoveToElement();
                }

                matches &= attribute is null;
            }

            if (matches && !elements.MoveNext())
            {
                return true;
            }
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            // The text is not the well-formed XML the document was parsed from.
        }

        ClearSourcePrefixes(document);
        return false;
    }

    private static SourcePrefix GetSourcePrefix(Dictionary<string, SourcePrefix> prefixes, string prefix)
    {
        if (!prefixes.TryGetValue(prefix, out SourcePrefix? sourcePrefix))
        {
            prefixes[prefix] = sourcePrefix = new SourcePrefix(prefix);
        }

        return sourcePrefix;
    }

    private static void ClearSourcePrefixes(XDocument document)
    {
        foreach (XElement element in document.Descendants())
        {
            element.RemoveAnnotations<SourcePrefix>();
            for (XAttribute? attribute = element.FirstAttribute; attribute is not null; attribute = attribute.NextAttribute)
            {
                attribute.RemoveAnnotations<SourcePrefix>();
            }
        }
    }

    private static string GetDeclaredPrefix(XAttribute declaration)
    {
        // "xmlns" declares the default namespace; "xmlns:p" declares prefix "p".
        return declaration.Name.NamespaceName.Length == 0 ? string.Empty : declaration.Name.LocalName;
    }

    private static string GetNormalizedNamespace(XamlNamespaceUri uri)
    {
        // "#using:" names the CLR namespace of the members; several namespaces cannot be told apart
        // without type information, so the first one is taken.
        if (uri.UsingNamespaces.Count != 0)
        {
            return XamlNamespaceUri.UsingScheme + uri.UsingNamespaces[0];
        }

        // An Uno platform namespace stands for the default namespace on that platform.
        if (UnoPlatformPrefixes.IsPlatformUri(uri.BaseUri))
        {
            return XamlNamespaceUri.PresentationNamespace;
        }

        return uri.BaseUri;
    }

    private static void AddPrefixes(HashSet<string> prefixes, string value)
    {
        foreach (string prefix in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            prefixes.Add(prefix);
        }
    }

    /// <summary>
    /// The prefix an element or attribute was written with, attached to it as an annotation.
    /// </summary>
    private sealed class SourcePrefix(string value)
    {
        public string Value { get; } = value;
    }

    /// <summary>
    /// One <c>xmlns</c> declaration that is in scope during the walk.
    /// </summary>
    private sealed class Binding(string prefix, XAttribute declaration)
    {
        public string Prefix { get; } = prefix;

        public XAttribute Declaration { get; } = declaration;

        // Parsed from the declared value: re-pointing the declaration later must not change the verdict.
        public XamlNamespaceUri Uri { get; } = XamlNamespaceUri.Parse(declaration.Value);

        public string NormalizedNamespace => GetNormalizedNamespace(Uri);
    }

    /// <summary>
    /// An element whose children are still being visited, with the scope changes to undo afterwards.
    /// </summary>
    private sealed class Frame(List<XElement> children, List<KeyValuePair<string, Binding?>>? shadowed, List<Binding>? ignorable)
    {
        public List<XElement> Children { get; } = children;

        public int NextChild { get; set; }

        public List<KeyValuePair<string, Binding?>>? Shadowed { get; } = shadowed;

        public List<Binding>? Ignorable { get; } = ignorable;
    }

    /// <summary>
    /// One rewrite of one document.
    /// </summary>
    private sealed class Session
    {
        private readonly XDocument _document;
        private readonly XamlNamespaceRewriteOptions _options;
        private readonly Dictionary<string, Binding> _scope = new(StringComparer.Ordinal);
        private readonly HashSet<Binding> _ignorable = [];
        private readonly Dictionary<(string Prefix, string Uri, bool IsIgnorable), XamlPrefixDisposition> _verdicts = [];
        private readonly List<string> _diagnostics = [];
        private readonly Func<string, string?> _resolveInScope;
        private int _droppedElements;
        private int _droppedAttributes;
        private int _normalizedMembers;
        private int _repointedDeclarations;

        public Session(XDocument document, XamlNamespaceRewriteOptions options)
        {
            _document = document;
            _options = options;
            _resolveInScope = prefix => _scope.TryGetValue(prefix, out Binding? binding) ? binding.Uri.Original : null;
        }

        public XamlNamespaceRewriteResult Run(XElement root)
        {
            if (!HasPolicyNamespaces(root))
            {
                // A classification that failed is still worth reporting, even though nothing was rewritten.
                return _diagnostics.Count == 0
                    ? XamlNamespaceRewriteResult.Unchanged
                    : new XamlNamespaceRewriteResult(0, 0, 0, 0, _diagnostics);
            }

            EnsureSourcePrefixes(root);

            // Walk without recursion: the nesting depth of a document must not be able to exhaust the stack.
            var frames = new Stack<Frame>();
            Frame? rootFrame = Open(root, isRoot: true);
            if (rootFrame is not null)
            {
                frames.Push(rootFrame);
            }

            while (frames.Count != 0)
            {
                Frame frame = frames.Peek();
                if (frame.NextChild < frame.Children.Count)
                {
                    Frame? childFrame = Open(frame.Children[frame.NextChild++], isRoot: false);
                    if (childFrame is not null)
                    {
                        frames.Push(childFrame);
                    }
                }
                else
                {
                    frames.Pop();
                    RestoreScope(frame.Shadowed, frame.Ignorable);
                }
            }

            return new XamlNamespaceRewriteResult(
                _droppedElements,
                _droppedAttributes,
                _normalizedMembers,
                _repointedDeclarations,
                _diagnostics);
        }

        /// <summary>
        /// Returns whether any declared prefix is classified as something other than <see cref="XamlPrefixDisposition.Leave"/>.
        /// Ordinary documents stop here, before any source prefix is read.
        /// </summary>
        private bool HasPolicyNamespaces(XElement root)
        {
            List<XAttribute>? declarations = null;
            HashSet<string>? ignorablePrefixes = null;
            foreach (XElement element in root.DescendantsAndSelf())
            {
                for (XAttribute? attribute = element.FirstAttribute; attribute is not null; attribute = attribute.NextAttribute)
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        (declarations ??= []).Add(attribute);
                    }
                    else if (attribute.Name == _IgnorableName)
                    {
                        AddPrefixes(ignorablePrefixes ??= new HashSet<string>(StringComparer.Ordinal), attribute.Value);
                    }
                }
            }

            if (declarations is null)
            {
                return false;
            }

            // A custom condition names its type through another prefix; before the walk the
            // only scope there is to resolve it in is the whole document.
            List<XAttribute> allDeclarations = declarations;
            string? ResolveInDocument(string prefix)
            {
                foreach (XAttribute candidate in allDeclarations)
                {
                    if (GetDeclaredPrefix(candidate) == prefix)
                    {
                        return candidate.Value;
                    }
                }

                return null;
            }

            foreach (XAttribute declaration in allDeclarations)
            {
                string prefix = GetDeclaredPrefix(declaration);
                XamlNamespaceUri uri = XamlNamespaceUri.Parse(declaration.Value);
                bool isListed = ignorablePrefixes is not null && ignorablePrefixes.Contains(prefix);
                if (Classify(prefix, uri, isListed, ResolveInDocument) != XamlPrefixDisposition.Leave)
                {
                    return true;
                }

                // mc:Ignorable is scoped to the element that carries it, so a listed prefix
                // can also be used where it is not ignorable.
                if (isListed && Classify(prefix, uri, isIgnorable: false, ResolveInDocument) != XamlPrefixDisposition.Leave)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureSourcePrefixes(XElement root)
        {
            if (root.Annotation<SourcePrefix>() is not null)
            {
                return;
            }

            string? source = _options.SourceXaml;
            if (source is null || !CaptureSourcePrefixes(_document, source))
            {
                Report("The prefixes the members were written with are unknown; members under prefixes that share a namespace are left as written.");
            }
        }

        /// <summary>
        /// Processes one element and returns a frame when its children still have to be visited.
        /// </summary>
        private Frame? Open(XElement element, bool isRoot)
        {
            List<KeyValuePair<string, Binding?>>? shadowed = null;
            List<Binding>? declared = null;
            XAttribute? ignorableAttribute = null;
            for (XAttribute? attribute = element.FirstAttribute; attribute is not null; attribute = attribute.NextAttribute)
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    string prefix = GetDeclaredPrefix(attribute);
                    _scope.TryGetValue(prefix, out Binding? previous);
                    (shadowed ??= []).Add(new KeyValuePair<string, Binding?>(prefix, previous));

                    var binding = new Binding(prefix, attribute);
                    _scope[prefix] = binding;
                    (declared ??= []).Add(binding);
                }
                else if (attribute.Name == _IgnorableName)
                {
                    ignorableAttribute = attribute;
                }
            }

            // The prefixes of mc:Ignorable are resolved where the attribute is, own declarations included.
            List<Binding>? ignorable = null;
            if (ignorableAttribute is not null)
            {
                foreach (string prefix in ignorableAttribute.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (_scope.TryGetValue(prefix, out Binding? binding) && _ignorable.Add(binding))
                    {
                        (ignorable ??= []).Add(binding);
                    }
                }
            }

            string? elementPrefix = GetElementPrefix(element);
            XamlPrefixDisposition disposition = ClassifyMember(elementPrefix, element.Name.NamespaceName, out Binding? elementBinding);
            if (disposition == XamlPrefixDisposition.Drop)
            {
                if (!isRoot)
                {
                    Report($"Dropped element {Describe(element, elementPrefix)}.");
                    element.Remove();
                    _droppedElements++;
                    RestoreScope(shadowed, ignorable);
                    return null;
                }

                Report($"The root element {Describe(element, elementPrefix)} is under a dropped prefix; it is kept because a document needs its root.");
            }

            // A root that stays under a prefix whose declaration is re-pointed moves to the bare namespace with it.
            if (elementBinding is not null && (disposition == XamlPrefixDisposition.Keep || (isRoot && IsRepointed(elementBinding))))
            {
                XName normalized = XNamespace.Get(elementBinding.NormalizedNamespace) + element.Name.LocalName;
                if (normalized != element.Name)
                {
                    element.Name = normalized;
                    _normalizedMembers++;
                }
            }

            RewriteAttributes(element, elementPrefix);

            if (declared is not null)
            {
                foreach (Binding binding in declared)
                {
                    Repoint(binding);
                }
            }

            List<XElement>? children = null;
            foreach (XElement child in element.Elements())
            {
                (children ??= []).Add(child);
            }

            if (children is null)
            {
                RestoreScope(shadowed, ignorable);
                return null;
            }

            return new Frame(children, shadowed, ignorable);
        }

        private void RestoreScope(List<KeyValuePair<string, Binding?>>? shadowed, List<Binding>? ignorable)
        {
            if (ignorable is not null)
            {
                foreach (Binding binding in ignorable)
                {
                    _ignorable.Remove(binding);
                }
            }

            if (shadowed is null)
            {
                return;
            }

            // Undo in reverse so a prefix declared twice on one element ends up with its outer binding.
            for (int index = shadowed.Count - 1; index >= 0; index--)
            {
                KeyValuePair<string, Binding?> entry = shadowed[index];
                if (entry.Value is null)
                {
                    _scope.Remove(entry.Key);
                }
                else
                {
                    _scope[entry.Key] = entry.Value;
                }
            }
        }

        private void RewriteAttributes(XElement element, string? elementPrefix)
        {
            List<XAttribute>? rewritten = null;
            for (XAttribute? attribute = element.FirstAttribute; attribute is not null; attribute = attribute.NextAttribute)
            {
                XAttribute? replacement = RewriteAttribute(element, elementPrefix, attribute);
                if (rewritten is null)
                {
                    if (ReferenceEquals(replacement, attribute))
                    {
                        continue;
                    }

                    // The first change: carry over the attributes before it, in their order.
                    rewritten = [];
                    for (XAttribute? previous = element.FirstAttribute; previous is not null && !ReferenceEquals(previous, attribute); previous = previous.NextAttribute)
                    {
                        rewritten.Add(previous);
                    }
                }

                if (replacement is null)
                {
                    continue;
                }

                for (int index = 0; index < rewritten.Count; index++)
                {
                    if (rewritten[index].Name == replacement.Name)
                    {
                        // Two attributes now set the same member. The XAML reader applies
                        // attributes in document order, so the later one wins.
                        Warn($"Attribute {replacement.Name.LocalName} of {Describe(element, elementPrefix)} is set twice; the later value is used.");
                        rewritten.RemoveAt(index);
                        _droppedAttributes++;
                        break;
                    }
                }

                rewritten.Add(replacement);
            }

            if (rewritten is not null)
            {
                element.ReplaceAttributes(rewritten);
            }
        }

        /// <summary>
        /// Returns the attribute itself when it stays, a renamed attribute, or <see langword="null"/> when it is dropped.
        /// </summary>
        private XAttribute? RewriteAttribute(XElement element, string? elementPrefix, XAttribute attribute)
        {
            if (attribute.IsNamespaceDeclaration)
            {
                return attribute;
            }

            // An attribute without a prefix belongs to its element and shares its fate.
            string? prefix = GetAttributePrefix(attribute);
            if (prefix is null || prefix.Length == 0)
            {
                return attribute;
            }

            XamlPrefixDisposition disposition = ClassifyMember(prefix, attribute.Name.NamespaceName, out Binding? binding);
            if (disposition == XamlPrefixDisposition.Drop)
            {
                Report($"Dropped attribute {prefix}:{attribute.Name.LocalName} of {Describe(element, elementPrefix)}.");
                _droppedAttributes++;
                return null;
            }

            if (disposition != XamlPrefixDisposition.Keep || binding is null)
            {
                return attribute;
            }

            XName normalized = NormalizeAttributeName(attribute.Name.LocalName, binding);
            if (ShouldDropNormalizedAttribute(element, normalized, attribute.Value))
            {
                Report($"Dropped attribute {prefix}:{attribute.Name.LocalName} of {Describe(element, elementPrefix)} after normalizing it.");
                _droppedAttributes++;
                return null;
            }

            if (normalized == attribute.Name)
            {
                return attribute;
            }

            _normalizedMembers++;
            return new XAttribute(normalized, attribute.Value);
        }

        private XName NormalizeAttributeName(string localName, Binding binding)
        {
            string target = binding.NormalizedNamespace;

            // A directive such as x:Name is only a directive in the XAML language namespace.
            if (target == _XamlLanguageNamespace)
            {
                return XNamespace.Get(target) + localName;
            }

            // A plain property belongs to its element. An attached property is looked up by its owner type:
            // without a prefix that is the default namespace, so the prefix can only go when both agree.
            // Where no default namespace is declared at all the prefix stays in any case: the XAML reader
            // ends the process on an attribute without a prefix there.
            if (_scope.ContainsKey(string.Empty) && (localName.IndexOf('.') < 0 || target == GetDefaultNamespace()))
            {
                return XName.Get(localName);
            }

            return XNamespace.Get(target) + localName;
        }

        private string GetDefaultNamespace()
        {
            if (!_scope.TryGetValue(string.Empty, out Binding? binding))
            {
                return string.Empty;
            }

            return IsRepointed(binding) ? binding.NormalizedNamespace : binding.Uri.Original;
        }

        /// <summary>
        /// Returns whether the declaration of a prefix ends up bound to its bare namespace.
        /// </summary>
        /// <remarks>
        /// That is the case for a kept prefix, and for a dropped one whose namespace carries a condition.
        /// The XAML reader interprets the condition of every declaration it sees, whether the prefix is used
        /// or not, and ends the process on some conditions it cannot interpret. A condition that was decided
        /// here therefore never reaches the reader.
        /// </remarks>
        private bool IsRepointed(Binding binding)
        {
            XamlPrefixDisposition disposition = Classify(binding);
            return disposition == XamlPrefixDisposition.Keep
                || (disposition == XamlPrefixDisposition.Drop && binding.Uri.IsConditional);
        }

        /// <summary>
        /// Points a declaration at its bare namespace instead of removing it: the prefix can still be
        /// referenced from attribute values such as <c>TargetType="c:Button"</c>.
        /// </summary>
        private void Repoint(Binding binding)
        {
            if (!IsRepointed(binding))
            {
                return;
            }

            string target = binding.NormalizedNamespace;
            if (string.Equals(binding.Declaration.Value, target, StringComparison.Ordinal))
            {
                return;
            }

            binding.Declaration.Value = target;
            _repointedDeclarations++;
        }

        private string? GetElementPrefix(XElement element)
        {
            SourcePrefix? sourcePrefix = element.Annotation<SourcePrefix>();
            return sourcePrefix is not null ? sourcePrefix.Value : FindPrefix(element.Name.NamespaceName, isAttribute: false);
        }

        private string? GetAttributePrefix(XAttribute attribute)
        {
            // An attribute name without a namespace was written without a prefix.
            if (attribute.Name.NamespaceName.Length == 0)
            {
                return string.Empty;
            }

            SourcePrefix? sourcePrefix = attribute.Annotation<SourcePrefix>();
            return sourcePrefix is not null ? sourcePrefix.Value : FindPrefix(attribute.Name.NamespaceName, isAttribute: true);
        }

        /// <summary>
        /// Finds the only prefix in scope that is bound to <paramref name="namespaceName"/>. Used when the source
        /// prefix is unknown; several candidates cannot be told apart and yield <see langword="null"/>.
        /// </summary>
        private string? FindPrefix(string namespaceName, bool isAttribute)
        {
            string? found = null;
            foreach (Binding binding in _scope.Values)
            {
                // The default namespace does not apply to attributes.
                if (isAttribute && binding.Prefix.Length == 0)
                {
                    continue;
                }

                if (!string.Equals(binding.Uri.Original, namespaceName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }

                found = binding.Prefix;
            }

            return found;
        }

        private XamlPrefixDisposition ClassifyMember(string? prefix, string namespaceName, out Binding? binding)
        {
            binding = null;
            if (prefix is null || !_scope.TryGetValue(prefix, out Binding? candidate))
            {
                return XamlPrefixDisposition.Leave;
            }

            // The prefix has to be bound to the namespace the member is in. Anything else means
            // the recorded prefixes do not belong to this document, so the member is left alone.
            if (!string.Equals(candidate.Uri.Original, namespaceName, StringComparison.Ordinal))
            {
                return XamlPrefixDisposition.Leave;
            }

            binding = candidate;
            return Classify(candidate);
        }

        private XamlPrefixDisposition Classify(Binding binding)
        {
            return Classify(binding.Prefix, binding.Uri, _ignorable.Contains(binding), _resolveInScope);
        }

        private XamlPrefixDisposition Classify(string prefix, XamlNamespaceUri uri, bool isIgnorable, Func<string, string?> resolveNamespace)
        {
            var key = (prefix, uri.Original, isIgnorable);
            if (_verdicts.TryGetValue(key, out XamlPrefixDisposition verdict))
            {
                return verdict;
            }

            try
            {
                XamlPrefixDisposition? overridden = _options.ClassifyOverride?.Invoke(prefix, uri, isIgnorable);
                if (overridden.HasValue)
                {
                    verdict = overridden.Value;
                }
                else
                {
                    Func<string, bool?>? evaluate = null;
                    XamlConditionEvaluator? evaluator = _options.EvaluateCondition;
                    if (evaluator is not null && uri.IsConditional)
                    {
                        evaluate = condition => evaluator(condition, resolveNamespace);
                    }

                    verdict = XamlNamespacePolicies.Classify(prefix, uri, isIgnorable, _options.TargetPlatform, evaluate);
                }
            }
            catch (Exception exception)
            {
                // A policy that cannot decide must not take the document down with it.
                Report($"Classifying prefix '{prefix}' failed; its members are left as written. {exception.GetType().Name}: {exception.Message}");
                verdict = XamlPrefixDisposition.Leave;
            }

            _verdicts[key] = verdict;
            return verdict;
        }

        private bool ShouldDropNormalizedAttribute(XElement element, XName name, string value)
        {
            Func<XElement, XName, string, bool>? filter = _options.ShouldDropNormalizedAttribute;
            if (filter is null)
            {
                return false;
            }

            try
            {
                return filter(element, name, value);
            }
            catch (Exception exception)
            {
                Report($"Filtering attribute {name.LocalName} failed; it is kept. {exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        private void Report(string message)
        {
            _diagnostics.Add(message);
            try
            {
                _options.Report?.Invoke(message);
            }
            catch (Exception)
            {
                // A diagnostics receiver must not interfere with the rewrite.
            }
        }

        /// <summary>
        /// Reports markup that is most likely a mistake of its author.
        /// </summary>
        private void Warn(string message)
        {
            _diagnostics.Add(message);
            try
            {
                (_options.ReportWarning ?? _options.Report)?.Invoke(message);
            }
            catch (Exception)
            {
                // A diagnostics receiver must not interfere with the rewrite.
            }
        }

        private static string Describe(XElement element, string? prefix)
        {
            string name = string.IsNullOrEmpty(prefix) ? element.Name.LocalName : prefix + ":" + element.Name.LocalName;
            IXmlLineInfo lineInfo = element;
            return lineInfo.HasLineInfo()
                ? $"<{name}> (line {lineInfo.LineNumber.ToString(CultureInfo.InvariantCulture)})"
                : $"<{name}>";
        }
    }
}
