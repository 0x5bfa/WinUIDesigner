// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class StyleEditTests
{
    private sealed record Insertion(List<string> Collection, int Index, string Item);

    [TestMethod]
    public void PendingInsertionsAreTakenPerCollectionInTheirOriginalOrder()
    {
        var first = new List<string>();
        var second = new List<string>();
        var queue = new DeferredInsertionQueue<Insertion>(insertion => insertion.Collection);
        queue.Add(new(first, 0, "a"));
        queue.Add(new(second, 0, "x"));
        queue.Add(new(first, 1, "b"));

        CollectionAssert.AreEqual(new[] { "a", "b" }, queue.TakeFor(first).Select(insertion => insertion.Item).ToArray());
        Assert.AreEqual(1, queue.Count);
        Assert.AreEqual(0, queue.TakeFor(first).Count);
        CollectionAssert.AreEqual(new[] { "x" }, queue.TakeAll().Select(insertion => insertion.Item).ToArray());
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void ReplacingTwoSettersInOneBatchKeepsTheIndicesVisualStudioComputed()
    {
        // Visual Studio replaces each changed setter by a new one: remove at i, add at i. Every index assumes
        // the earlier actions of the batch already ran.
        var setters = new List<string> { "FontSize=20", "Padding=7" };
        var queue = new DeferredInsertionQueue<Insertion>(insertion => insertion.Collection);

        void Remove(int index)
        {
            InsertPending(queue.TakeFor(setters));
            setters.RemoveAt(index);
        }

        Remove(0);
        queue.Add(new(setters, 0, "FontSize=24"));
        Remove(1);
        queue.Add(new(setters, 1, "Padding=9"));
        InsertPending(queue.TakeAll());

        CollectionAssert.AreEqual(new[] { "FontSize=24", "Padding=9" }, setters);
    }

    [TestMethod]
    public void RemovingAndAddingAStyleUnderOneKeyIsAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object oldStyle = new();
        object newStyle = new();

        tracker.Removed(dictionary, "ProbeStyle", oldStyle);
        tracker.Added(dictionary, "ProbeStyle", newStyle);

        var replacement = tracker.TakeReplacements().Single();
        Assert.AreSame(dictionary, replacement.Dictionary);
        Assert.AreSame(oldStyle, replacement.OldValue);
        Assert.AreSame(newStyle, replacement.NewValue);
        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    public void ImplicitStyleKeysPairByType()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object newStyle = new();

        tracker.Removed(dictionary, typeof(Uri), new object());
        tracker.Added(dictionary, typeof(string), newStyle);
        Assert.AreEqual(0, tracker.TakeReplacements().Count, "Another type is another key.");

        tracker.Removed(dictionary, typeof(Uri), new object());
        tracker.Added(dictionary, typeof(Uri), newStyle);
        Assert.AreSame(newStyle, tracker.TakeReplacements().Single().NewValue);
    }

    [TestMethod]
    public void AnAdditionWithoutARemovalIsNotAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();

        tracker.Removed(dictionary, "ProbeStyle", new object());
        tracker.Added(new object(), "ProbeStyle", new object());
        tracker.Added(dictionary, "OtherStyle", new object());

        Assert.AreEqual(0, tracker.TakeReplacements().Count, "Neither addition matches the removed key of that dictionary.");
    }

    [TestMethod]
    public void ReaddingTheSameInstanceIsNotAReplacement()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        object style = new();

        tracker.Removed(dictionary, "ProbeStyle", style);
        tracker.Added(dictionary, "ProbeStyle", style);

        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    public void ARemovalDoesNotPairWithAnAdditionInALaterBatch()
    {
        var tracker = new ReplacedValueTracker();
        object dictionary = new();
        tracker.Removed(dictionary, "ProbeStyle", new object());
        tracker.TakeReplacements();

        tracker.Added(dictionary, "ProbeStyle", new object());

        Assert.AreEqual(0, tracker.TakeReplacements().Count);
    }

    [TestMethod]
    [DataRow("BasedOn:Microsoft.UI.Xaml.Style, Microsoft.WinUI, Version=3.0.0.0, Culture=neutral, PublicKeyToken=null", "Microsoft.UI.Xaml.Style.BasedOn")]
    [DataRow("Value:Microsoft.UI.Xaml.Setter", "Microsoft.UI.Xaml.Setter.Value")]
    [DataRow("Value", "Microsoft.UI.Xaml.Setter.Value")]
    [DataRow("Property:Windows.UI.Xaml.Setter, Windows.Foundation.UniversalApiContract, Version=10.0.0.0, ContentType=WindowsRuntime", "Microsoft.UI.Xaml.Setter.Property")]
    [DataRow("Items:Sample.Control`1[[System.String, System.Runtime]], Sample", "Sample.Control`1[[System.String, System.Runtime]].Items")]
    public void ActionPropertyNamesBecomeFullMetadataNames(string actionPropertyName, string expected)
    {
        Assert.AreEqual(expected, XamlDiagnosticsPropertyName.FromActionProperty(actionPropertyName, "Microsoft.UI.Xaml.Setter"));
    }

    private static void InsertPending(List<Insertion> insertions)
    {
        foreach (Insertion insertion in insertions)
        {
            insertion.Collection.Insert(Math.Min(insertion.Index, insertion.Collection.Count), insertion.Item);
        }
    }
}
