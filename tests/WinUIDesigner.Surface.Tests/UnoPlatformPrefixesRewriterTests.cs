// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml;
using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Runs the rewriter with the real platform-prefix policy, the way the surface does before it hands prepared
/// XAML to the XAML reader. The markup is the one of https://github.com/0x5bfa/WinUIDesigner/issues/5.
/// </summary>
[TestClass]
public sealed class UnoPlatformPrefixesRewriterTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _DesignTime = "using:XSurfUwp";
    private const string _NullExtension = "{x:Null}";

    private static readonly XNamespace _P = _Presentation;
    private static readonly XNamespace _X = _XamlLanguage;
    private static readonly XNamespace _D = _DesignTime;

    // The page of the issue as Visual Studio prepares it: xmlns:d is re-pointed to the designer's own
    // namespace and design-time attributes are added; everything under a platform prefix is as written.
    private static readonly string _IssuePage = $"""
        <Page xmlns:_0="{_DesignTime}"
              xmlns="{_Presentation}"
              xmlns:x="{_XamlLanguage}"
              xmlns:d="{_DesignTime}"
              xmlns:mc="{_MarkupCompatibility}"
              xmlns:win="{_Presentation}"
              xmlns:not_win="http://uno.ui/not_win"
              xmlns:skia="http://uno.ui/skia"
              mc:Ignorable="d not_win skia" _0:DT.ShouldDisableImplicitStyle="True" d:DT.DesignWidth="800">
          <StackPanel x:Name="Root">
            <win:Border xmlns:tv="using:WinUI.TableView" x:Name="OnWindows">
              <tv:TableView />
            </win:Border>
            <not_win:Border x:Name="NotOnWindows">
              <ListView x:Name="InsideNotOnWindows" />
            </not_win:Border>
            <Grid win:IsDoubleTapEnabled="True">
              <Grid.ColumnDefinitions>
                <skia:ColumnDefinition Width="Auto" MinWidth="0" />
                <win:ColumnDefinition Width="Auto" MinWidth="128" />
              </Grid.ColumnDefinitions>
            </Grid>
            <TextBox win:SelectionFlyout="{_NullExtension}" win:TextReadingOrder="DetectFromContent" />
          </StackPanel>
        </Page>
        """;

    // One page with a branch for each of several platforms.
    private static readonly string _MultiPlatformPage = $"""
        <Page xmlns="{_Presentation}"
              xmlns:mc="{_MarkupCompatibility}"
              xmlns:win="{_Presentation}"
              xmlns:not_android="{_Presentation}"
              xmlns:android="http://uno.ui/android"
              xmlns:not_win="http://uno.ui/not_win"
              xmlns:ios="http://uno.ui/ios"
              mc:Ignorable="android not_win ios">
          <StackPanel>
            <win:Border Tag="windows" />
            <android:Border Tag="droid">
              <TextBlock Text="inside android" />
            </android:Border>
            <not_win:Border Tag="not windows" />
            <ios:Border Tag="ios" />
            <TextBlock win:Text="on Windows" android:Text="on Android" not_android:FontSize="24" not_win:FontSize="12" />
          </StackPanel>
        </Page>
        """;

    [TestMethod]
    public void ElementThatIsExcludedOnWindowsIsDroppedWithItsChildren()
    {
        XamlNamespaceRewriteResult result = Rewrite(_IssuePage, "win", out XDocument document);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(2, result.DroppedElements);
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "ListView"));
        CollectionAssert.AreEqual(
            new[] { "OnWindows" },
            document.Descendants().Where(element => element.Name.LocalName == "Border").Select(Name).ToArray());

        // Nothing of an Uno namespace is left for the XAML reader, and the dropped elements are reported.
        Assert.IsFalse(document.Descendants().Any(element => UnoPlatformPrefixes.IsPlatformUri(element.Name.NamespaceName)));
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("not_win:Border", StringComparison.Ordinal) && message.Contains("line 14", StringComparison.Ordinal)));
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("skia:ColumnDefinition", StringComparison.Ordinal) && message.Contains("line 19", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ElementThatIsIncludedOnWindowsStaysWithItsChildrenAndItsNestedNamespace()
    {
        Rewrite(_IssuePage, "win", out XDocument document);

        XElement border = document.Descendants(_P + "Border").Single();
        Assert.AreEqual("using:WinUI.TableView", (string?)border.Attribute(XNamespace.Xmlns + "tv"));
        Assert.AreEqual(XNamespace.Get("using:WinUI.TableView") + "TableView", border.Elements().Single().Name);
    }

    [TestMethod]
    public void ExcludedItemOfATypedCollectionIsDroppedNextToAnIncludedOne()
    {
        Rewrite(_IssuePage, "win", out XDocument document);

        XElement definitions = document.Descendants(_P + "Grid.ColumnDefinitions").Single();
        XElement definition = definitions.Elements().Single();
        Assert.AreEqual(_P + "ColumnDefinition", definition.Name);
        CollectionAssert.AreEqual(new[] { "Width=Auto", "MinWidth=128" }, PlainAttributes(definition));
    }

    [TestMethod]
    public void AttributesThatAreIncludedOnWindowsBecomePlainAttributesWithTheirValues()
    {
        XamlNamespaceRewriteResult result = Rewrite(_IssuePage, "win", out XDocument document);

        CollectionAssert.AreEqual(new[] { "IsDoubleTapEnabled=True" }, PlainAttributes(document.Descendants(_P + "Grid").Single()));
        CollectionAssert.AreEqual(
            new[] { "SelectionFlyout={x:Null}", "TextReadingOrder=DetectFromContent" },
            PlainAttributes(document.Descendants(_P + "TextBox").Single()));
        Assert.AreEqual(3, result.NormalizedMembers);
        Assert.AreEqual(0, result.DroppedAttributes);
        Assert.AreEqual(0, result.RepointedDeclarations);

        // The markup extension still finds its prefix when the rewritten text is parsed again.
        XDocument reloaded = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        XElement textBox = reloaded.Descendants(_P + "TextBox").Single();
        Assert.AreEqual("{x:Null}", (string?)textBox.Attribute("SelectionFlyout"));
        Assert.AreEqual(_X, textBox.GetNamespaceOfPrefix("x"));
    }

    [TestMethod]
    public void DesignTimeNamespacesOfThePreparedXamlAreNotTouched()
    {
        Rewrite(_IssuePage, "win", out XDocument document);

        XElement root = document.Root!;
        Assert.AreEqual(_DesignTime, (string?)root.Attribute(XNamespace.Xmlns + "d"));
        Assert.AreEqual(_DesignTime, (string?)root.Attribute(XNamespace.Xmlns + "_0"));
        Assert.AreEqual("True", (string?)root.Attribute(_D + "DT.ShouldDisableImplicitStyle"));
        Assert.AreEqual("800", (string?)root.Attribute(_D + "DT.DesignWidth"));
        Assert.AreEqual("d not_win skia", (string?)root.Attribute(XNamespace.Get(_MarkupCompatibility) + "Ignorable"));

        // The declarations of the platform prefixes stay as well; nothing refers to the dropped ones any more.
        Assert.AreEqual(_Presentation, (string?)root.Attribute(XNamespace.Xmlns + "win"));
        Assert.AreEqual("http://uno.ui/not_win", (string?)root.Attribute(XNamespace.Xmlns + "not_win"));
        Assert.AreEqual("Root", (string?)root.Elements().Single().Attribute(_X + "Name"));
    }

    [TestMethod]
    public void DroppedElementCannotClaimASourcePosition()
    {
        (string Name, int Line, int Position)[] survivors = XDocument.Parse(_IssuePage, LoadOptions.SetLineInfo)
            .Descendants()
            .Where(element => !UnoPlatformPrefixes.IsPlatformUri(element.Name.NamespaceName) && element.Name.LocalName != "ListView")
            .Select(LineInfo)
            .ToArray();

        Rewrite(_IssuePage, "win", out XDocument document);

        // What is left is what was loaded, each element at the position it has in the prepared file.
        CollectionAssert.AreEqual(survivors, document.Descendants().Select(LineInfo).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Page", "StackPanel", "Border", "TableView", "Grid", "Grid.ColumnDefinitions", "ColumnDefinition", "TextBox" },
            document.Descendants().Select(element => element.Name.LocalName).ToArray());
    }

    [TestMethod]
    public void AttributeOfAPlatformPrefixOnAnElementOfAClrNamespaceBecomesAPlainAttribute()
    {
        // The XAML reader does not find win:Tag on an element whose own namespace is not the presentation namespace.
        string xaml = $"""
            <Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}" xmlns:prim="using:Microsoft.UI.Xaml.Controls.Primitives">
              <prim:ToggleButton win:Tag="w" win:Grid.Row="1" Content="toggle" />
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        XElement button = document.Root!.Elements().Single();
        Assert.AreEqual(XNamespace.Get("using:Microsoft.UI.Xaml.Controls.Primitives") + "ToggleButton", button.Name);
        CollectionAssert.AreEqual(new[] { "Tag=w", "Grid.Row=1", "Content=toggle" }, PlainAttributes(button));
        Assert.AreEqual(2, result.NormalizedMembers);
        StringAssert.Contains(document.ToString(SaveOptions.DisableFormatting), "<prim:ToggleButton Tag=\"w\" Grid.Row=\"1\" Content=\"toggle\" />");
    }

    [TestMethod]
    public void PropertyElementFollowsItsPrefix()
    {
        string xaml = $"""
            <Grid xmlns="{_Presentation}" xmlns:mc="{_MarkupCompatibility}" xmlns:win="{_Presentation}" xmlns:not_win="http://uno.ui/not_win" mc:Ignorable="not_win">
              <win:Grid.ColumnDefinitions>
                <ColumnDefinition />
              </win:Grid.ColumnDefinitions>
              <not_win:Grid.RowDefinitions>
                <RowDefinition />
              </not_win:Grid.RowDefinitions>
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        CollectionAssert.AreEqual(new[] { _P + "Grid.ColumnDefinitions" }, document.Root!.Elements().Select(element => element.Name).ToArray());
        Assert.IsFalse(document.Descendants(_P + "RowDefinition").Any());
        Assert.AreEqual(1, result.DroppedElements);
    }

    [TestMethod]
    public void SamePageShowsItsWindowsBranchOnWindows()
    {
        XamlNamespaceRewriteResult result = Rewrite(_MultiPlatformPage, "win", out XDocument document);

        CollectionAssert.AreEqual(new[] { "windows" }, BorderTags(document));
        CollectionAssert.AreEqual(new[] { "Text=on Windows", "FontSize=24" }, PlainAttributes(document.Descendants(_P + "TextBlock").Single()));
        Assert.AreEqual(3, result.DroppedElements);
        Assert.AreEqual(2, result.DroppedAttributes);
        Assert.AreEqual(2, result.NormalizedMembers);
        Assert.AreEqual(0, result.RepointedDeclarations);
    }

    [TestMethod]
    public void SamePageShowsItsAndroidBranchForTheAndroidTarget()
    {
        XamlNamespaceRewriteResult result = Rewrite(_MultiPlatformPage, "android", out XDocument document);

        // android: and not_win: are kept and become members of the presentation namespace; win: is dropped.
        CollectionAssert.AreEqual(new[] { "droid", "not windows" }, BorderTags(document));
        Assert.IsTrue(document.Descendants().All(element => element.Name.Namespace == _P));
        CollectionAssert.AreEqual(
            new[] { "Text=inside android", "Text=on Android|FontSize=12" },
            document.Descendants(_P + "TextBlock").Select(element => string.Join("|", PlainAttributes(element))).ToArray());
        Assert.AreEqual(2, result.DroppedElements);
        Assert.AreEqual(2, result.DroppedAttributes);

        // The kept Uno namespaces now name the presentation namespace, so prefixed values keep resolving.
        Assert.AreEqual(_Presentation, (string?)document.Root!.Attribute(XNamespace.Xmlns + "android"));
        Assert.AreEqual(_Presentation, (string?)document.Root.Attribute(XNamespace.Xmlns + "not_win"));
        Assert.AreEqual("http://uno.ui/ios", (string?)document.Root.Attribute(XNamespace.Xmlns + "ios"));
        Assert.AreEqual(2, result.RepointedDeclarations);
    }

    [TestMethod]
    public void PlatformPrefixThatIsMissingFromIgnorableIsStillDropped()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:android="http://uno.ui/android">
              <android:Border />
              <TextBlock android:Text="droid" Text="everywhere" />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);
        CollectionAssert.AreEqual(new[] { _P + "TextBlock" }, document.Root!.Elements().Select(element => element.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Text=everywhere" }, PlainAttributes(document.Root.Elements().Single()));

        // The caller reports the declaration the XAML compiler would reject.
        CollectionAssert.AreEqual(
            new[] { "android" },
            UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml)).Select(declaration => declaration.Prefix).ToArray());
    }

    [TestMethod]
    public void UnknownUnoPrefixIsDroppedWhenItIsIgnorableAndLeftOtherwise()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:mc="{_MarkupCompatibility}" xmlns:legacy="http://uno.ui/legacy" xmlns:other="http://uno.ui/other" mc:Ignorable="legacy">
              <legacy:Border legacy:Tag="1" />
              <other:Border />
              <TextBlock legacy:Text="old" Text="new" />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        // The element that is not ignorable stays for the XAML reader to reject, as the XAML compiler would.
        CollectionAssert.AreEqual(
            new[] { XNamespace.Get("http://uno.ui/other") + "Border", _P + "TextBlock" },
            document.Root!.Elements().Select(element => element.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Text=new" }, PlainAttributes(document.Descendants(_P + "TextBlock").Single()));
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);
    }

    [TestMethod]
    public void UsingSuffixNamesTheNamespaceOfAKeptPrefixAndChangesNothingForADroppedOne()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:mc="{_MarkupCompatibility}" xmlns:android="http://uno.ui/android#using:My.Droid;My.Other" mc:Ignorable="android">
              <android:NativeView android:Tag="x" />
              <TextBlock />
            </StackPanel>
            """;

        XamlNamespaceRewriteResult onWindows = Rewrite(xaml, "win", out XDocument windows);
        XamlNamespaceRewriteResult onAndroid = Rewrite(xaml, "android", out XDocument android);

        Assert.AreEqual(1, onWindows.DroppedElements);
        CollectionAssert.AreEqual(new[] { _P + "TextBlock" }, windows.Root!.Elements().Select(element => element.Name).ToArray());

        Assert.AreEqual(0, onAndroid.DroppedElements);
        XElement view = android.Root!.Elements().First();
        Assert.AreEqual(XNamespace.Get("using:My.Droid") + "NativeView", view.Name);
        CollectionAssert.AreEqual(new[] { "Tag=x" }, PlainAttributes(view));
    }

    [TestMethod]
    public void PrefixOfAClrNamespaceIsNotAPlatformPrefixWhateverItIsCalled()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:skia="using:SkiaSharp.Views.Windows" xmlns:desktop="using:My.App.Desktop">
              <skia:SKXamlCanvas skia:Tag="1" />
              <desktop:Shell />
            </StackPanel>
            """;
        string before = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);

        foreach (string target in new[] { "win", "android", "skia" })
        {
            XamlNamespaceRewriteResult result = Rewrite(xaml, target, out XDocument document);

            Assert.AreSame(XamlNamespaceRewriteResult.Unchanged, result, target);
            Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting), target);
        }
    }

    [TestMethod]
    public void DocumentWithoutPlatformPrefixesIsNotTouched()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:d="{_DesignTime}" xmlns:local="using:My.App" xmlns:mc="{_MarkupCompatibility}" mc:Ignorable="d">
              <local:Card x:Name="card" d:DT.DesignWidth="800" Title="Why?" />
            </Page>
            """;
        string before = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        Assert.AreSame(XamlNamespaceRewriteResult.Unchanged, result);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void DocumentThatOnlyUsesTheWindowsPrefixOnElementsKeepsItsText()
    {
        // win:Border already is an element of the presentation namespace: there is nothing to rewrite.
        string xaml = $"""<Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}"><win:Border><win:TextBlock /></win:Border></Grid>""";
        string before = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = Rewrite(xaml, "win", out XDocument document);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }

    private static XamlNamespaceRewriteResult Rewrite(string xaml, string targetPlatform, out XDocument document)
    {
        document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions { SourceXaml = xaml, TargetPlatform = targetPlatform });
    }

    private static string? Name(XElement element)
    {
        return (string?)element.Attribute(_X + "Name");
    }

    private static string?[] BorderTags(XDocument document)
    {
        return document.Descendants().Where(element => element.Name.LocalName == "Border").Select(element => (string?)element.Attribute("Tag")).ToArray();
    }

    /// <summary>
    /// Returns the attributes that are neither namespace declarations nor directives as "Name=value", in
    /// document order. A name that is still in a namespace shows it, so a leftover prefix does not go unnoticed.
    /// </summary>
    private static string[] PlainAttributes(XElement element)
    {
        return element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace != _X)
            .Select(attribute => $"{attribute.Name}={attribute.Value}")
            .ToArray();
    }

    private static (string Name, int Line, int Position) LineInfo(XElement element)
    {
        IXmlLineInfo lineInfo = element;
        Assert.IsTrue(lineInfo.HasLineInfo(), $"<{element.Name.LocalName}> lost its line information.");
        return (element.Name.LocalName, lineInfo.LineNumber, lineInfo.LinePosition);
    }
}
