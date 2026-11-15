// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Keeps the resource dictionaries that were read from Visual Studio's prepared copies, with the namespace
/// policies applied and the dictionaries they merge expanded, for as long as the copies do not change.
/// </summary>
/// <remarks>
/// <para>
/// A dictionary that several documents merge, or that one document merges in several places, is then read and
/// rewritten once. Each use still gets an element of its own, because WinUI lets a dictionary object belong to
/// one parent only.
/// </para>
/// <para>
/// An entry is valid while the text of its prepared copy, and of every prepared copy merged into it, is the text
/// it was made from, and a merged copy that was missing is still missing. The text is compared rather than the
/// time of the last write: Visual Studio writes a copy again when it prepares the document again, and two writes
/// can carry the same time stamp. Reading a file is cheap next to parsing it and applying the namespace policies.
/// </para>
/// <para>The cache is not thread-safe; the surface uses it on its UI thread.</para>
/// </remarks>
/// <param name="capacity">The number of dictionaries kept; when it is reached, the cache starts afresh.</param>
internal sealed class PreparedResourceDictionaryCache(int capacity = PreparedResourceDictionaryCache.DefaultCapacity)
{
    /// <summary>
    /// The number of dictionaries kept by default, far more than an application merges.
    /// </summary>
    public const int DefaultCapacity = 256;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the number of dictionaries kept.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns the dictionary kept for <paramref name="path"/> when it was made from <paramref name="text"/> and the
    /// dictionaries merged into it are unchanged. A stale entry is removed.
    /// </summary>
    /// <param name="path">The full path of the prepared copy.</param>
    /// <param name="text">The current text of the prepared copy.</param>
    /// <param name="readFile">Reads the current text of a merged dictionary; <see langword="null"/> when it is missing.</param>
    /// <param name="dictionary">The dictionary kept for the path.</param>
    public bool TryGet(string path, string text, Func<string, string?> readFile, [NotNullWhen(true)] out PreparedResourceDictionary? dictionary)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(readFile);

        dictionary = null;
        if (!_entries.TryGetValue(path, out Entry? entry))
        {
            return false;
        }

        if (!string.Equals(entry.Text, text, StringComparison.Ordinal) || !AreCurrent(entry.Dictionary.Dependencies, readFile))
        {
            _entries.Remove(path);
            return false;
        }

        dictionary = entry.Dictionary;
        return true;
    }

    /// <summary>
    /// Keeps <paramref name="template"/> as the dictionary read from <paramref name="text"/>.
    /// </summary>
    /// <param name="path">The full path of the prepared copy.</param>
    /// <param name="text">The text the dictionary was read from.</param>
    /// <param name="template">The dictionary. It must not change afterwards; users of the cache copy it.</param>
    /// <param name="dependencies">
    /// The prepared copies merged into the dictionary, at any depth, with their text, or <see langword="null"/> for
    /// a copy that was missing.
    /// </param>
    public PreparedResourceDictionary Store(string path, string text, XElement template, IEnumerable<KeyValuePair<string, string?>> dependencies)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(dependencies);

        // One path, one text: a dictionary merged twice is compared once.
        var unique = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> dependency in dependencies)
        {
            if (!string.Equals(dependency.Key, path, StringComparison.OrdinalIgnoreCase))
            {
                unique.TryAdd(dependency.Key, dependency.Value);
            }
        }

        if (_entries.Count >= capacity && !_entries.ContainsKey(path))
        {
            _entries.Clear();
        }

        var dictionary = new PreparedResourceDictionary(template, new List<KeyValuePair<string, string?>>(unique));
        _entries[path] = new Entry(text, dictionary);
        return dictionary;
    }

    /// <summary>
    /// Forgets every dictionary.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    private static bool AreCurrent(IReadOnlyList<KeyValuePair<string, string?>> dependencies, Func<string, string?> readFile)
    {
        foreach (KeyValuePair<string, string?> dependency in dependencies)
        {
            string? current;
            try
            {
                current = readFile(dependency.Key);
            }
            catch (Exception)
            {
                // A file that cannot be read now is not the file the entry was made from.
                return false;
            }

            if (!string.Equals(current, dependency.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record Entry(string Text, PreparedResourceDictionary Dictionary);
}

/// <summary>
/// A resource dictionary read from a prepared copy: the root element with the namespace policies applied and the
/// dictionaries it merges expanded, and the prepared copies it was made from besides its own.
/// </summary>
internal sealed class PreparedResourceDictionary(XElement template, IReadOnlyList<KeyValuePair<string, string?>> dependencies)
{
    /// <summary>
    /// Gets the root element of the dictionary. Callers copy it before they change or insert it.
    /// </summary>
    public XElement Template { get; } = template;

    /// <summary>
    /// Gets the full paths and texts of the prepared copies merged into the dictionary, at any depth;
    /// <see langword="null"/> stands for a copy that was missing.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Dependencies { get; } = dependencies;
}
