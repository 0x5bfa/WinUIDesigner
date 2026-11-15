// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// When a dictionary read from Visual Studio's prepared copy may be used again: while its copy and the copies
/// merged into it keep the text they had.
/// </summary>
[TestClass]
public sealed class PreparedResourceDictionaryCacheTests
{
    private const string _Outer = @"C:\Cache\Themes\~vs]~1~Outer.xaml";
    private const string _Inner = @"C:\Cache\Themes\~vs]~2~Inner.xaml";
    private const string _Missing = @"C:\Cache\Themes\~vs]~3~Missing.xaml";

    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase)
    {
        [_Outer] = "outer",
        [_Inner] = "inner",
    };

    [TestMethod]
    public void UnchangedCopyIsServedFromTheCache()
    {
        var cache = new PreparedResourceDictionaryCache();
        XElement template = new("ResourceDictionary");
        cache.Store(_Outer, "outer", template, [new(_Inner, "inner")]);

        Assert.IsTrue(cache.TryGet(_Outer, "outer", ReadFile, out PreparedResourceDictionary? cached));
        Assert.AreSame(template, cached.Template);
        CollectionAssert.AreEqual(new[] { new KeyValuePair<string, string?>(_Inner, "inner") }, cached.Dependencies.ToArray());
    }

    [TestMethod]
    public void PathIsComparedWithoutCase()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), []);

        Assert.IsTrue(cache.TryGet(_Outer.ToUpperInvariant(), "outer", ReadFile, out _));
    }

    [TestMethod]
    public void ChangedCopyIsReadAgain()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), []);

        Assert.IsFalse(cache.TryGet(_Outer, "outer, edited", ReadFile, out _));

        // The stale entry is gone, so even the old text does not bring it back.
        Assert.IsFalse(cache.TryGet(_Outer, "outer", ReadFile, out _));
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public void ChangedMergedCopyInvalidatesTheDictionaryThatMergesIt()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), [new(_Inner, "inner")]);
        _files[_Inner] = "inner, edited";

        Assert.IsFalse(cache.TryGet(_Outer, "outer", ReadFile, out _));
    }

    [TestMethod]
    public void MergedCopyThatDisappearsInvalidatesTheDictionary()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), [new(_Inner, "inner")]);
        _files.Remove(_Inner);

        Assert.IsFalse(cache.TryGet(_Outer, "outer", ReadFile, out _));
    }

    [TestMethod]
    public void MergedCopyThatWasMissingAndAppearsInvalidatesTheDictionary()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), [new(_Missing, null)]);

        Assert.IsTrue(cache.TryGet(_Outer, "outer", ReadFile, out _));

        _files[_Missing] = "written later";
        Assert.IsFalse(cache.TryGet(_Outer, "outer", ReadFile, out _));
    }

    [TestMethod]
    public void MergedCopyThatCannotBeReadInvalidatesTheDictionary()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), [new(_Inner, "inner")]);

        Assert.IsFalse(cache.TryGet(_Outer, "outer", path => path == _Inner ? throw new IOException("locked") : ReadFile(path), out _));
    }

    [TestMethod]
    public void DependenciesAreKeptOncePerPathAndWithoutTheDictionaryItself()
    {
        var cache = new PreparedResourceDictionaryCache();

        PreparedResourceDictionary stored = cache.Store(
            _Outer,
            "outer",
            new XElement("ResourceDictionary"),
            [new(_Inner, "inner"), new(_Inner.ToUpperInvariant(), "inner"), new(_Outer, "outer")]);

        CollectionAssert.AreEqual(new[] { new KeyValuePair<string, string?>(_Inner, "inner") }, stored.Dependencies.ToArray());
    }

    [TestMethod]
    public void FullCacheStartsAfresh()
    {
        var cache = new PreparedResourceDictionaryCache(capacity: 2);
        cache.Store(@"C:\Cache\a.xaml", "a", new XElement("ResourceDictionary"), []);
        cache.Store(@"C:\Cache\b.xaml", "b", new XElement("ResourceDictionary"), []);

        // Storing a known path again does not count as a new entry.
        cache.Store(@"C:\Cache\b.xaml", "b2", new XElement("ResourceDictionary"), []);
        Assert.AreEqual(2, cache.Count);

        cache.Store(@"C:\Cache\c.xaml", "c", new XElement("ResourceDictionary"), []);
        Assert.AreEqual(1, cache.Count);
        Assert.IsTrue(cache.TryGet(@"C:\Cache\c.xaml", "c", ReadFile, out _));
    }

    [TestMethod]
    public void ClearForgetsEverything()
    {
        var cache = new PreparedResourceDictionaryCache();
        cache.Store(_Outer, "outer", new XElement("ResourceDictionary"), []);

        cache.Clear();

        Assert.AreEqual(0, cache.Count);
        Assert.IsFalse(cache.TryGet(_Outer, "outer", ReadFile, out _));
    }

    private string? ReadFile(string path)
    {
        return _files.TryGetValue(path, out string? text) ? text : null;
    }
}
