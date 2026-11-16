// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// The platform-prefix policy does not interpret prefixes yet. These tests pin that:
/// until it does, platform members must reach the XAML reader exactly as they were written.
/// </summary>
[TestClass]
public sealed class UnoPlatformPrefixesTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void DesignerPreviewsWindowsByDefault()
    {
        Assert.AreEqual("win", new XamlNamespaceRewriteOptions().TargetPlatform);
        Assert.AreEqual("win", new XamlNamespaceRewriteOptions { TargetPlatform = string.Empty }.TargetPlatform);
        Assert.AreEqual("android", new XamlNamespaceRewriteOptions { TargetPlatform = "android" }.TargetPlatform);
    }

    [TestMethod]
    [DataRow("http://uno.ui/android", true)]
    [DataRow("http://uno.ui/not_win", true)]
    [DataRow("http://uno.ui/android#using:My.Droid", true)]
    [DataRow("http://uno.ui", false)]
    [DataRow("HTTP://UNO.UI/android", false)]
    [DataRow(_Presentation, false)]
    [DataRow("using:My.App", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void PlatformNamespaceIsRecognizedByItsUri(string? namespaceUri, bool expected)
    {
        Assert.AreEqual(expected, UnoPlatformPrefixes.IsPlatformUri(namespaceUri));
    }

    [TestMethod]
    [DataRow("win", _Presentation, false)]
    [DataRow("not_android", _Presentation, false)]
    [DataRow("not_win", "http://uno.ui/not_win", true)]
    [DataRow("android", "http://uno.ui/android", true)]
    [DataRow("android", "http://uno.ui/android#using:My.Droid", true)]
    public void PlatformPrefixIsLeftAloneOnEveryPlatform(string prefix, string uri, bool isIgnorable)
    {
        foreach (string platform in new[] { "win", "android", "ios", "wasm", "skia" })
        {
            Assert.AreEqual(
                XamlPrefixDisposition.Leave,
                UnoPlatformPrefixes.Classify(prefix, XamlNamespaceUri.Parse(uri), isIgnorable, platform),
                $"Prefix '{prefix}' on platform '{platform}'.");
        }
    }

    [TestMethod]
    public void DocumentWithPlatformMembersIsNotRewritten()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}"
                  xmlns:win="{_Presentation}"
                  xmlns:not_win="http://uno.ui/not_win"
                  xmlns:android="http://uno.ui/android"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  mc:Ignorable="not_win android">
              <win:Border win:Tag="windows" android:Tag="droid" />
              <not_win:Border />
            </Page>
            """;
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        string before = document.ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions { SourceXaml = xaml });

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }
}
