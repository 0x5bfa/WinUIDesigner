// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Gives each resource dictionary that Visual Studio prepared with a design-time source the content of the
/// dictionary that source names.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio rewrites <c>ResourceDictionary.Source</c> into the design-time attached property
/// <c>DT.ResourceDictionarySource</c> of <c>using:XSurfUwp</c> and writes a prepared copy of the referenced
/// dictionary into the designer's shadow cache (see <see cref="ResourceDictionarySourceUri"/>). Microsoft's UWP
/// surface hands that value to a native function of its own; the WinUI reader cannot open such a copy through
/// <c>Source</c>, does not know the attached property, and could not apply the namespace policies to the copy.
/// </para>
/// <para>
/// The expander therefore reads the copy itself, applies the namespace policies to it, expands the dictionaries it
/// merges in turn, and inserts the result as the last dictionary merged into the element that carried the source.
/// The last merged dictionary is looked up first, and the entries of a <c>Source</c> win over merged dictionaries
/// at run time, so the content keeps its precedence. Inserted into the document, the content is read in the same
/// pass as the document: a <c>StaticResource</c> later in the document finds it, and a dictionary sees the
/// dictionaries merged before it, as in App.xaml at run time.
/// </para>
/// <para>
/// A dictionary Visual Studio did not find in the solution, such as one compiled into a package, keeps its source;
/// when the WinUI runtime can load it in the surface, it is merged with that source, otherwise it is left out.
/// </para>
/// <para>
/// Every dictionary that cannot be merged is reported once and left empty; the document goes on loading. A chain
/// of dictionaries that merges one of its own members again is cut there.
/// </para>
/// </remarks>
/// <param name="options">The file system, the namespace policies and the receivers of messages.</param>
internal sealed class ResourceDictionarySourceExpander(ResourceDictionarySourceOptions options)
{
    /// <summary>
    /// The namespace of Visual Studio's design-time properties for the UWP and WinUI designers.
    /// </summary>
    public const string DesignTimeNamespace = "using:XSurfUwp";

    /// <summary>
    /// The attribute Visual Studio's cleaner writes in place of <c>ResourceDictionary.Source</c>.
    /// </summary>
    public static readonly XName SourceAttributeName = XName.Get("DT.ResourceDictionarySource", DesignTimeNamespace);

    private static readonly XNamespace _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace _DesignTime = DesignTimeNamespace;

    private readonly ResourceDictionarySourceOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly PreparedResourceDictionaryCache _cache = new();

    // Whether the WinUI runtime loads a source in the surface does not change while the surface runs.
    private readonly Dictionary<string, string?> _runtimeLoadErrors = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the dictionaries kept from earlier expansions.
    /// </summary>
    public PreparedResourceDictionaryCache Cache => _cache;

