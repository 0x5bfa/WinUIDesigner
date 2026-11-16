// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Rewrites prepared XAML the way the surface does when the user forces conditions: the overrides that arrive with
/// the design-time options come before the probes of the surface. The probes describe a system with API contract
/// versions up to 10 and an application <c>My.App</c> whose feature flag <c>NewExperience</c> is on.
/// </summary>
[TestClass]
public sealed class ConditionalXamlOverrideRewriteTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = "IsApiContractPresent(" + _Contract + ",7)";
    private const string _C99 = "IsApiContractPresent(" + _Contract + ",99)";
    private const string _NewExperienceKey = "using:My.App|FeatureFlagCondition(NewExperience)";
    private const string _LegacyModeKey = "using:My.App|FeatureFlagCondition(LegacyMode)";

    private static readonly XNamespace _P = _Presentation;

    // The shape of samples/WinUIDesignerSampleApp/ConditionalXamlPage.xaml.
    private const string _Page = $"""
        <Page xmlns="{_Presentation}"
              xmlns:cond="using:My.App"
              xmlns:c7n="{_Presentation}?IsApiContractNotPresent({_Contract},7)"
              xmlns:c7="{_Presentation}?IsApiContractPresent({_Contract},7)"
              xmlns:c99n="{_Presentation}?IsApiContractNotPresent({_Contract},99)"
              xmlns:c99="{_Presentation}?IsApiContractPresent({_Contract},99)"
              xmlns:legacyMode="{_Presentation}?cond:FeatureFlagCondition(LegacyMode)"
              xmlns:newExperience="{_Presentation}?cond:FeatureFlagCondition(NewExperience)">
          <StackPanel>
            <c7:TextBlock Text="7 present" />
            <c7n:TextBlock Text="7 not present" />
            <c99:TextBlock Text="99 present" />
            <c99n:TextBlock Text="99 not present" />
            <Border c99n:Background="Green" c99:Background="Red" />
            <newExperience:TextBlock Text="new experience" />
            <legacyMode:TextBlock Text="legacy mode" />
            <Button legacyMode:Content="classic editor" newExperience:Content="new editor" />
          </StackPanel>
        </Page>
        """;

    [TestMethod]
    public void WithoutOverridesTheProbesDecide()
    {
        XDocument document = Rewrite(_Page, XamlConditionOverrides.Empty, out Probes probes);

        CollectionAssert.AreEqual(new[] { "7 present", "99 not present", "new experience" }, Texts(document));
        Assert.AreEqual("Green", Attribute(document, "Border", "Background"));
        Assert.AreEqual("new editor", Attribute(document, "Button", "Content"));
        Assert.AreEqual(4, probes.Calls.Count);
    }

    [TestMethod]
    public void BranchThatHoldsIsForcedOff()
    {
        XDocument document = Rewrite(_Page, XamlConditionOverrides.Empty.With(_C7, holds: false), out Probes probes);

        CollectionAssert.AreEqual(new[] { "7 not present", "99 not present", "new experience" }, Texts(document));

        // The overridden question is not asked at all.
        Assert.IsFalse(probes.Calls.Any(call => call.Contains(" 7", StringComparison.Ordinal)), string.Join(", ", probes.Calls));
    }

    [TestMethod]
    public void BranchThatDoesNotHoldIsForcedOn()
    {
        XDocument document = Rewrite(_Page, XamlConditionOverrides.Empty.With(_C99, holds: true), out _);

        CollectionAssert.AreEqual(new[] { "7 present", "99 present", "new experience" }, Texts(document));
        Assert.AreEqual("Red", Attribute(document, "Border", "Background"), "The complementary attribute goes with its branch.");
    }

    [TestMethod]
    public void CustomConditionsAreForcedBothWays()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty
            .With(_NewExperienceKey, holds: false)
            .With(_LegacyModeKey, holds: true);

        XDocument document = Rewrite(_Page, overrides, out Probes probes);

        CollectionAssert.AreEqual(new[] { "7 present", "99 not present", "legacy mode" }, Texts(document));
        Assert.AreEqual("classic editor", Attribute(document, "Button", "Content"));
        Assert.IsFalse(probes.Calls.Any(call => call.StartsWith("custom", StringComparison.Ordinal)), string.Join(", ", probes.Calls));
    }

    [TestMethod]
    public void OverrideOfAnotherNamespaceDoesNotApply()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With("using:Other.App|FeatureFlagCondition(NewExperience)", holds: false);

        XDocument document = Rewrite(_Page, overrides, out _);

        CollectionAssert.Contains(Texts(document), "new experience");
    }

    [TestMethod]
    public void MalformedConditionStaysSkippedWhateverIsForced()
    {
        string xaml = $"""
            <StackPanel xmlns="{_Presentation}" xmlns:bad="{_Presentation}?IsApiContractPresent({_Contract},7.0)">
              <bad:TextBlock Text="malformed" />
              <TextBlock Text="always" />
            </StackPanel>
            """;
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With("!IsApiContractPresent(" + _Contract + ",7.0)", holds: true);

        XDocument document = Rewrite(xaml, overrides, out _);

        CollectionAssert.AreEqual(new[] { "always" }, Texts(document));
    }

    [TestMethod]
    public void OverridesOfTheCreateSurfaceRequestReachTheRewrite()
    {
        // What Visual Studio sends: the design-time options with the overrides, written into the 516 request.
        var sent = new DesignTimeOptions(
            DesignTimeTargetPlatform.Windows,
            contractVersion: null,
            XamlConditionOverrides.Empty.With(_C7, holds: false).With(_C99, holds: true).With(_LegacyModeKey, holds: true));

        Assert.IsTrue(DesignTimeOptions.TryParse(sent.Serialize(), out DesignTimeOptions received));
        XDocument document = Rewrite(_Page, received.ConditionOverrides, out _);

        CollectionAssert.AreEqual(new[] { "7 not present", "99 present", "new experience", "legacy mode" }, Texts(document));
        Assert.AreEqual("Red", Attribute(document, "Border", "Background"));
    }

    private static XDocument Rewrite(string xaml, XamlConditionOverrides overrides, out Probes probes)
    {
        probes = new Probes();
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

        // The composition SurfaceService.ConfigureConditionalNamespacePolicy uses.
        XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            EvaluateCondition = XamlConditionOverrides.CreateEvaluator(overrides, probes.Conditions.Evaluate),
        });

        return document;
    }

    private static string?[] Texts(XDocument document)
    {
        return document.Descendants(_P + "TextBlock").Select(element => (string?)element.Attribute("Text")).ToArray();
    }

    private static string? Attribute(XDocument document, string element, string attribute)
    {
        return (string?)document.Descendants(_P + element).Single().Attribute(attribute);
    }

    /// <summary>
    /// The system the documents are evaluated against, and a record of what was asked of it.
    /// </summary>
    private sealed class Probes
    {
        public Probes()
        {
            Conditions = new XamlConditionService(new XamlConditionProbeSet
            {
                IsApiContractPresent = (name, major, _) =>
                {
                    Calls.Add($"contract {major}");
                    return name == _Contract && major <= 10;
                },
                EvaluateCustom = (type, argument) =>
                {
                    Calls.Add($"custom {type}[{argument}]");
                    return type == "My.App.FeatureFlagCondition" ? argument == "NewExperience" : null;
                },
            });
        }

        public XamlConditionService Conditions { get; }

        public List<string> Calls { get; } = [];
    }
}
