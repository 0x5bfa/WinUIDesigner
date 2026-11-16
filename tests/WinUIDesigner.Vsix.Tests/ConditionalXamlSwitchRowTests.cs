// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

/// <summary>
/// Covers what the conditional XAML switches show for each question of a document: the value the preview uses,
/// whether it is overridden, which prefixes are shown, and the overrides of the project the document does not ask.
/// </summary>
[TestClass]
public sealed class ConditionalXamlSwitchRowTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = "IsApiContractPresent(" + _Contract + ",7)";
    private const string _C99 = "IsApiContractPresent(" + _Contract + ",99)";
    private const string _NewExperienceKey = "using:My.App|FeatureFlagCondition(NewExperience)";

    private const string _Page = $"""
        <Page xmlns="{_Presentation}"
              xmlns:cond="using:My.App"
              xmlns:c7n="{_Presentation}?IsApiContractNotPresent({_Contract},7)"
              xmlns:c7="{_Presentation}?IsApiContractPresent({_Contract},7)"
              xmlns:c99="{_Presentation}?IsApiContractPresent({_Contract},99)"
              xmlns:newExperience="{_Presentation}?cond:FeatureFlagCondition(NewExperience)"
              xmlns:broken="{_Presentation}?IsApiContractPresent({_Contract},7.0)" />
        """;

    [TestMethod]
    public void RowsShowTheEvaluatedValueUntilTheUserForcesOne()
    {
        IReadOnlyList<ConditionalXamlSwitchRow> rows = Rows(XamlConditionOverrides.Empty, Evaluate);

        CollectionAssert.AreEqual(new[] { _C7, _C99, "cond:FeatureFlagCondition(NewExperience)", $"IsApiContractPresent({_Contract},7.0)" }, rows.Select(row => row.Title).ToArray());
        CollectionAssert.AreEqual(new bool?[] { true, false, true, false }, rows.Select(row => row.Value).ToArray());
        Assert.IsTrue(rows.All(row => !row.IsOverridden && row.IsInDocument));

        Assert.AreEqual("c7n: hidden, c7: shown", rows[0].Branches);
        Assert.AreEqual("Evaluated: holds.", rows[0].Status);
        Assert.AreEqual("c99: hidden", rows[1].Branches);
        Assert.AreEqual("Evaluated: does not hold.", rows[1].Status);
        Assert.AreEqual("newExperience: shown", rows[2].Branches);
    }

    [TestMethod]
    public void OverriddenRowShowsBothAnswers()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(_C7, holds: false).With(_NewExperienceKey, holds: true);

        IReadOnlyList<ConditionalXamlSwitchRow> rows = Rows(overrides, Evaluate);

        ConditionalXamlSwitchRow contract = rows[0];
        Assert.IsTrue(contract.IsOverridden);
        Assert.AreEqual(false, contract.Value);
        Assert.AreEqual(false, contract.OverrideValue);
        Assert.AreEqual(true, contract.EvaluatedValue);
        Assert.AreEqual("c7n: shown, c7: hidden", contract.Branches);
        Assert.AreEqual("Overridden: does not hold. Evaluated: holds.", contract.Status);

        // Forcing the evaluated answer is still an override: it no longer follows the evaluation.
        ConditionalXamlSwitchRow flag = rows[2];
        Assert.IsTrue(flag.IsOverridden);
        Assert.AreEqual(true, flag.Value);
        Assert.AreEqual("Overridden: holds. Evaluated: holds.", flag.Status);
        Assert.IsFalse(rows[1].IsOverridden);
    }

    [TestMethod]
    public void UnknownValueIsShownAsSuch()
    {
        IReadOnlyList<ConditionalXamlSwitchRow> rows = Rows(XamlConditionOverrides.Empty.With(_C99, holds: true), evaluate: null);

        Assert.IsNull(rows[0].Value);
        Assert.AreEqual("Not evaluated yet.", rows[0].Status);
        Assert.AreEqual("c7n: shown when it does not hold, c7: shown when it holds", rows[0].Branches);
        Assert.AreEqual(true, rows[1].Value);
        Assert.AreEqual("Overridden: holds. Evaluated: not known yet.", rows[1].Status);
        Assert.AreEqual("c99: shown", rows[1].Branches);
    }

    [TestMethod]
    public void MalformedRowCannotBeSwitched()
    {
        ConditionalXamlSwitchRow row = Rows(XamlConditionOverrides.Empty, Evaluate)[3];

        Assert.IsFalse(row.IsSwitchable);
        Assert.AreEqual(false, row.Value);
        Assert.AreEqual("broken: skipped", row.Branches);
        StringAssert.StartsWith(row.Status, "Cannot be switched: '7.0' is not a contract version");
        StringAssert.EndsWith(row.Status, "Its members are skipped.");
    }

    [TestMethod]
    public void OverridesTheDocumentDoesNotAskFollowItsQuestions()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty
            .With("using:Other|Flag(B)", holds: false)
            .With(_C7, holds: true)
            .With("IsTypePresent(A)", holds: true);

        IReadOnlyList<ConditionalXamlSwitchRow> rows = Rows(overrides, Evaluate);

        CollectionAssert.AreEqual(
            new[] { _C7, _C99, "cond:FeatureFlagCondition(NewExperience)", $"IsApiContractPresent({_Contract},7.0)", "IsTypePresent(A)", "using:Other|Flag(B)" },
            rows.Select(row => row.Title).ToArray());
        ConditionalXamlSwitchRow other = rows[5];
        Assert.IsFalse(other.IsInDocument);
        Assert.IsTrue(other.IsSwitchable);
        Assert.AreEqual(false, other.Value);
        Assert.AreEqual(string.Empty, other.Branches);
        Assert.AreEqual("Overridden: does not hold. This document does not use it.", other.Status);
    }

    [TestMethod]
    public void FailingEvaluationShowsAnUnknownValue()
    {
        IReadOnlyList<ConditionalXamlSwitchRow> rows = Rows(XamlConditionOverrides.Empty, _ => throw new InvalidOperationException("the surface is gone"));

        Assert.IsNull(rows[0].Value);
        Assert.AreEqual("Not evaluated yet.", rows[0].Status);
    }

    [TestMethod]
    [DataRow(0, "Conditions", "No condition is overridden in this project.")]
    [DataRow(1, "Conditions (1 overridden)", "1 condition is overridden in this project.")]
    [DataRow(3, "Conditions (3 overridden)", "3 conditions are overridden in this project.")]
    public void ButtonAndSummarySayHowManyConditionsAreOverridden(int count, string label, string summary)
    {
        Assert.AreEqual(label, ConditionalXamlSwitchRow.FormatButtonLabel(count));
        Assert.AreEqual(summary, ConditionalXamlSwitchRow.FormatOverrideSummary(count));
    }

    private static IReadOnlyList<ConditionalXamlSwitchRow> Rows(XamlConditionOverrides overrides, Func<XamlConditionQuery, bool?>? evaluate)
    {
        IReadOnlyList<XamlConditionSwitch> switches = XamlConditionSwitches.Collect(XamlNamespaceScanner.Scan(_Page));
        return ConditionalXamlSwitchRow.Create(switches, overrides, evaluate);
    }

    // The surface's answers: contract versions up to 10, the feature flag on. Only positive questions are asked.
    private static bool? Evaluate(XamlConditionQuery query)
    {
        Assert.IsFalse(query.Condition.IsNegated, "A switch shows the answer to the positive form of its question.");
        return query.Condition.Kind switch
        {
            XamlConditionKind.ApiContract => query.Condition.MajorVersion <= 10,
            XamlConditionKind.Custom => query.Condition.Argument == "NewExperience",
            _ => null,
        };
    }
}
