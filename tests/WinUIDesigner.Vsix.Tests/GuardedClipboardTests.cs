// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.Utility.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

[TestClass]
public sealed class GuardedClipboardTests
{
    // CLIPBRD_E_CANT_OPEN: what WPF reports when another process keeps the clipboard open.
    private const int _ClipboardCannotBeOpened = unchecked((int)0x800401D0);

    [TestMethod]
    public void ReadableClipboardAnswersForItself()
    {
        var data = new DataObject();
        var clipboard = new FakeClipboard { ContainsDataResult = true, Data = "text", DataObject = data, IsCurrentResult = true };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add);

        Assert.IsTrue(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual("text", guarded.GetData(DataFormats.Text));
        Assert.AreSame(data, guarded.GetDataObject());
        Assert.IsTrue(guarded.IsCurrent(data));
        Assert.AreEqual(0, reports.Count);
    }

    [TestMethod]
    public void ClipboardThatCannotBeOpenedCountsAsEmpty()
    {
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard);

        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));
        Assert.IsNull(guarded.GetData(DataFormats.Text));
        Assert.IsFalse(guarded.IsCurrent(new DataObject()));

        IDataObject data = guarded.GetDataObject();
        Assert.IsNotNull(data, "Visual Studio uses the data object without checking it.");
        Assert.AreEqual(0, data.GetFormats().Length);
    }

    [TestMethod]
    public void AccessDeniedCountsAsEmptyToo()
    {
        var clipboard = new FakeClipboard { Failure = new UnauthorizedAccessException() };
        var guarded = new GuardedClipboard(clipboard);

        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));
    }

    [TestMethod]
    public void OtherFailuresAreNotHidden()
    {
        var clipboard = new FakeClipboard { Failure = new InvalidOperationException("not a clipboard failure") };
        var guarded = new GuardedClipboard(clipboard);

        Assert.ThrowsExactly<InvalidOperationException>(() => guarded.ContainsData(DataFormats.Text));
        Assert.ThrowsExactly<InvalidOperationException>(() => guarded.GetDataObject());
    }

    [TestMethod]
    public void WritingReportsItsFailureToTheCaller()
    {
        // Visual Studio shows a message when a copy fails; hiding the failure would lose it.
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard);

        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("text"));
        Assert.ThrowsExactly<COMException>(() => guarded.SetDataObject("text", true));
    }

    [TestMethod]
    public void WritingReachesTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var guarded = new GuardedClipboard(clipboard);

        guarded.SetDataObject("first");
        guarded.SetDataObject("second", true);

        CollectionAssert.AreEqual(new[] { "first", "second (copy)" }, clipboard.Written);
    }

    [TestMethod]
    public void ContainsDataDoesNotAskAgainUntilTheRetryDelayHasPassed()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard, getTime: () => now);

        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual(1, clipboard.Reads);

        // Visual Studio asks for several formats on each refresh; only the first question waits for the clipboard.
        now = GuardedClipboard.RetryDelay - TimeSpan.FromMilliseconds(1);
        Assert.IsFalse(guarded.ContainsData(DataFormats.Bitmap));
        Assert.IsFalse(guarded.ContainsData(DataFormats.FileDrop));
        Assert.AreEqual(1, clipboard.Reads);

        now = GuardedClipboard.RetryDelay;
        clipboard.Failure = null;
        clipboard.ContainsDataResult = true;
        Assert.IsTrue(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual(2, clipboard.Reads);
    }

    [TestMethod]
    public void EveryFailureStartsTheRetryDelayAgain()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard, getTime: () => now);

        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));

        now = GuardedClipboard.RetryDelay;
        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual(2, clipboard.Reads);

        now = GuardedClipboard.RetryDelay + GuardedClipboard.RetryDelay - TimeSpan.FromMilliseconds(1);
        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));
        Assert.AreEqual(2, clipboard.Reads);
    }

    [TestMethod]
    public void PastingAsksTheClipboardEvenDuringTheRetryDelay()
    {
        TimeSpan now = TimeSpan.Zero;
        var clipboard = new FakeClipboard { Failure = CreateCannotOpenFailure() };
        var guarded = new GuardedClipboard(clipboard, getTime: () => now);
        Assert.IsFalse(guarded.ContainsData(DataFormats.Text));

        // The clipboard is free again and the user pastes straight away.
        var data = new DataObject();
        clipboard.Failure = null;
        clipboard.DataObject = data;

        Assert.AreSame(data, guarded.GetDataObject());

        // That read showed the clipboard is back, so the next question is asked as well.
        clipboard.ContainsDataResult = true;
        Assert.IsTrue(guarded.ContainsData(DataFormats.Text));
    }

    [TestMethod]
    public void EachOutageIsReportedOnceAndSoIsItsEnd()
    {
        TimeSpan now = TimeSpan.Zero;
        COMException failure = CreateCannotOpenFailure();
        var clipboard = new FakeClipboard { Failure = failure };
        var reports = new List<Exception?>();
        var guarded = new GuardedClipboard(clipboard, reports.Add, () => now);

        guarded.ContainsData(DataFormats.Text);
        guarded.GetDataObject();
        now = GuardedClipboard.RetryDelay;
        guarded.ContainsData(DataFormats.Text);
        Assert.AreEqual(1, reports.Count);
        Assert.AreSame(failure, reports[0]);

        clipboard.Failure = null;
        now += GuardedClipboard.RetryDelay;
        guarded.ContainsData(DataFormats.Text);
        guarded.ContainsData(DataFormats.Text);
        Assert.AreEqual(2, reports.Count);
        Assert.IsNull(reports[1]);

        clipboard.Failure = failure;
        guarded.ContainsData(DataFormats.Text);
        Assert.AreEqual(3, reports.Count);
        Assert.AreSame(failure, reports[2]);
    }

    private static COMException CreateCannotOpenFailure()
    {
        return new COMException("OpenClipboard Failed", _ClipboardCannotBeOpened);
    }

    private sealed class FakeClipboard : IClipboard
    {
        public Exception? Failure { get; set; }

        public bool ContainsDataResult { get; set; }

        public object? Data { get; set; }

        public IDataObject? DataObject { get; set; }

        public bool IsCurrentResult { get; set; }

        public int Reads { get; private set; }

        public List<string> Written { get; } = [];

        public bool ContainsData(string format)
        {
            Read();
            return ContainsDataResult;
        }

        public object GetData(string format)
        {
            Read();
            return Data!;
        }

        public IDataObject GetDataObject()
        {
            Read();
            return DataObject!;
        }

        public bool IsCurrent(IDataObject data)
        {
            Read();
            return IsCurrentResult;
        }

        public void SetDataObject(object data)
        {
            ThrowIfFailing();
            Written.Add((string)data);
        }

        public void SetDataObject(object data, bool copy)
        {
            ThrowIfFailing();
            Written.Add(copy ? $"{data} (copy)" : (string)data);
        }

        private void Read()
        {
            Reads++;
            ThrowIfFailing();
        }

        private void ThrowIfFailing()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