    /// <summary>
    /// Returns whether <paramref name="element"/> belongs to the content of a merged dictionary that an expansion
    /// inserted, rather than to the document it was inserted into.
    /// </summary>
    public static bool IsInserted(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        for (XElement? current = element; current is not null; current = current.Parent)
        {
            if (current.Annotation<InsertedDictionary>() is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces every design-time source at or below <paramref name="root"/> with the dictionary it names.
    /// </summary>
    /// <param name="root">The element to expand, changed in place.</param>
    /// <param name="documentUri">The design-time URI of the document, the base of a relative source.</param>
    /// <param name="owner">The document as messages name it, such as <c>document 7</c>.</param>
    /// <returns>How many dictionaries were merged and how many were left empty.</returns>
    public ResourceDictionaryExpansion Expand(XElement root, string? documentUri, string owner)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(owner);

        // The document is the first member of every chain: a dictionary that merges the document is cut there.
        var session = new Session();
        if (ResourceDictionarySourceUri.IsDesignTimeUri(documentUri)
            && ResourceDictionarySourceUri.TryGetLocalPath(documentUri!, _options.ContentDirectory) is { } documentPath)
        {
            session.Chain.Add(new ChainMember(documentPath, ResourceDictionarySourceUri.GetDisplayName(documentUri!)));
        }

        ExpandElement(root, documentUri, owner, session, []);
        return new ResourceDictionaryExpansion(session.Prepared, session.Runtime, session.Failed);
    }

    /// <summary>
    /// Returns the dictionary a design-time source names, ready to be read on its own; the construction-action
    /// path merges it into the dictionary that Visual Studio sets the source on.
    /// </summary>
    /// <param name="source">The value Visual Studio set.</param>
    /// <param name="documentUri">The design-time URI of the document, the base of a relative source.</param>
    /// <param name="owner">The document as messages name it.</param>
    public ResourceDictionarySourceContent Load(string? source, string? documentUri, string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        string? uri = ResourceDictionarySourceUri.Resolve(source, documentUri);
        if (uri is null)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                Warn($"The source '{source}' of a resource dictionary merged by {owner} is not a URI; the dictionary is left empty.");
            }

            return ResourceDictionarySourceContent.None;
        }

        if (!ResourceDictionarySourceUri.IsDesignTimeUri(uri))
        {
            return ResourceDictionarySourceContent.FromRuntime(uri);
        }

        PreparedResourceDictionary? prepared = LoadPrepared(uri, owner, new Session(), []);
        if (prepared is null)
        {
            return ResourceDictionarySourceContent.Failed;
        }

        // Read on its own, the dictionary stays the root of a document: mc:Ignorable keeps working there.
        var dictionary = new XElement(prepared.Template);
        RemoveDesignTimeAttributes(dictionary);
        XamlFragmentGrafter.RemoveClassDirectives(dictionary);
        return ResourceDictionarySourceContent.FromPrepared(uri, dictionary);
    }

    /// <summary>
    /// Reports that the WinUI runtime could not load a source that Visual Studio did not find in the solution.
    /// </summary>
    public void ReportRuntimeLoadFailure(string uri, string owner, string error)
    {
        Warn(DescribeRuntimeLoadFailure(uri, owner, error));
    }

    private static string DescribeRuntimeLoadFailure(string uri, string owner, string error)
    {
        return $"Resource dictionary '{uri}' merged by {owner} is left empty: Visual Studio did not find it in the solution, and the WinUI runtime could not load it in the designer: {error}";
    }

    private void ExpandElement(XElement root, string? documentUri, string owner, Session session, List<KeyValuePair<string, string?>> dependencies)
    {
        // Collected first: the content inserted below is expanded already and must not be visited again.
        List<XElement> targets = root.DescendantsAndSelf().Where(element => element.Attribute(SourceAttributeName) is not null).ToList();
        foreach (XElement target in targets)
        {
            XAttribute attribute = target.Attribute(SourceAttributeName)!;
            string value = attribute.Value;
            attribute.Remove();

            string? uri = ResourceDictionarySourceUri.Resolve(value, documentUri);
            if (uri is null)
            {
                // An empty source is an empty dictionary at run time as well.
                if (!string.IsNullOrWhiteSpace(value))
                {
                    session.Failed++;
                    Warn($"The source '{value}' of a resource dictionary merged by {owner} is not a URI; the dictionary is left empty.");
                }

                continue;
            }

            XElement fragment;
            if (ResourceDictionarySourceUri.IsDesignTimeUri(uri))
            {
                PreparedResourceDictionary? prepared = LoadPrepared(uri, owner, session, dependencies);
                if (prepared is null)
                {
                    session.Failed++;
                    continue;
                }

                fragment = new XElement(prepared.Template);
                RemoveDesignTimeAttributes(fragment);
                XamlFragmentGrafter.PrepareAsNestedElement(fragment);
                session.Prepared++;
            }
            else
            {
                string? error = GetRuntimeLoadError(uri);
                if (error is not null)
                {
                    session.Failed++;
                    Warn(DescribeRuntimeLoadFailure(uri, owner, error));
                    continue;
                }

                fragment = new XElement(_Presentation + "ResourceDictionary", new XAttribute("Source", uri));
                session.Runtime++;
            }

            fragment.AddAnnotation(InsertedDictionary.Instance);
            XamlFragmentGrafter.Append(GetOrCreateMergedDictionaries(target), fragment);
        }
    }

