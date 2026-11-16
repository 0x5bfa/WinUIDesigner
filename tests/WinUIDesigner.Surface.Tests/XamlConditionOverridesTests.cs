// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers the answers the user forces on conditional XAML conditions: how they are matched to a condition,
/// how they combine with evaluation, and their text form, which travels between the designer processes and
/// is stored in the solution's user options.
/// </summary>
[TestClass]
public sealed class XamlConditionOverridesTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = "IsApiContractPresent(" + _Contract + ",7)";
    private const string _C7N = "IsApiContractNotPresent(" + _Contract + ",7)";
    private const string _C99 = "IsApiContractPresent(" + _Contract + ",99)";

    [TestMethod]
    public void OneOverrideDecidesBothFormsOfAQuestion()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(Key(_C7), holds: false);

        Assert.AreEqual(false, overrides.Evaluate(Query(_C7)));
        Assert.AreEqual(true, overrides.Evaluate(Query(_C7N)));
        Assert.AreEqual(false, overrides.Evaluate(Query($"IsApiContractPresent( {_Contract} , 7 )")));
        Assert.AreEqual(true, overrides.Evaluate(XamlConditionQuery.Create("p:" + _C7N, _ => "http://schemas.microsoft.com/winfx/2006/xaml/presentation")));
        Assert.IsNull(overrides.Evaluate(Query(_C99)));
        Assert.IsNull(overrides.Evaluate(Query($"IsApiContractPresent({_Contract},7,1)")), "A minor version asks another question.");
    }

    [TestMethod]
    public void CustomConditionIsOverriddenForTheNamespaceOfItsPrefix()
    {
        XamlConditionQuery inApp = XamlConditionQuery.Create("cond:FeatureFlagCondition(NewExperience)", _ => "using:My.App");
        XamlConditionQuery inOtherNamespace = XamlConditionQuery.Create("cond:FeatureFlagCondition(NewExperience)", _ => "using:Other");
        XamlConditionQuery otherArgument = XamlConditionQuery.Create("cond:FeatureFlagCondition(LegacyMode)", _ => "using:My.App");

        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(inApp.Key, holds: false);

        Assert.AreEqual(false, overrides.Evaluate(inApp));
        Assert.AreEqual(false, overrides.Evaluate(XamlConditionQuery.Create("other:FeatureFlagCondition(NewExperience)", _ => "using:My.App")), "The prefix does not matter, the namespace does.");
        Assert.IsNull(overrides.Evaluate(inOtherNamespace));
        Assert.IsNull(overrides.Evaluate(otherArgument));
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,abc)")]
    [DataRow("IsTypePresent(")]
    [DataRow("Nonsense")]
    [DataRow("")]
    public void ConditionThatCannotBeEvaluatedIsNeverOverridden(string condition)
    {
        XamlConditionQuery query = Query(condition);
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(query.Key.Length == 0 ? "empty" : query.Key, holds: true);

        Assert.IsFalse(XamlConditionOverrides.CanOverride(query));
        Assert.IsNull(overrides.Evaluate(query));
    }

    [TestMethod]
    public void EvaluatorAsksOnlyAboutQuestionsThatAreNotOverridden()
    {
        var asked = new List<string>();
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty
            .With(Key(_C7), holds: false)
            .With(XamlConditionQuery.Create("cond:Flag(On)", _ => "using:My.App").Key, holds: true);
        XamlConditionEvaluator evaluate = XamlConditionOverrides.CreateEvaluator(overrides, query =>
        {
            asked.Add(query.Condition.Text);
            return query.Condition.IsNegated ? false : true;
        });

        Assert.AreEqual(false, evaluate(_C7, _ => null));
        Assert.AreEqual(true, evaluate(_C7N, _ => null));
        Assert.AreEqual(true, evaluate("cond:Flag(On)", prefix => prefix == "cond" ? "using:My.App" : null));
        Assert.AreEqual(true, evaluate(_C99, _ => null));
        Assert.AreEqual(false, evaluate("IsApiContractNotPresent(" + _Contract + ",99)", _ => null));

        CollectionAssert.AreEqual(new[] { _C99, "IsApiContractNotPresent(" + _Contract + ",99)" }, asked);
    }

    [TestMethod]
    public void EvaluatorWithoutOverridesAsksAboutEverything()
    {
        int calls = 0;
        XamlConditionEvaluator evaluate = XamlConditionOverrides.CreateEvaluator(null, _ =>
        {
            calls++;
            return null;
        });

        Assert.IsNull(evaluate(_C7, _ => null));
        Assert.AreEqual(1, calls);
        Assert.ThrowsExactly<ArgumentNullException>(() => XamlConditionOverrides.CreateEvaluator(XamlConditionOverrides.Empty, null!));
    }

    [TestMethod]
    public void TextFormEscapesOnlyWhatStructuresTheOptions()
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty
            .With(Key(_C7), holds: false)
            .With("using:My.App|FeatureFlagCondition(NewExperience)", holds: true);

        Assert.AreEqual(
            "IsApiContractPresent(Windows.Foundation.UniversalApiContract%2C7)=0,using:My.App|FeatureFlagCondition(NewExperience)=1",
            overrides.Format());
        Assert.AreEqual(overrides.Format(), overrides.ToString());
        Assert.AreEqual(string.Empty, XamlConditionOverrides.Empty.Format());
    }

    [TestMethod]
    [DataRow("using:My.App|FeatureFlagCondition(A;B=C,D%E)")]
    [DataRow("using:My.App|Flag(%25 %2C %u0041 %ZZ %)")]
    [DataRow("using:My.App|Flag( leading and trailing )")]
    [DataRow("using:My.App|Flag(\ttab\r\nnew line\u0001\u007F)")]
    [DataRow("using:My.App|Flag(é ∑ \"quoted\" \\ / ' < > & ? # [ ] { } |)")]
    [DataRow("using:My.App|Flag(=,=;;,)")]
    [DataRow("?undeclared|Flag(x)")]
    [DataRow(" using:Spaced.Namespace |Flag(x)")]
    [DataRow("IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)")]
    public void HostileKeysSurviveTheTextForm(string key)
    {
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(key, holds: true).With("IsTypePresent(X)", holds: false);

        string text = overrides.Format();

        Assert.AreEqual(-1, text.IndexOf(';'), text);
        Assert.AreEqual(1, text.Count(character => character == ','), text);
        Assert.AreEqual(text.Trim(), text, "Nothing at either end may be lost to trimming.");
        Assert.IsTrue(XamlConditionOverrides.TryParse(text, out XamlConditionOverrides parsed), text);
        Assert.AreEqual(overrides, parsed);
        Assert.IsTrue(parsed.TryGetValue(key, out bool holds) && holds, text);
    }

    [TestMethod]
    public void EveryCharacterSurvivesTheTextForm()
    {
        var builder = new System.Text.StringBuilder("using:My.App|Flag(");
        for (int code = 1; code < 0x3100; code++)
        {
            if (!char.IsSurrogate((char)code))
            {
                builder.Append((char)code);
            }
        }

        string key = builder.Append(')').ToString();
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(key, holds: false);

        Assert.IsTrue(XamlConditionOverrides.TryParse(overrides.Format(), out XamlConditionOverrides parsed));
        Assert.IsTrue(parsed.TryGetValue(key, out bool holds));
        Assert.IsFalse(holds);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(",")]
    [DataRow(" , ,")]
    public void NoTextReadsAsNoOverrides(string? text)
    {
        Assert.IsTrue(XamlConditionOverrides.TryParse(text, out XamlConditionOverrides overrides));
        Assert.AreSame(XamlConditionOverrides.Empty, overrides);
        Assert.IsTrue(overrides.IsEmpty);
    }

    [TestMethod]
    [DataRow("a=1,garbage", "a")]
    [DataRow("a=1,=1", "a")]
    [DataRow("a=1,b=2", "a")]
    [DataRow("a=1,b=yes", "a")]
    [DataRow("a=1,b", "a")]
    [DataRow("a=1,b%ZZ=1", "a")]
    [DataRow("a=1,b%2=1", "a")]
    [DataRow("a=1,b%u12=1", "a")]
    [DataRow("a=1,b%=0", "a")]
    public void EntryThatCannotBeReadIsLeftOut(string text, string readableKey)
    {
        Assert.IsFalse(XamlConditionOverrides.TryParse(text, out XamlConditionOverrides overrides));
        CollectionAssert.AreEqual(new[] { readableKey }, overrides.Keys.ToArray());
    }

    [TestMethod]
    public void TextIsReadLeniently()
    {
        Assert.IsTrue(XamlConditionOverrides.TryParse(" b = 0 , a=1 ,a=0,c%2c%2C=1, ", out XamlConditionOverrides overrides));

        CollectionAssert.AreEqual(new[] { "a", "b", "c,," }, overrides.Keys.ToArray());
        Assert.IsTrue(overrides.TryGetValue("a", out bool a) && !a, "The last value of a key wins.");
        Assert.IsTrue(overrides.TryGetValue("b", out bool b) && !b);
        Assert.IsTrue(overrides.TryGetValue("c,,", out bool c) && c);
    }

    [TestMethod]
    public void OverridesAreImmutableAndCompareByContent()
    {
        XamlConditionOverrides first = XamlConditionOverrides.Empty.With("b", true).With("a", false);
        XamlConditionOverrides second = XamlConditionOverrides.Create([new("a", true), new("b", true), new("a", false), new(string.Empty, true)]);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(first == second);
        Assert.AreEqual(2, first.Count);
        CollectionAssert.AreEqual(new[] { "a", "b" }, first.Keys.ToArray());

        XamlConditionOverrides changed = first.With("a", true);
        Assert.AreNotEqual(first, changed);
        Assert.IsTrue(first != changed);
        Assert.IsTrue(first.TryGetValue("a", out bool unchanged) && !unchanged, "With returns a copy.");
        Assert.AreSame(first, first.With("a", false), "Forcing the same answer again changes nothing.");

        XamlConditionOverrides removed = first.Without("a");
        CollectionAssert.AreEqual(new[] { "b" }, removed.Keys.ToArray());
        Assert.AreEqual(2, first.Count, "Without returns a copy.");
        Assert.AreSame(first, first.Without("missing"));
        Assert.AreSame(XamlConditionOverrides.Empty, removed.Without("b"));
        Assert.IsFalse(first.Equals(null));
        Assert.ThrowsExactly<ArgumentException>(() => first.With(string.Empty, true));
    }

    private static XamlConditionQuery Query(string condition)
    {
        return XamlConditionQuery.Create(condition, resolveNamespace: null);
    }

    private static string Key(string condition)
    {
        return Query(condition).Key;
    }
}
