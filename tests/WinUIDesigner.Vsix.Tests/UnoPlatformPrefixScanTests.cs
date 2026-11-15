// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers what the Visual Studio side reads from the text of a document before it sends the document to the
/// surface: whether platform prefixes are involved at all, whether an element is written under one (the XAML
/// cleaner replaces such an element), and which platform namespaces are missing from <c>mc:Ignorable</c>.
/// </summary>
[TestClass]
public sealed class UnoPlatformPrefixScanTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    // The markup of https://github.com/0x5bfa/WinUIDesigner/issues/5.
    private const string _IssuePage = """
        <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
              xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
              xmlns:win="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:not_win="http://uno.ui/not_win"
              xmlns:skia="http://uno.ui/skia"
              mc:Ignorable="d not_win skia">
          <StackPanel>
            <win:Border xmlns:tv="using:WinUI.TableView">
              <tv:TableView />
            </win:Border>
            <not_win:Border>
              <ListView />
            </not_win:Border>
            <Grid win:IsDoubleTapEnabled="True">
              <Grid.ColumnDefinitions>
                <skia:ColumnDefinition Width="Auto" MinWidth="0" />
                <win:ColumnDefinition Width="Auto" MinWidth="128" />
              </Grid.ColumnDefinitions>
            </Grid>
            <TextBox win:SelectionFlyout="{x:Null}" win:TextReadingOrder="DetectFromContent" />
          </StackPanel>
        </Page>
        """;

    [TestMethod]
    public void IssuePageHasElementsTheCleanerReplaces()
    {
        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(_IssuePage);

        Assert.IsTrue(scan.IsComplete);
        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.IsTrue(scan.HasPlatformPrefixedObjectElements);
        Assert.IsTrue(XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(scan).Count);
    }

    [TestMethod]
    public void PageWithPlatformAttributesOnlyKeepsItsPreparedXaml()
    {
        // The cleaner passes attributes and property elements of an ignorable namespace through as written.
        string xaml =
            $"<Page xmlns=\"{_Presentation}\" xmlns:mc=\"{_MarkupCompatibility}\" xmlns:win=\"{_Presentation}\" xmlns:android=\"http://uno.ui/android\" mc:Ignorable=\"android\">" +
            "<Grid win:Tag=\"w\" android:Tag=\"a\"><android:Grid.RowDefinitions><RowDefinition /></android:Grid.RowDefinitions><win:Border /></Grid></Page>";

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(scan.HasPlatformNamespaces);
        Assert.IsFalse(scan.HasPlatformPrefixedObjectElements);
        Assert.IsTrue(XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));
    }

    [TestMethod]
    public void PageWithoutUnoNamespacesIsNotLookedAtFurther()
    {
        // win: alone is the presentation namespace: the cleaner resolves everything, nothing is replaced.
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:win=\"{_Presentation}\"><win:Border win:Tag=\"w\" /></Page>";

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsFalse(scan.HasPlatformNamespaces);
        Assert.IsFalse(scan.HasPlatformPrefixedObjectElements);
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(scan).Count);
    }

    [TestMethod]
    public void PlatformNamespaceThatIsMissingFromIgnorableIsReportedWithItsPosition()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:android="http://uno.ui/android"
                  xmlns:not_win="http://uno.ui/not_win#using:My.App"
                  mc:Ignorable="android">
              <not_win:Border />
            </Page>
            """;

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);
        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(scan);

        Assert.AreEqual(1, missing.Count);
        Assert.AreEqual("not_win", missing[0].Prefix);
        Assert.AreEqual("http://uno.ui/not_win#using:My.App", missing[0].Uri.Original);
        Assert.AreEqual(4, missing[0].LineNumber);
        Assert.IsTrue(missing[0].LinePosition > 0);

        // The prefix is applied all the same: its members are not part of the Windows preview.
        Assert.AreEqual(
            XamlPrefixDisposition.Drop,
            UnoPlatformPrefixes.Classify("not_win", missing[0].Uri, scan.IsIgnorable("not_win"), UnoPlatformPrefixes.DefaultTargetPlatform));
        Assert.IsTrue(scan.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    [DataRow("win", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "win", "Keep")]
    [DataRow("win", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "android", "Drop")]
    [DataRow("android", "http://uno.ui/android", true, "win", "Drop")]
    [DataRow("android", "http://uno.ui/android", true, "android", "Keep")]
    [DataRow("not_win", "http://uno.ui/not_win", true, "win", "Drop")]
    [DataRow("not_win", "http://uno.ui/not_win", true, "skia", "Keep")]
    [DataRow("skia", "using:SkiaSharp.Views.Windows", false, "win", "Leave")]
    [DataRow("legacy", "http://uno.ui/legacy", true, "win", "Drop")]
    [DataRow("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false, "android", "Leave")]
    public void DocumentModelSideClassifiesLikeTheSurface(string prefix, string uri, bool isIgnorable, string target, string expected)
    {
        // The policy is shared source: the Visual Studio process compiles it for the .NET Framework.
        XamlPrefixDisposition actual = XamlNamespacePolicies.Classify(prefix, XamlNamespaceUri.Parse(uri), isIgnorable, target, evaluateCondition: null);

        Assert.AreEqual((XamlPrefixDisposition)Enum.Parse(typeof(XamlPrefixDisposition), expected), actual);
    }
}