    /// <summary>
    /// Reads a prepared copy, applies the namespace policies to it and expands the dictionaries it merges.
    /// </summary>
    /// <param name="uri">The design-time URI of the copy.</param>
    /// <param name="owner">The dictionary or document that merges it, as messages name it.</param>
    /// <param name="session">The expansion this is part of.</param>
    /// <param name="dependencies">
    /// Receives every prepared copy this read depends on, with the text it had, so that a dictionary that merges
    /// this one is not kept longer than its parts.
    /// </param>
    /// <returns>The dictionary, or <see langword="null"/> when it cannot be merged; that has been reported.</returns>
    private PreparedResourceDictionary? LoadPrepared(string uri, string owner, Session session, List<KeyValuePair<string, string?>> dependencies)
    {
        string name = ResourceDictionarySourceUri.GetDisplayName(uri);
        string? path = ResourceDictionarySourceUri.TryGetLocalPath(uri, _options.ContentDirectory);
        if (path is null)
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: '{uri}' does not name a file the designer can read.");
            return null;
        }

        int cycleStart = session.Chain.FindIndex(member => string.Equals(member.Path, path, StringComparison.OrdinalIgnoreCase));
        if (cycleStart >= 0)
        {
            session.IsCut = true;
            IEnumerable<string> cycle = session.Chain.Skip(cycleStart).Select(member => member.Name).Append(name);
            Warn($"Resource dictionary '{name}' merged by {owner} is not merged again: it would merge itself ({string.Join(" → ", cycle)}).");
            return null;
        }

        if (session.Depth >= _options.MaxDepth)
        {
            session.IsCut = true;
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: dictionaries are merged more than {_options.MaxDepth} levels deep.");
            return null;
        }

        string? text;
        try
        {
            text = _options.ReadFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: Visual Studio's prepared copy '{path}' cannot be read: {ex.Message}");
            dependencies.Add(new KeyValuePair<string, string?>(path, null));
            return null;
        }

