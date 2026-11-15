// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Text;
using System.Xml;
using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Exercises the rewrite mechanics. The verdict of every prefix is supplied by the test, so these tests hold
/// whatever the namespace policies decide.
/// </summary>
[TestClass]
public sealed class XamlNamespaceRewriterTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _Android = "http://uno.ui/android";
    private const string _Conditional = _Presentation + "?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";

    private static readonly XNamespace _P = _Presentation;
    private static readonly XNamespace _X = _XamlLanguage;

    [TestMethod]
    public void DroppedElementIsRemovedWithItsSubtree()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:android="{_Android}">
              <StackPanel>
                <android:Border>
                  <TextBlock Text="inside" />
                </android:Border>
                <TextBlock Text="outside" />
              </StackPanel>
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("android", XamlPrefixDisposition.Drop), out XDocument document);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(0, result.DroppedAttributes);
        Assert.AreEqual(0, result.NormalizedMembers);
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "Border"));
        CollectionAssert.AreEqual(new[] { "outside" }, document.Descendants(_P + "TextBlock").Select(element => (string?)element.Attribute("Text")).ToArray());
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("android:Border", StringComparison.Ordinal) && message.Contains("line 3", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DroppedAttributeIsRemovedFromItsElement()
    {
        string xaml = $"""<TextBlock xmlns="{_Presentation}" xmlns:android="{_Android}" Text="everywhere" android:Text="droid" FontSize="12" />""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("android", XamlPrefixDisposition.Drop), out XDocument document);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(0, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);
        CollectionAssert.AreEqual(new[] { "Text=everywhere", "FontSize=12" }, PlainAttributes(document.Root!));
    }

    [TestMethod]
    public void KeptMembersBecomePlainMembersOfTheirBareNamespace()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}">
              <c7:Border c7:CornerRadius="4" Tag="t" c7:Grid.Row="1" />
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("c7", XamlPrefixDisposition.Keep), out XDocument document);

        XElement border = document.Root!.Elements().Single();
        Assert.AreEqual(_P + "Border", border.Name);
        CollectionAssert.AreEqual(new[] { "CornerRadius=4", "Tag=t", "Grid.Row=1" }, PlainAttributes(border));
        Assert.IsTrue(border.Attributes().All(attribute => attribute.Name.NamespaceName.Length == 0));

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(3, result.NormalizedMembers);
        Assert.AreEqual(1, result.RepointedDeclarations);
        Assert.AreEqual(0, result.DroppedElements);
        Assert.AreEqual(0, result.DroppedAttributes);

        string rewritten = document.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(rewritten.Contains('?'), rewritten);
    }

    [TestMethod]
    public void MemberThatAlreadyIsInItsBareNamespaceIsNotChanged()
    {
        // "win" is bound to the presentation namespace itself: there is nothing to normalize on an element.
        string xaml = $"""<Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}"><win:Border /></Grid>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("win", XamlPrefixDisposition.Keep), out XDocument document);

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(_P + "Border", document.Root!.Elements().Single().Name);
    }

    [TestMethod]
    public void PropertyElementsFollowTheirPrefix()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}" xmlns:android="{_Android}">
              <Border>
                <c7:Border.Background>Red</c7:Border.Background>
                <android:Border.Child><Grid /></android:Border.Child>
              </Border>
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix switch
            {
                "c7" => XamlPrefixDisposition.Keep,
                "android" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document);

        XElement border = document.Root!.Elements().Single();
        CollectionAssert.AreEqual(new[] { _P + "Border.Background" }, border.Elements().Select(element => element.Name).ToArray());
        Assert.AreEqual("Red", border.Elements().Single().Value);
        Assert.IsFalse(document.Descendants(_P + "Grid").Any());
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, result.NormalizedMembers);
    }

    [TestMethod]
    public void UsingSuffixNamesTheNamespaceKeptMembersMoveTo()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:droid="{_Android}#using:My.Droid;My.Other">
              <droid:NativeView droid:Tag="x" />
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("droid", XamlPrefixDisposition.Keep), out XDocument document);

        XElement view = document.Root!.Elements().Single();
        Assert.AreEqual(XNamespace.Get("using:My.Droid") + "NativeView", view.Name);
        CollectionAssert.AreEqual(new[] { "Tag=x" }, PlainAttributes(view));
        Assert.AreEqual("using:My.Droid", (string?)document.Root!.Attribute(XNamespace.Xmlns + "droid"));
        Assert.AreEqual(2, result.NormalizedMembers);
        Assert.AreEqual(1, result.RepointedDeclarations);

        // The declaration that was re-pointed is the one the element is written with again.
        StringAssert.Contains(document.ToString(SaveOptions.DisableFormatting), "<droid:NativeView Tag=\"x\" />");
    }

    [TestMethod]
    public void PlatformNamespaceStandsForThePresentationNamespace()
    {
        string xaml = $"""<Page xmlns="{_Presentation}" xmlns:android="{_Android}"><android:Border android:Tag="1" /></Page>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("android", XamlPrefixDisposition.Keep), out XDocument document);

        XElement border = document.Root!.Elements().Single();
        Assert.AreEqual(_P + "Border", border.Name);
        CollectionAssert.AreEqual(new[] { "Tag=1" }, PlainAttributes(border));
        Assert.AreEqual(_Presentation, (string?)document.Root!.Attribute(XNamespace.Xmlns + "android"));
        Assert.AreEqual(2, result.NormalizedMembers);
    }

    [TestMethod]
    public void KeptDeclarationIsRepointedSoThatPrefixedValuesStillResolve()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}" xmlns:android="{_Android}">
              <Page.Resources>
                <Style TargetType="c7:Button" />
              </Page.Resources>
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix switch
            {
                "c7" => XamlPrefixDisposition.Keep,
                "android" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, result.RepointedDeclarations);
        Assert.AreEqual(0, result.NormalizedMembers);

        // The declaration stays, bound to the bare namespace; a dropped prefix keeps its declaration untouched.
        XDocument reloaded = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        XElement style = reloaded.Descendants(_P + "Style").Single();
        Assert.AreEqual("c7:Button", (string?)style.Attribute("TargetType"));
        Assert.AreEqual(_P, style.GetNamespaceOfPrefix("c7"));
        Assert.AreEqual(XNamespace.Get(_Android), style.GetNamespaceOfPrefix("android"));
    }

    [TestMethod]
    public void RootIsNeverDropped()
    {
        string xaml = $"""<android:Page xmlns="{_Presentation}" xmlns:android="{_Android}" android:Tag="1"><Grid /></android:Page>""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("android", XamlPrefixDisposition.Drop), out XDocument document);

        Assert.AreEqual(XNamespace.Get(_Android) + "Page", document.Root!.Name);
        Assert.AreEqual(1, document.Root.Elements(_P + "Grid").Count());
        Assert.AreEqual(0, result.DroppedElements);

        // Its attribute under the same prefix is an ordinary member and goes.
        Assert.AreEqual(1, result.DroppedAttributes);
        Assert.AreEqual(0, PlainAttributes(document.Root).Length);
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("root", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void PrefixesThatShareANamespaceAreToldApartByPrefix()
    {
        // "win", "not_android" and the default namespace are all the presentation namespace.
        string xaml = $"""
            <Grid xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:win="{_Presentation}" xmlns:not_android="{_Presentation}">
              <win:Border x:Name="a" />
              <Border x:Name="b" win:Tag="w" not_android:Opacity="1" Margin="2" />
              <not_android:Border x:Name="c" />
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("win", XamlPrefixDisposition.Drop), out XDocument document);

        CollectionAssert.AreEqual(new[] { "b", "c" }, document.Root!.Elements(_P + "Border").Select(element => (string?)element.Attribute(_X + "Name")).ToArray());
        XElement kept = document.Root.Elements().First();
        Assert.IsNull(kept.Attribute(_P + "Tag"));
        Assert.AreEqual("1", (string?)kept.Attribute(_P + "Opacity"));
        Assert.AreEqual("2", (string?)kept.Attribute("Margin"));
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);
        Assert.AreEqual(0, result.NormalizedMembers);
    }

    [TestMethod]
    public void WithoutTheSourceTextPrefixesThatShareANamespaceAreLeftAlone()
    {
        string xaml = $"""
            <Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}" xmlns:not_android="{_Presentation}" xmlns:android="{_Android}">
              <win:Border win:Tag="w" />
              <android:Border />
            </Grid>
            """;

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix is "win" or "android" ? XamlPrefixDisposition.Drop : XamlPrefixDisposition.Leave,
            out XDocument document,
            options => options.SourceXaml = null);

        // Nothing tells <win:Border> from <Border>, or win:Tag from not_android:Tag, any more, so they stay.
        // "android" is the only prefix of its namespace and is still recognized.
        XElement border = document.Root!.Elements().Single();
        Assert.AreEqual(_P + "Border", border.Name);
        Assert.AreEqual("w", (string?)border.Attribute(_P + "Tag"));
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(0, result.DroppedAttributes);
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("unknown", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LineInformationOfSurvivingElementsIsKept()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}" xmlns:android="{_Android}">
              <android:Border />
              <c7:Grid>
                <TextBlock c7:Text="a" android:Tag="b" />
              </c7:Grid>
            </Page>
            """;
        (string Name, int Line, int Position)[] expected = XDocument.Parse(xaml, LoadOptions.SetLineInfo)
            .Descendants()
            .Where(element => element.Name.LocalName != "Border")
            .Select(LineInfo)
            .ToArray();

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix switch
            {
                "c7" => XamlPrefixDisposition.Keep,
                "android" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document);

        Assert.IsTrue(result.Changed);
        CollectionAssert.AreEqual(new[] { ("Page", 1, 2), ("Grid", 3, 4), ("TextBlock", 4, 6) }, expected);
        CollectionAssert.AreEqual(expected, document.Descendants().Select(LineInfo).ToArray());
    }

    [TestMethod]
    public void DocumentWithoutPolicyNamespacesIsNotTouched()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:local="using:My.App" xmlns:mc="{_MarkupCompatibility}" mc:Ignorable="d">
              <local:Card x:Name="card" Title="Why?" />
            </Page>
            """;
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        string before = document.ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions { SourceXaml = xaml });

        Assert.IsFalse(result.Changed);
        Assert.AreSame(XamlNamespaceRewriteResult.Unchanged, result);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void DocumentWhosePrefixesAreAllLeftAloneIsNotTouched()
    {
        string xaml = $"""<Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}" xmlns:android="{_Android}"><c7:Border android:Tag="1" /></Page>""";
        string before = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = Rewrite(xaml, _ => XamlPrefixDisposition.Leave, out XDocument document);

        Assert.AreSame(XamlNamespaceRewriteResult.Unchanged, result);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void EmptyDocumentIsNotTouched()
    {
        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(new XDocument(), new XamlNamespaceRewriteOptions());

        Assert.AreSame(XamlNamespaceRewriteResult.Unchanged, result);
    }

    [TestMethod]
    public void PrefixRedeclaredOnANestedElementIsClassifiedByTheDeclarationInScope()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:p="using:My.App">
              <p:Card Title="before" />
              <Grid xmlns:p="{_Android}">
                <p:Border />
              </Grid>
              <p:Card Title="after" />
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            (_, uri, _) => uri.BaseUri == _Android ? XamlPrefixDisposition.Drop : XamlPrefixDisposition.Leave,
            out XDocument document);

        Assert.AreEqual(1, result.DroppedElements);
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "Border"));
        CollectionAssert.AreEqual(
            new[] { "before", "after" },
            document.Descendants(XNamespace.Get("using:My.App") + "Card").Select(element => (string?)element.Attribute("Title")).ToArray());
    }

    [TestMethod]
    public void IgnorableStateIsThatOfTheElementWhereThePrefixIsUsed()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:mc="{_MarkupCompatibility}" xmlns:android="{_Android}" xmlns:ios="http://uno.ui/ios" mc:Ignorable="ios">
              <android:Border Tag="outside" />
              <ios:Border Tag="ios" />
              <Grid mc:Ignorable="android">
                <android:Border Tag="inside" />
              </Grid>
              <android:Border Tag="after" />
            </Page>
            """;

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            (_, _, isIgnorable) => isIgnorable ? XamlPrefixDisposition.Drop : XamlPrefixDisposition.Leave,
            out XDocument document);

        CollectionAssert.AreEqual(
            new[] { "outside", "after" },
            document.Descendants().Where(element => element.Name.LocalName == "Border").Select(element => (string?)element.Attribute("Tag")).ToArray());
        Assert.AreEqual(2, result.DroppedElements);
    }

    [TestMethod]
    public void NormalizedAttributeCanBeDroppedByTheFilter()
    {
        string xaml = $"""<Button xmlns="{_Presentation}" xmlns:win="{_Presentation}" win:Click="OnClick" win:Content="Go" Tag="t" />""";
        var seen = new List<string>();

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            Only("win", XamlPrefixDisposition.Keep),
            out XDocument document,
            options => options.ShouldDropNormalizedAttribute = (element, name, value) =>
            {
                seen.Add($"{element.Name.LocalName}.{name}={value}");
                return name == "Click";
            });

        // Only attributes that were normalized are offered to the filter, under their plain names.
        CollectionAssert.AreEqual(new[] { "Button.Click=OnClick", "Button.Content=Go" }, seen);
        CollectionAssert.AreEqual(new[] { "Content=Go", "Tag=t" }, PlainAttributes(document.Root!));
        Assert.AreEqual(1, result.DroppedAttributes);
        Assert.AreEqual(1, result.NormalizedMembers);
    }

    [TestMethod]
    public void AttributeOrderIsKept()
    {
        string xaml = $"""<TextBox xmlns="{_Presentation}" xmlns:win="{_Presentation}" xmlns:android="{_Android}" A="1" win:B="2" C="3" android:X="x" win:D="4" E="5" />""";

        Rewrite(
            xaml,
            prefix => prefix switch
            {
                "win" => XamlPrefixDisposition.Keep,
                "android" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document);

        CollectionAssert.AreEqual(new[] { "A=1", "B=2", "C=3", "D=4", "E=5" }, PlainAttributes(document.Root!));
        CollectionAssert.AreEqual(
            new[] { "xmlns", "win", "android" },
            document.Root!.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => attribute.Name.LocalName).ToArray());
    }

    [TestMethod]
    [DataRow("Text=\"plain\" c7:Text=\"conditional\"", "conditional")]
    [DataRow("c7:Text=\"conditional\" Text=\"plain\"", "plain")]
    public void LaterAttributeWinsWhenNormalizingSetsAMemberTwice(string attributes, string expected)
    {
        string xaml = $"""<TextBlock xmlns="{_Presentation}" xmlns:c7="{_Conditional}" {attributes} />""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("c7", XamlPrefixDisposition.Keep), out XDocument document);

        CollectionAssert.AreEqual(new[] { "Text=" + expected }, PlainAttributes(document.Root!));
        Assert.AreEqual(1, result.DroppedAttributes);
    }

    [TestMethod]
    public void AttachedPropertyKeepsItsNamespaceWhenTheDefaultNamespaceIsAnotherOne()
    {
        // Without a prefix, Grid.Row would be looked up in "using:My.App".
        string xaml = $"""<Card xmlns="using:My.App" xmlns:c7="{_Conditional}" c7:Grid.Row="1" c7:Opacity="0.5" />""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("c7", XamlPrefixDisposition.Keep), out XDocument document);

        Assert.AreEqual("1", (string?)document.Root!.Attribute(_P + "Grid.Row"));
        Assert.AreEqual("0.5", (string?)document.Root.Attribute("Opacity"));
        Assert.AreEqual(2, result.NormalizedMembers);
        StringAssert.Contains(document.ToString(SaveOptions.DisableFormatting), $"xmlns:c7=\"{_Presentation}\" c7:Grid.Row=\"1\" Opacity=\"0.5\"");
    }

    [TestMethod]
    public void DirectiveStaysInTheXamlLanguageNamespace()
    {
        string xaml = $"""<Grid xmlns="{_Presentation}" xmlns:x7="{_XamlLanguage}?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)" x7:Name="root" />""";

        XamlNamespaceRewriteResult result = Rewrite(xaml, Only("x7", XamlPrefixDisposition.Keep), out XDocument document);

        Assert.AreEqual("root", (string?)document.Root!.Attribute(_X + "Name"));
        Assert.IsNull(document.Root.Attribute("Name"));
        Assert.AreEqual(_XamlLanguage, (string?)document.Root.Attribute(XNamespace.Xmlns + "x7"));
        Assert.AreEqual(1, result.NormalizedMembers);
    }

    [TestMethod]
    public void FailingClassificationLeavesThePrefixAloneAndIsReported()
    {
        string xaml = $"""<Page xmlns="{_Presentation}" xmlns:android="{_Android}" xmlns:ios="http://uno.ui/ios"><android:Border /><ios:Border /></Page>""";
        var reported = new List<string>();

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix switch
            {
                "android" => throw new InvalidOperationException("no verdict"),
                "ios" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document,
            options => options.Report = reported.Add);

        CollectionAssert.AreEqual(
            new[] { XNamespace.Get(_Android) + "Border" },
            document.Root!.Elements().Select(element => element.Name).ToArray());
        Assert.AreEqual(1, result.DroppedElements);
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("'android'", StringComparison.Ordinal) && message.Contains("no verdict", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(result.Diagnostics.ToArray(), reported);
    }

    [TestMethod]
    public void FailingCallbacksDoNotStopTheRewrite()
    {
        string xaml = $"""<Button xmlns="{_Presentation}" xmlns:win="{_Presentation}" xmlns:android="{_Android}" win:Content="Go"><android:Border /></Button>""";

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            prefix => prefix switch
            {
                "win" => XamlPrefixDisposition.Keep,
                "android" => XamlPrefixDisposition.Drop,
                _ => XamlPrefixDisposition.Leave,
            },
            out XDocument document,
            options =>
            {
                options.ShouldDropNormalizedAttribute = (_, _, _) => throw new InvalidOperationException("filter failed");
                options.Report = _ => throw new InvalidOperationException("report failed");
            });

        // The attribute the filter could not judge is kept, and the walk goes on.
        CollectionAssert.AreEqual(new[] { "Content=Go" }, PlainAttributes(document.Root!));
        Assert.IsFalse(document.Root!.HasElements);
        Assert.AreEqual(1, result.DroppedElements);
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("filter failed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SourcePrefixesAreOnlyCapturedFromTheTextOfTheDocument()
    {
        string xaml = $"""<Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}"><win:Border win:Tag="1" /></Grid>""";

        Assert.IsTrue(XamlNamespaceRewriter.CaptureSourcePrefixes(XDocument.Parse(xaml), xaml));
        Assert.IsFalse(XamlNamespaceRewriter.CaptureSourcePrefixes(XDocument.Parse(xaml), xaml.Replace("win:Tag=\"1\"", string.Empty, StringComparison.Ordinal)));
        Assert.IsFalse(XamlNamespaceRewriter.CaptureSourcePrefixes(XDocument.Parse(xaml), xaml.Replace("<win:Border", "<Border /><win:Border", StringComparison.Ordinal)));
        Assert.IsFalse(XamlNamespaceRewriter.CaptureSourcePrefixes(XDocument.Parse(xaml), "<Grid"));
    }

    [TestMethod]
    public void TextOfAnotherDocumentIsNotUsedToTellPrefixesApart()
    {
        string xaml = $"""<Grid xmlns="{_Presentation}" xmlns:win="{_Presentation}"><win:Border /></Grid>""";

        XamlNamespaceRewriteResult result = Rewrite(
            xaml,
            Only("win", XamlPrefixDisposition.Drop),
            out XDocument document,
            options => options.SourceXaml = "<Other />");

        Assert.IsFalse(result.Changed);
        Assert.IsTrue(document.Root!.HasElements);
        Assert.AreEqual(1, result.Diagnostics.Count);
    }

    [TestMethod]
    public void DeeplyNestedDocumentIsWalkedWithoutRecursion()
    {
        const int depth = 5000;
        var xaml = new StringBuilder();
        xaml.Append("<Grid xmlns=\"").Append(_Presentation).Append("\" xmlns:android=\"").Append(_Android).Append("\">");
        for (int index = 0; index < depth; index++)
        {
            xaml.Append("<Grid>");
        }

        xaml.Append("<android:Border /><Border />");
        for (int index = 0; index < depth; index++)
        {
            xaml.Append("</Grid>");
        }

        xaml.Append("</Grid>");

        XamlNamespaceRewriteResult result = Rewrite(xaml.ToString(), Only("android", XamlPrefixDisposition.Drop), out XDocument document);

        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, document.Descendants().Count(element => element.Name.LocalName == "Border"));
    }

    private static Func<string, XamlPrefixDisposition> Only(string prefix, XamlPrefixDisposition disposition)
    {
        return candidate => candidate == prefix ? disposition : XamlPrefixDisposition.Leave;
    }

    private static XamlNamespaceRewriteResult Rewrite(
        string xaml,
        Func<string, XamlPrefixDisposition> classify,
        out XDocument document,
        Action<XamlNamespaceRewriteOptions>? configure = null)
    {
        return Rewrite(xaml, (prefix, _, _) => classify(prefix), out document, configure);
    }

    private static XamlNamespaceRewriteResult Rewrite(
        string xaml,
        Func<string, XamlNamespaceUri, bool, XamlPrefixDisposition> classify,
        out XDocument document,
        Action<XamlNamespaceRewriteOptions>? configure = null)
    {
        document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var options = new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            ClassifyOverride = (prefix, uri, isIgnorable) => classify(prefix, uri, isIgnorable),
        };
        configure?.Invoke(options);

        return XamlNamespaceRewriter.Rewrite(document, options);
    }

    /// <summary>
    /// Returns the attributes that are not namespace declarations as "Name=value", in document order.
    /// A name that is still in a namespace shows it, so a leftover prefix does not go unnoticed.
    /// </summary>
    private static string[] PlainAttributes(XElement element)
    {
        return element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
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
