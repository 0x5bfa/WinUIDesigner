// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Rewrites documents the way the surface does: with the real namespace policies and a condition service.
/// Only the probes are fakes. They describe a system with API contract versions up to 10, the Windows App SDK
/// types of a few examples, and an application <c>My.App</c> with a feature-flag condition.
/// </summary>
[TestClass]
public sealed class ConditionalXamlRewriteTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = _Presentation + "?IsApiContractPresent(" + _Contract + ",7)";
    private const string _C7N = _Presentation + "?IsApiContractNotPresent(" + _Contract + ",7)";
    private const string _C99 = _Presentation + "?IsApiContractPresent(" + _Contract + ",99)";
    private const string _C99N = _Presentation + "?IsApiContractNotPresent(" + _Contract + ",99)";

    private static readonly XNamespace _P = _Presentation;
    private static readonly XNamespace _X = _XamlLanguage;

    [TestMethod]
    public void ElementIsKeptAsAPlainElementOrRemovedWithItsCondition()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c99="{_C99}">
              <c7:TextBlock Text="new" />
              <c99:TextBlock Text="future"><c99:TextBlock.Tag>x</c99:TextBlock.Tag></c99:TextBlock>
              <TextBlock Text="always" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        CollectionAssert.AreEqual(new[] { "new", "always" }, Texts(document));
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(0, result.DroppedAttributes);
        Assert.AreEqual(1, result.NormalizedMembers);
        Assert.AreEqual(2, result.RepointedDeclarations);
        AssertNoConditionIsLeft(document);
        Assert.AreEqual(0, probes.Reports.Count);
    }

    [TestMethod]
    public void AttributeIsKeptAsAPlainAttributeOrRemovedWithItsCondition()
    {
        string xaml = $"""<TextBlock xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c99="{_C99}" c7:Text="new" c99:Tag="future" FontSize="12" />""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        CollectionAssert.AreEqual(new[] { "Text=new", "FontSize=12" }, PlainAttributes(document.Root!));
        Assert.AreEqual(1, result.DroppedAttributes);
        Assert.AreEqual(1, result.NormalizedMembers);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    [DataRow("c7:CornerRadius=\"4\" c7n:CornerRadius=\"0\"", "c7n:CornerRadius=\"0\" c7:CornerRadius=\"4\"", "CornerRadius=4")]
    [DataRow("c99:Background=\"Red\" c99n:Background=\"Green\"", "c99n:Background=\"Green\" c99:Background=\"Red\"", "Background=Green")]
    public void ComplementaryAttributesGiveTheBranchThatHoldsInEitherOrder(string trueOrFalseFirst, string otherOrder, string expected)
    {
        string declarations = $"xmlns=\"{_Presentation}\" xmlns:c7=\"{_C7}\" xmlns:c7n=\"{_C7N}\" xmlns:c99=\"{_C99}\" xmlns:c99n=\"{_C99N}\"";

        XDocument first = Rewrite($"<Grid {declarations}><Border {trueOrFalseFirst} /></Grid>", out XamlNamespaceRewriteResult firstResult, out _);
        XDocument second = Rewrite($"<Grid {declarations}><Border {otherOrder} /></Grid>", out XamlNamespaceRewriteResult secondResult, out _);

        CollectionAssert.AreEqual(new[] { expected }, PlainAttributes(first.Root!.Elements().Single()));
        Assert.AreEqual(first.ToString(SaveOptions.DisableFormatting), second.ToString(SaveOptions.DisableFormatting));

        // One attribute goes, one becomes plain, and nothing is reported as set twice.
        foreach (XamlNamespaceRewriteResult result in new[] { firstResult, secondResult })
        {
            Assert.AreEqual(1, result.DroppedAttributes);
            Assert.AreEqual(1, result.NormalizedMembers);
            Assert.IsFalse(result.Diagnostics.Any(message => message.Contains("twice", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    [DataRow("Text=\"plain\" c7:Text=\"new\"", "new", true)]
    [DataRow("c7:Text=\"new\" Text=\"plain\"", "plain", true)]
    [DataRow("Text=\"plain\" c99:Text=\"future\"", "plain", false)]
    [DataRow("c99:Text=\"future\" Text=\"plain\"", "plain", false)]
    public void PlainAttributeAndConditionalAttributeOfOneProperty(string attributes, string expected, bool isSetTwice)
    {
        string xaml = $"""<TextBlock xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c99="{_C99}" {attributes} />""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        CollectionAssert.AreEqual(new[] { "Text=" + expected }, PlainAttributes(document.Root!));
        Assert.AreEqual(1, result.DroppedAttributes);

        // Both apply only when the condition holds; then the later one is used and the clash is reported.
        Assert.AreEqual(isSetTwice, result.Diagnostics.Any(message => message.Contains("Text", StringComparison.Ordinal) && message.Contains("twice", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TwoConditionsThatHoldOnOnePropertyGiveTheLaterValueAndAWarning()
    {
        string c6 = _Presentation + "?IsApiContractPresent(" + _Contract + ",6)";
        string xaml = $"""<Border xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c6="{c6}" c7:CornerRadius="4" Tag="t" c6:CornerRadius="1" />""";

        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var reports = new List<string>();
        var warnings = new List<string>();

        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            EvaluateCondition = new Probes().Conditions.Evaluate,
            Report = reports.Add,
            ReportWarning = warnings.Add,
        });

        CollectionAssert.AreEqual(new[] { "Tag=t", "CornerRadius=1" }, PlainAttributes(document.Root!));
        Assert.AreEqual(1, result.DroppedAttributes);

        // The reader refuses such markup, so it is a warning and not one of the routine messages.
        Assert.AreEqual(1, warnings.Count);
        StringAssert.Contains(warnings[0], "CornerRadius");
        StringAssert.Contains(warnings[0], "set twice");
        StringAssert.Contains(warnings[0], "line 1");
        Assert.IsFalse(reports.Any(message => message.Contains("twice", StringComparison.Ordinal)));
        CollectionAssert.Contains(result.Diagnostics.ToArray(), warnings[0]);
    }

    [TestMethod]
    public void WarningGoesToTheRoutineMessagesWhenNobodyListensForWarnings()
    {
        string xaml = $"""<TextBlock xmlns="{_Presentation}" xmlns:c7="{_C7}" Text="plain" c7:Text="new" />""";
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var reports = new List<string>();

        XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            EvaluateCondition = new Probes().Conditions.Evaluate,
            Report = reports.Add,
        });

        Assert.AreEqual(1, reports.Count(message => message.Contains("Text", StringComparison.Ordinal) && message.Contains("set twice", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PropertyElementFollowsItsCondition()
    {
        string xaml = $"""
            <Border xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c7n="{_C7N}">
              <c7n:Border.Background><SolidColorBrush Color="Blue" /></c7n:Border.Background>
              <c7:Border.Background><SolidColorBrush Color="Red" /></c7:Border.Background>
            </Border>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        XElement background = document.Root!.Elements().Single();
        Assert.AreEqual(_P + "Border.Background", background.Name);
        Assert.AreEqual("Red", (string?)background.Elements(_P + "SolidColorBrush").Single().Attribute("Color"));
        Assert.AreEqual(1, result.DroppedElements);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void ConditionOnAClrNamespaceKeepsTheMembersInThatNamespace()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}"
                        xmlns:local7="using:My.App?IsApiContractPresent({_Contract},7)"
                        xmlns:local99="using:My.App?IsApiContractPresent({_Contract},99)">
              <local7:Card local7:Title="new" local7:Grid.Row="1" Tag="t" />
              <local99:Card Title="future" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        XNamespace app = "using:My.App";
        XElement card = document.Root!.Elements().Single();
        Assert.AreEqual(app + "Card", card.Name);

        // A plain property belongs to its element. An attached property keeps naming the namespace of its owner.
        Assert.AreEqual("new", (string?)card.Attribute("Title"));
        Assert.AreEqual("1", (string?)card.Attribute(app + "Grid.Row"));
        Assert.AreEqual("t", (string?)card.Attribute("Tag"));

        Assert.AreEqual("using:My.App", (string?)document.Root.Attribute(XNamespace.Xmlns + "local7"));
        Assert.AreEqual("using:My.App", (string?)document.Root.Attribute(XNamespace.Xmlns + "local99"));
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(2, result.RepointedDeclarations);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    [DataRow(4, 1)]
    [DataRow(99, 0)]
    public void XamlControlsResourcesInMergedDictionariesFollowsItsCondition(int contractVersion, int expectedDictionaries)
    {
        string xaml = $"""
            <ResourceDictionary xmlns="{_Presentation}"
                                xmlns:x="{_XamlLanguage}"
                                xmlns:contractPresent="{_Presentation}?IsApiContractPresent({_Contract},{contractVersion})">
              <ResourceDictionary.MergedDictionaries>
                <contractPresent:XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
              </ResourceDictionary.MergedDictionaries>
              <SolidColorBrush x:Key="Accent" Color="Red" />
            </ResourceDictionary>
            """;

        XDocument document = Rewrite(xaml, out _, out _);

        // What the XAML reader is given is the serialized document, so that is what has to be right.
        XDocument reloaded = Reload(document);
        XElement merged = reloaded.Root!.Element(_P + "ResourceDictionary.MergedDictionaries")!;
        Assert.AreEqual(expectedDictionaries, merged.Elements().Count());
        if (expectedDictionaries == 1)
        {
            // The element is in the presentation namespace although it redeclares the default namespace for itself.
            XElement resources = merged.Elements().Single();
            Assert.AreEqual(_P + "XamlControlsResources", resources.Name);
            Assert.AreEqual("using:Microsoft.UI.Xaml.Controls", (string?)resources.Attribute("xmlns"));
        }

        Assert.AreEqual("Red", (string?)reloaded.Root.Element(_P + "SolidColorBrush")!.Attribute("Color"));
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void SettersFollowTheirConditions()
    {
        string xaml = $"""
            <Style xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c7n="{_C7N}" xmlns:c99="{_C99}" TargetType="Border">
              <Setter Property="Padding" Value="4" />
              <c7:Setter Property="CornerRadius" Value="8" />
              <c7n:Setter Property="CornerRadius" Value="0" />
              <c99:Setter Property="BackgroundSizing" Value="OuterBorderEdge" />
            </Style>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        CollectionAssert.AreEqual(
            new[] { "Padding=4", "CornerRadius=8" },
            document.Root!.Elements(_P + "Setter").Select(setter => $"{(string?)setter.Attribute("Property")}={(string?)setter.Attribute("Value")}").ToArray());
        Assert.AreEqual(2, result.DroppedElements);
        Assert.AreEqual(2, document.Root.Elements().Count());
    }

    [TestMethod]
    public void NestedDeclarationShadowsThePrefixOfAnOuterOne()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:c="{_C7}">
              <Grid xmlns:c="{_C99}">
                <c:Border Tag="inner" />
              </Grid>
              <c:Border Tag="outer" />
              <Grid xmlns:c="{_C99N}">
                <c:Border Tag="inner again" />
              </Grid>
              <c:Border Tag="outer again" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        CollectionAssert.AreEqual(
            new[] { "outer", "inner again", "outer again" },
            document.Descendants(_P + "Border").Select(border => (string?)border.Attribute("Tag")).ToArray());
        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(3, result.RepointedDeclarations);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void PrefixUsedInAnAttributeValueStillResolves()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:c7="{_C7}" xmlns:c99="{_C99}">
              <Page.Resources>
                <Style x:Key="Present" TargetType="c7:Button" />
                <Style x:Key="Absent" TargetType="c99:Button" />
              </Page.Resources>
            </Page>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        // The declarations stay, bound to the bare namespace: the XAML reader resolves "c99:Button"
        // whatever the condition says, because a condition applies to members, not to type names.
        XDocument reloaded = Reload(document);
        XElement[] styles = reloaded.Descendants(_P + "Style").ToArray();
        CollectionAssert.AreEqual(new[] { "c7:Button", "c99:Button" }, styles.Select(style => (string?)style.Attribute("TargetType")).ToArray());
        Assert.AreEqual(_P, styles[0].GetNamespaceOfPrefix("c7"));
        Assert.AreEqual(_P, styles[1].GetNamespaceOfPrefix("c99"));
        Assert.AreEqual(2, result.RepointedDeclarations);
        Assert.AreEqual(0, result.DroppedElements);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void CustomConditionDecidesElementsAttributesAndSetters()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}"
                        xmlns:cond="using:My.App"
                        xmlns:newExp="{_Presentation}?cond:FeatureFlagCondition(NewExperience)"
                        xmlns:legacy="{_Presentation}?cond:FeatureFlagCondition(LegacyMode)">
              <StackPanel.Resources>
                <Style TargetType="TextBlock">
                  <newExp:Setter Property="FontSize" Value="28" />
                  <legacy:Setter Property="FontSize" Value="18" />
                </Style>
              </StackPanel.Resources>
              <newExp:InfoBar Title="New experience" />
              <legacy:InfoBar Title="Legacy mode" />
              <Button legacy:Content="Open the classic editor" newExp:Content="Open the new editor" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        CollectionAssert.AreEqual(new[] { "New experience" }, document.Descendants(_P + "InfoBar").Select(bar => (string?)bar.Attribute("Title")).ToArray());
        CollectionAssert.AreEqual(new[] { "Content=Open the new editor" }, PlainAttributes(document.Descendants(_P + "Button").Single()));
        CollectionAssert.AreEqual(new[] { "28" }, document.Descendants(_P + "Setter").Select(setter => (string?)setter.Attribute("Value")).ToArray());
        Assert.AreEqual(2, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);

        // Each condition was evaluated once, with the argument as written; the declaration of "cond" is untouched.
        CollectionAssert.AreEquivalent(
            new[] { "custom My.App.FeatureFlagCondition[NewExperience]", "custom My.App.FeatureFlagCondition[LegacyMode]" },
            probes.Calls);
        Assert.AreEqual("using:My.App", (string?)document.Root!.Attribute(XNamespace.Xmlns + "cond"));
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    [DataRow("cond:NoSuchCondition(X)", "My.App.NoSuchCondition")]
    [DataRow("cond:BrokenCondition(X)", "the flags are not loaded")]
    [DataRow("nope:FeatureFlagCondition(NewExperience)", "prefix 'nope'")]
    [DataRow("IsFooPresent(X)", "IsFooPresent")]
    public void CustomConditionThatCannotBeEvaluatedCountsAsNotSatisfiedAndNeverReachesTheXamlReader(string condition, string expectedReport)
    {
        // The XAML reader ends the process on such a namespace, some of them even when the prefix is not used.
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:cond="using:My.App" xmlns:bad="{_Presentation}?{condition}" xmlns:unused="{_Presentation}?{condition}">
              <bad:Border />
              <TextBlock bad:Text="x" Tag="kept" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        CollectionAssert.AreEqual(new[] { _P + "TextBlock" }, document.Root!.Elements().Select(element => element.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Tag=kept" }, PlainAttributes(document.Root.Elements().Single()));
        Assert.AreEqual(_Presentation, (string?)document.Root.Attribute(XNamespace.Xmlns + "bad"));
        Assert.AreEqual(_Presentation, (string?)document.Root.Attribute(XNamespace.Xmlns + "unused"));
        Assert.AreEqual(2, result.RepointedDeclarations);
        AssertNoConditionIsLeft(document);

        // Reported once, although two prefixes carry the condition.
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], expectedReport);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,abc)")]
    [DataRow("IsApiContractPresent (Windows.Foundation.UniversalApiContract,7)")]
    [DataRow("IsTypePresent(A,B)")]
    public void MalformedConditionCountsAsNotSatisfiedAndNeverReachesTheXamlReader(string condition)
    {
        string xaml = $"""<StackPanel xmlns="{_Presentation}" xmlns:bad="{_Presentation}?{condition}"><bad:Border /><TextBlock bad:Text="x" /></StackPanel>""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        Assert.AreEqual(1, result.DroppedElements);
        Assert.AreEqual(1, result.DroppedAttributes);
        Assert.AreEqual(_Presentation, (string?)document.Root!.Attribute(XNamespace.Xmlns + "bad"));
        AssertNoConditionIsLeft(document);
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], condition);
    }

    [TestMethod]
    public void ConditionalNamespaceWithoutMembersIsStillDecided()
    {
        // The XAML reader interprets the condition of a declaration whether its prefix is used or not.
        string xaml = $"""<StackPanel xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c99="{_C99}"><TextBlock /></StackPanel>""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(2, result.RepointedDeclarations);
        Assert.AreEqual(0, result.DroppedElements + result.DroppedAttributes + result.NormalizedMembers);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void NamespaceWithAnEmptyConditionIsUnconditional()
    {
        string xaml = $"""<StackPanel xmlns="{_Presentation}" xmlns:e="{_Presentation}?"><e:Border e:Tag="t" /></StackPanel>""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        XElement border = document.Root!.Elements().Single();
        Assert.AreEqual(_P + "Border", border.Name);
        CollectionAssert.AreEqual(new[] { "Tag=t" }, PlainAttributes(border));
        Assert.AreEqual(0, probes.Calls.Count);
        Assert.AreEqual(0, probes.Reports.Count);
        Assert.AreEqual(1, result.RepointedDeclarations);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void EvaluatorThatThrowsLeavesTheNamespaceAloneAndIsReported()
    {
        string xaml = $"""<StackPanel xmlns="{_Presentation}" xmlns:c7="{_C7}"><c7:Border c7:Tag="t" /></StackPanel>""";
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        string before = document.ToString(SaveOptions.DisableFormatting);
        var reported = new List<string>();

        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            EvaluateCondition = (_, _) => throw new InvalidOperationException("the evaluator failed"),
            Report = reported.Add,
        });

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
        Assert.IsTrue(result.Diagnostics.Any(message => message.Contains("'c7'", StringComparison.Ordinal) && message.Contains("the evaluator failed", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(result.Diagnostics.ToArray(), reported);
    }

    [TestMethod]
    public void DuplicateKeyAcrossComplementaryBranchesLeavesOneEntry()
    {
        string xaml = $"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:c7="{_C7}" xmlns:c7n="{_C7N}">
              <c7n:SolidColorBrush x:Key="Accent" Color="Blue" />
              <c7:SolidColorBrush x:Key="Accent" Color="Red" />
              <c7:Style x:Key="Card" TargetType="Border" />
              <c7n:Style x:Key="Card" TargetType="Grid" />
            </ResourceDictionary>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        CollectionAssert.AreEqual(
            new[] { "SolidColorBrush Accent Red", "Style Card Border" },
            document.Root!.Elements().Select(element => $"{element.Name.LocalName} {(string?)element.Attribute(_X + "Key")} {(string?)element.Attribute("Color") ?? (string?)element.Attribute("TargetType")}").ToArray());
        Assert.IsTrue(document.Root.Elements().All(element => element.Name.Namespace == _P));
        Assert.AreEqual(2, result.DroppedElements);
    }

    [TestMethod]
    [DataRow("c99", false)]
    [DataRow("c7", true)]
    public void RootUnderAConditionalPrefixIsKeptAsAPlainElement(string prefix, bool conditionHolds)
    {
        // The XAML reader cannot load a root under a conditional prefix at all, so the root always becomes plain.
        string xaml = $"""<{prefix}:Page xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c99="{_C99}" {prefix}:Tag="t" Width="5"><TextBlock c99:Text="future" /></{prefix}:Page>""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        Assert.AreEqual(_P + "Page", document.Root!.Name);
        Assert.AreEqual(0, result.DroppedElements);
        Assert.AreEqual(1, document.Root.Elements(_P + "TextBlock").Count());
        Assert.AreEqual(0, PlainAttributes(document.Root.Elements().Single()).Length);

        // An attribute of the root is an ordinary member: it follows the condition.
        CollectionAssert.AreEqual(conditionHolds ? new[] { "Tag=t", "Width=5" } : new[] { "Width=5" }, PlainAttributes(document.Root));
        Assert.AreEqual(!conditionHolds, result.Diagnostics.Any(message => message.Contains("root", StringComparison.OrdinalIgnoreCase)));

        // The serialized document has one root in the presentation namespace and can be read back.
        Assert.AreEqual(_P + "Page", Reload(document).Root!.Name);
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void LineInformationOfSurvivingElementsIsKept()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}"
                  xmlns:c7="{_C7}"
                  xmlns:c99="{_C99}">
              <c99:Border>
                <TextBlock Text="gone" />
              </c99:Border>
              <c7:Grid>
                <TextBlock c7:Text="a" c99:Tag="b" />
                <c99:TextBlock />
                <Button />
              </c7:Grid>
            </Page>
            """;
        (string Name, int Line, int Position)[] expected =
        [
            ("Page", 1, 2),
            ("Grid", 7, 4),
            ("TextBlock", 8, 6),
            ("Button", 10, 6),
        ];

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        Assert.IsTrue(result.Changed);
        CollectionAssert.AreEqual(expected, document.Descendants().Select(LineInfo).ToArray());
    }

    [TestMethod]
    public void AttributeKeepsAPrefixWhereNoDefaultNamespaceIsDeclared()
    {
        // The XAML reader ends the process on an attribute without a prefix in a document without a default namespace.
        string xaml = $"""<p:StackPanel xmlns:p="{_Presentation}" xmlns:c7="{_C7}"><p:TextBlock c7:Tag="yes" c7:Grid.Row="1" /></p:StackPanel>""";

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out _);

        XElement textBlock = document.Root!.Elements().Single();
        CollectionAssert.AreEqual(new[] { _P + "Tag", _P + "Grid.Row" }, textBlock.Attributes().Select(attribute => attribute.Name).ToArray());
        Assert.AreEqual(2, result.NormalizedMembers);

        XElement reloaded = Reload(document).Root!.Elements().Single();
        Assert.AreEqual("yes", (string?)reloaded.Attribute(_P + "Tag"));
        Assert.IsNull(reloaded.Attribute("Tag"));
        AssertNoConditionIsLeft(document);
    }

    [TestMethod]
    public void TypeAndPropertyConditionsAreEvaluated()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}"
                        xmlns:picker="{_Presentation}?IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)"
                        xmlns:noPicker="{_Presentation}?IsTypeNotPresent(Microsoft.UI.Xaml.Controls.ColorPicker)"
                        xmlns:uwp="{_Presentation}?IsTypePresent(Windows.UI.Xaml.Controls.ColorPicker)"
                        xmlns:radius="{_Presentation}?IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control, CornerRadius)"
                        xmlns:noSuch="{_Presentation}?IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control,NoSuchProperty)">
              <picker:ColorPicker />
              <noPicker:ComboBox />
              <uwp:TextBlock Text="the UWP type is not in this system" />
              <Button radius:CornerRadius="4" noSuch:Tag="x" />
            </StackPanel>
            """;

        XDocument document = Rewrite(xaml, out _, out Probes probes);

        CollectionAssert.AreEqual(new[] { "ColorPicker", "Button" }, document.Root!.Elements().Select(element => element.Name.LocalName).ToArray());
        CollectionAssert.AreEqual(new[] { "CornerRadius=4" }, PlainAttributes(document.Root.Elements().Last()));

        // "Windows." types are asked of the operating system, the others of the application's assemblies.
        CollectionAssert.AreEquivalent(
            new[]
            {
                "managed type Microsoft.UI.Xaml.Controls.ColorPicker",
                "windows type Windows.UI.Xaml.Controls.ColorPicker",
                "managed property Microsoft.UI.Xaml.Controls.Control.CornerRadius",
                "managed property Microsoft.UI.Xaml.Controls.Control.NoSuchProperty",
            },
            probes.Calls);
    }

    [TestMethod]
    public void ConditionIsProbedOnceHoweverOftenItIsUsed()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:c7="{_C7}" xmlns:c7n="{_C7N}" xmlns:again="{_C7}">
              <c7:Border c7:Tag="1" /><c7n:Border /><again:Border again:Tag="2" /><c7:Border />
              <Grid xmlns:c7="{_Presentation}?IsApiContractPresent( {_Contract} , 7 )"><c7:Border /></Grid>
            </StackPanel>
            """;
        var probes = new Probes();

        XDocument first = Rewrite(xaml, out _, probes);
        XDocument second = Rewrite(xaml, out _, probes);

        Assert.AreEqual(4, first.Descendants(_P + "Border").Count());
        Assert.AreEqual(first.ToString(SaveOptions.DisableFormatting), second.ToString(SaveOptions.DisableFormatting));
        CollectionAssert.AreEqual(new[] { $"contract {_Contract} 7" }, probes.Calls);
    }

    [TestMethod]
    public void PreparedPageOfVisualStudioIsRewrittenToWhatTheConditionsSelect()
    {
        // What Visual Studio's cleaner writes for a page with conditional members: the declarations and the
        // prefixed members are passed through, and only members without a prefix get design-time replacements.
        string xaml = $"""
            <Page xmlns:_0="using:XSurfUwp"
                xmlns="{_Presentation}"
                xmlns:x="{_XamlLanguage}"
                xmlns:d="using:XSurfUwp"
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:c7="{_C7}"
                xmlns:c7n="{_C7N}"
                xmlns:c99="{_C99}"
                xmlns:c99n="{_C99N}"
                xmlns:prim7="using:Microsoft.UI.Xaml.Controls.Primitives?IsApiContractPresent({_Contract},7)"
                mc:Ignorable="d">
                <Page.Resources>
                    <Style x:Key="ConditionalStyle" TargetType="Border">
                        <c7:Setter Property="BorderThickness" Value="2" />
                        <c99:Setter Property="BorderThickness" Value="9" />
                    </Style>
                </Page.Resources>
                <StackPanel _0:DT.CustomTag="">
                    <c7:TextBlock Text="c7" />
                    <c99:TextBlock Text="c99" />
                    <c7n:TextBlock Text="c7n" />
                    <c99n:TextBlock Text="c99n" />
                    <Border _0:DT.RuntimeHeight="20" c99n:Background="Green" c99:Background="Red" />
                    <Border _0:DT.RuntimeHeight="20" c99:Background="Red" c99n:Background="Green" />
                    <prim7:ToggleButton Content="prim7" />
                    <Border _0:DT.RuntimeHeight="20">
                        <c99:Border.Background>
                            <SolidColorBrush Color="Red" />
                        </c99:Border.Background>
                    </Border>
                </StackPanel>
            </Page>
            """;

        XDocument document = Rewrite(xaml, out XamlNamespaceRewriteResult result, out Probes probes);

        XNamespace designTime = "using:XSurfUwp";
        XNamespace primitives = "using:Microsoft.UI.Xaml.Controls.Primitives";
        XElement panel = document.Root!.Element(_P + "StackPanel")!;
        CollectionAssert.AreEqual(
            new[] { _P + "TextBlock", _P + "TextBlock", _P + "Border", _P + "Border", primitives + "ToggleButton", _P + "Border" },
            panel.Elements().Select(element => element.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "c7", "c99n" }, Texts(document));

        // Both borders get the branch that holds, whichever attribute comes first, and keep their design-time attribute.
        XElement[] borders = panel.Elements(_P + "Border").ToArray();
        foreach (XElement border in borders.Take(2))
        {
            Assert.AreEqual("Green", (string?)border.Attribute("Background"));
            Assert.AreEqual("20", (string?)border.Attribute(designTime + "DT.RuntimeHeight"));
            Assert.AreEqual(2, border.Attributes().Count());
        }

        Assert.IsFalse(borders[2].HasElements);
        CollectionAssert.AreEqual(new[] { "2" }, document.Descendants(_P + "Setter").Select(setter => (string?)setter.Attribute("Value")).ToArray());

        Assert.AreEqual(4, result.DroppedElements);
        Assert.AreEqual(2, result.DroppedAttributes);
        Assert.AreEqual(5, result.RepointedDeclarations);
        Assert.AreEqual(0, probes.Reports.Count);
        AssertNoConditionIsLeft(document);
        Assert.AreEqual(_P + "Page", Reload(document).Root!.Name);
    }

    private static XDocument Rewrite(string xaml, out XamlNamespaceRewriteResult result, out Probes probes)
    {
        probes = new Probes();
        return Rewrite(xaml, out result, probes);
    }

    private static XDocument Rewrite(string xaml, out XamlNamespaceRewriteResult result, Probes probes)
    {
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        result = XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            EvaluateCondition = probes.Conditions.Evaluate,
        });

        return document;
    }

    /// <summary>
    /// Returns the document as the XAML reader gets it: serialized and parsed again.
    /// </summary>
    private static XDocument Reload(XDocument document)
    {
        return XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
    }

    private static void AssertNoConditionIsLeft(XDocument document)
    {
        string text = document.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(text.Contains('?'), text);
        Assert.IsFalse(XamlNamespaceScanner.Scan(text).HasConditionalNamespaces, text);
    }

    private static string?[] Texts(XDocument document)
    {
        return document.Descendants(_P + "TextBlock").Select(element => (string?)element.Attribute("Text")).ToArray();
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

    /// <summary>
    /// The system the documents are evaluated against, and a record of what was asked of it.
    /// </summary>
    private sealed class Probes
    {
        public Probes()
        {
            Conditions = new XamlConditionService(
                new XamlConditionProbeSet
                {
                    IsApiContractPresent = (name, major, _) =>
                    {
                        Calls.Add($"contract {name} {major}");
                        return name == _Contract && major <= 10;
                    },
                    IsWindowsTypePresent = name =>
                    {
                        Calls.Add("windows type " + name);
                        return name == "Windows.Foundation.Uri";
                    },
                    IsWindowsPropertyPresent = (type, property) =>
                    {
                        Calls.Add($"windows property {type}.{property}");
                        return false;
                    },
                    IsManagedTypePresent = name =>
                    {
                        Calls.Add("managed type " + name);
                        return name == "Microsoft.UI.Xaml.Controls.ColorPicker";
                    },
                    IsManagedPropertyPresent = (type, property) =>
                    {
                        Calls.Add($"managed property {type}.{property}");
                        return type == "Microsoft.UI.Xaml.Controls.Control" && property == "CornerRadius";
                    },
                    EvaluateCustom = (type, argument) =>
                    {
                        Calls.Add($"custom {type}[{argument}]");
                        return type switch
                        {
                            "My.App.FeatureFlagCondition" => argument == "NewExperience",
                            "My.App.BrokenCondition" => throw new InvalidOperationException("the flags are not loaded"),
                            _ => null,
                        };
                    },
                },
                Reports.Add);
        }

        public XamlConditionService Conditions { get; }

        public List<string> Calls { get; } = [];

        public List<string> Reports { get; } = [];
    }
}