        // Whatever comes of it, a dictionary that merges this one is only as current as this text.
        dependencies.Add(new KeyValuePair<string, string?>(path, text));
        if (text is null)
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: Visual Studio's prepared copy '{path}' does not exist.");
            return null;
        }

        if (_cache.TryGet(path, text, _options.ReadFile, out PreparedResourceDictionary? cached))
        {
            dependencies.AddRange(cached.Dependencies);
            return cached;
        }

        XElement? root;
        try
        {
            root = _options.Parse(text, $"resource dictionary '{name}'").Root;
        }
        catch (Exception ex)
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: its prepared copy '{path}' is not well-formed XAML: {ex.Message}");
            return null;
        }

        if (root is null)
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: its prepared copy '{path}' has no root element.");
            return null;
        }

        if (!IsDictionaryRoot(root.Name))
        {
            Warn($"Resource dictionary '{name}' merged by {owner} is left empty: its root element <{root.Name.LocalName}> is not a resource dictionary.");
            return null;
        }

        var nested = new List<KeyValuePair<string, string?>>();
        bool isOuterCut = session.IsCut;
        session.IsCut = false;
        session.Chain.Add(new ChainMember(path, name));
        session.Depth++;
        try
        {
            ExpandElement(root, uri, $"'{name}'", session, nested);
        }
        finally
        {
            session.Depth--;
            session.Chain.RemoveAt(session.Chain.Count - 1);
        }

        // What a cut chain leaves out depends on where the expansion started, so such a dictionary is not kept.
        bool isCut = session.IsCut;
        session.IsCut = isOuterCut || isCut;
        PreparedResourceDictionary prepared = isCut
            ? new PreparedResourceDictionary(root, nested)
            : _cache.Store(path, text, root, nested);

        dependencies.AddRange(prepared.Dependencies);
        return prepared;
    }

    private string? GetRuntimeLoadError(string uri)
    {
        if (_runtimeLoadErrors.TryGetValue(uri, out string? error))
        {
            return error;
        }

        if (_options.LoadWithRuntime is not { } load)
        {
            error = "the designer cannot load dictionaries by URI.";
        }
        else
        {
            try
            {
                error = load(uri);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        _runtimeLoadErrors[uri] = error;
        return error;
    }

    private bool IsDictionaryRoot(XName name)
    {
        if (name.LocalName is "ResourceDictionary" or "XamlControlsResources" or "ColorPaletteResources")
        {
            return true;
        }

        try
        {
            return _options.IsDictionaryType?.Invoke(name) == true;
        }
        catch (Exception ex)
        {
            Report($"Resolving the type <{name}> failed: {ex.Message}");
            return false;
        }
    }

    private static XElement GetOrCreateMergedDictionaries(XElement dictionary)
    {
        foreach (XElement child in dictionary.Elements())
        {
            if (child.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
            {
                return child;
            }
        }

        // Before the entries: the entries of a dictionary are its content, and content must not be interrupted.
        var property = new XElement(_Presentation + "ResourceDictionary.MergedDictionaries");
        dictionary.AddFirst(property);
        return property;
    }

    /// <summary>
    /// Removes the design-time attributes from the root of a prepared copy. Visual Studio adds them for the root of
    /// a document; on a merged dictionary they mean nothing, and the reader may not know them.
    /// </summary>
    private static void RemoveDesignTimeAttributes(XElement root)
    {
        foreach (XAttribute attribute in root.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace == _DesignTime).ToList())
        {
            attribute.Remove();
        }
    }

    private void Warn(string message)
    {
        _options.ReportWarning?.Invoke(message);
    }

    private void Report(string message)
    {
        _options.Report?.Invoke(message);
    }

    /// <summary>
    /// Marks the root of a dictionary an expansion inserted.
    /// </summary>
    private sealed class InsertedDictionary
    {
        public static readonly InsertedDictionary Instance = new();
    }

    private readonly record struct ChainMember(string Path, string Name);

    /// <summary>
    /// One expansion: the chain of documents and dictionaries being read, outermost first, and what happened.
    /// </summary>
    private sealed class Session
    {
        public List<ChainMember> Chain { get; } = [];

        /// <summary>
        /// Gets or sets the number of dictionaries being read; the document at the start of the chain does not count.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Gets or sets whether a dictionary was left out because the chain reached it again or grew too deep.
        /// </summary>
        public bool IsCut { get; set; }

        public int Prepared { get; set; }

        public int Runtime { get; set; }

        public int Failed { get; set; }
    }
}

/// <summary>
/// Configures a <see cref="ResourceDictionarySourceExpander"/>.
/// </summary>
internal sealed class ResourceDictionarySourceOptions
{
    /// <summary>
    /// The number of dictionaries a chain may nest by default.
    /// </summary>
    public const int DefaultMaxDepth = 32;

    /// <summary>
    /// Gets or sets the folder <c>ms-appx:///</c> stands for: Visual Studio's shadow cache, beside the surface.
    /// </summary>
    public string ContentDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how a file is read. Returns <see langword="null"/> when the file does not exist.
    /// </summary>
    public Func<string, string?> ReadFile { get; set; } = path => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>
    /// Gets or sets how prepared XAML is parsed: it receives the text and a description for messages and returns
    /// the document with the namespace policies applied. It throws when the text is not well-formed.
    /// </summary>
    public Func<string, string, XDocument> Parse { get; set; } = (xaml, _) => XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);

    /// <summary>
    /// Gets or sets whether an element name other than the dictionary types of WinUI names a resource dictionary
    /// type, such as a class of the project that derives from <c>ResourceDictionary</c>.
    /// </summary>
    public Func<XName, bool>? IsDictionaryType { get; set; }

    /// <summary>
    /// Gets or sets how the WinUI runtime is asked to load a dictionary by its URI. It returns
    /// <see langword="null"/> when the dictionary loads and the reason otherwise. Without it, no such source is merged.
    /// </summary>
    public Func<string, string?>? LoadWithRuntime { get; set; }

    /// <summary>
    /// Gets or sets the number of dictionaries a chain may nest; deeper ones are left empty.
    /// </summary>
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    /// <summary>
    /// Gets or sets the receiver of the messages about dictionaries that are left empty.
    /// </summary>
    public Action<string>? ReportWarning { get; set; }

    /// <summary>
    /// Gets or sets the receiver of diagnostic messages.
    /// </summary>
    public Action<string>? Report { get; set; }
}

/// <summary>
/// The outcome of one expansion.
/// </summary>
/// <param name="Prepared">The dictionaries merged from Visual Studio's prepared copies.</param>
/// <param name="Runtime">The dictionaries merged with their source, for the WinUI runtime to load.</param>
/// <param name="Failed">The dictionaries left empty.</param>
internal readonly record struct ResourceDictionaryExpansion(int Prepared, int Runtime, int Failed)
{
    /// <summary>
    /// Gets the number of sources the expansion met.
    /// </summary>
    public int Total => Prepared + Runtime + Failed;
}

/// <summary>
/// What the construction-action path merges into a dictionary whose source Visual Studio sets.
/// </summary>
internal sealed class ResourceDictionarySourceContent
{
    private ResourceDictionarySourceContent(ResourceDictionarySourceContentKind kind, string? uri, XElement? dictionary)
    {
        Kind = kind;
        Uri = uri;
        Dictionary = dictionary;
    }

    /// <summary>
    /// Gets the content of an empty source: nothing to merge.
    /// </summary>
    public static ResourceDictionarySourceContent None { get; } = new(ResourceDictionarySourceContentKind.None, null, null);

    /// <summary>
    /// Gets the content of a source that cannot be merged; the reason has been reported.
    /// </summary>
    public static ResourceDictionarySourceContent Failed { get; } = new(ResourceDictionarySourceContentKind.Failed, null, null);

    /// <summary>
    /// Gets what kind of content it is.
    /// </summary>
    public ResourceDictionarySourceContentKind Kind { get; }

    /// <summary>
    /// Gets the absolute URI of the source.
    /// </summary>
    public string? Uri { get; }

    /// <summary>
    /// Gets the dictionary read from Visual Studio's prepared copy, a root element of its own.
    /// </summary>
    public XElement? Dictionary { get; }

    /// <summary>
    /// Creates the content of a source Visual Studio prepared a copy of.
    /// </summary>
    public static ResourceDictionarySourceContent FromPrepared(string uri, XElement dictionary)
    {
        return new(ResourceDictionarySourceContentKind.Prepared, uri, dictionary);
    }

    /// <summary>
    /// Creates the content of a source for the WinUI runtime to load.
    /// </summary>
    public static ResourceDictionarySourceContent FromRuntime(string uri)
    {
        return new(ResourceDictionarySourceContentKind.Runtime, uri, null);
    }
}

/// <summary>
/// The kinds of <see cref="ResourceDictionarySourceContent"/>.
/// </summary>
internal enum ResourceDictionarySourceContentKind
{
    /// <summary>The source is empty.</summary>
    None,

    /// <summary>The source cannot be merged.</summary>
    Failed,

    /// <summary>The dictionary was read from Visual Studio's prepared copy.</summary>
    Prepared,

    /// <summary>The WinUI runtime loads the dictionary from its URI.</summary>
    Runtime,
}
