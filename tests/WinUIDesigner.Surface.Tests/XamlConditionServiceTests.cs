// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers how conditions are resolved to questions and how the questions are answered through probes.
/// </summary>
[TestClass]
public sealed class XamlConditionServiceTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    [TestMethod]
    public void ContractConditionAsksTheContractProbe()
    {
        var probes = new RecordingProbes { ContractAnswer = (_, major, _) => major <= 10 };
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(true, conditions.Evaluate($"IsApiContractPresent({_Contract},7)", NoNamespaces));
        Assert.AreEqual(false, conditions.Evaluate($"IsApiContractPresent({_Contract},99)", NoNamespaces));
        Assert.AreEqual(true, conditions.Evaluate($"IsApiContractPresent({_Contract}, 7, 2)", NoNamespaces));

        CollectionAssert.AreEqual(
            new[] { $"contract {_Contract} 7", $"contract {_Contract} 99", $"contract {_Contract} 7.2" },
            probes.Calls);
        Assert.AreEqual(0, probes.Reports.Count);
    }

    [TestMethod]
    [DataRow("IsTypePresent(Windows.Foundation.Uri)", "windows type Windows.Foundation.Uri")]
    [DataRow("IsTypePresent(Windows.UI.Xaml.Controls.Button)", "windows type Windows.UI.Xaml.Controls.Button")]
    [DataRow("IsTypePresent(Microsoft.UI.Xaml.Controls.Button)", "managed type Microsoft.UI.Xaml.Controls.Button")]
    [DataRow("IsTypePresent(My.App.Card)", "managed type My.App.Card")]
    [DataRow("IsTypePresent(WindowsCommunity.Card)", "managed type WindowsCommunity.Card")]
    [DataRow("IsTypePresent(windows.foundation.uri)", "managed type windows.foundation.uri")]
    [DataRow("IsPropertyPresent(Windows.Foundation.Uri, Host)", "windows property Windows.Foundation.Uri.Host")]
    [DataRow("IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)", "managed property Microsoft.UI.Xaml.Controls.Control.CornerRadius")]
    public void TypesOfTheOperatingSystemAndOtherTypesGoToDifferentProbes(string condition, string expectedCall)
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(true, conditions.Evaluate(condition, NoNamespaces));

        CollectionAssert.AreEqual(new[] { expectedCall }, probes.Calls);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,7)", "IsApiContractNotPresent(X,7)")]
    [DataRow("IsTypePresent(My.Type)", "IsTypeNotPresent(My.Type)")]
    [DataRow("IsPropertyPresent(Windows.Foundation.Uri,Host)", "IsPropertyNotPresent(Windows.Foundation.Uri,Host)")]
    public void NotPresentIsTheOppositeOfPresent(string present, string notPresent)
    {
        foreach (bool answer in new[] { true, false })
        {
            var probes = new RecordingProbes
            {
                ContractAnswer = (_, _, _) => answer,
                TypeAnswer = _ => answer,
                PropertyAnswer = (_, _) => answer,
            };
            XamlConditionService conditions = probes.CreateService();

            Assert.AreEqual(answer, conditions.Evaluate(present, NoNamespaces));
            Assert.AreEqual(!answer, conditions.Evaluate(notPresent, NoNamespaces));
        }
    }

    [TestMethod]
    public void ComplementaryConditionsAreAnsweredByOneProbeCall()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        // Asked in the order a document may use them: the negative branch first, repeatedly, with other spacing.
        Assert.AreEqual(false, conditions.Evaluate($"IsApiContractNotPresent({_Contract},7)", NoNamespaces));
        Assert.AreEqual(true, conditions.Evaluate($"IsApiContractPresent({_Contract},7)", NoNamespaces));
        Assert.AreEqual(true, conditions.Evaluate($"IsApiContractPresent( {_Contract} , 7 )", NoNamespaces));
        Assert.AreEqual(false, conditions.Evaluate($"IsApiContractNotPresent({_Contract}, 007)", NoNamespaces));

        Assert.AreEqual(1, probes.Calls.Count);
    }

    [TestMethod]
    public void CustomConditionIsEvaluatedByItsTypeWithTheArgumentAsWritten()
    {
        var probes = new RecordingProbes { CustomAnswer = (_, argument) => argument == " NewExperience, 2 " };
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(true, conditions.Evaluate("cond:FeatureFlagCondition( NewExperience, 2 )", Namespaces(("cond", "using:My.App"))));
        Assert.AreEqual(false, conditions.Evaluate("cond:FeatureFlagCondition(NewExperience, 2)", Namespaces(("cond", "using:My.App"))));

        CollectionAssert.AreEqual(
            new[] { "custom My.App.FeatureFlagCondition[ NewExperience, 2 ]", "custom My.App.FeatureFlagCondition[NewExperience, 2]" },
            probes.Calls);
        Assert.AreEqual(0, probes.Reports.Count);
    }

    [TestMethod]
    public void CustomConditionIsEvaluatedOncePerTypeAndArgument()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        // The same type through two prefixes, and through a namespace that carries a condition of its own.
        Assert.AreEqual(true, conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "using:My.App"))));
        Assert.AreEqual(true, conditions.Evaluate("other:Flag(On)", Namespaces(("other", "using:My.App"))));
        Assert.AreEqual(true, conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "using:My.App?IsApiContractPresent(X,7)"))));
        Assert.AreEqual(1, probes.Calls.Count);

        // Another argument and another namespace are other questions.
        conditions.Evaluate("cond:Flag(Off)", Namespaces(("cond", "using:My.App")));
        conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "using:Other.App")));
        CollectionAssert.AreEqual(
            new[] { "custom My.App.Flag[On]", "custom My.App.Flag[Off]", "custom Other.App.Flag[On]" },
            probes.Calls);
    }

    [TestMethod]
    public void CustomConditionWithoutPrefixIsATypeOfTheDefaultNamespace()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(true, conditions.Evaluate("FeatureFlagCondition(On)", Namespaces(("", "using:My.App"))));

        CollectionAssert.AreEqual(new[] { "custom My.App.FeatureFlagCondition[On]" }, probes.Calls);
    }

    [TestMethod]
    public void ClrNamespaceSyntaxNamesTheNamespaceOfACondition()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "clr-namespace:My.App;assembly=My.App.Core")));

        CollectionAssert.AreEqual(new[] { "custom My.App.Flag[On]" }, probes.Calls);
    }

    [TestMethod]
    public void CustomConditionThatCannotBeEvaluatedIsNotSatisfiedAndReportedOnce()
    {
        var probes = new RecordingProbes { CustomAnswer = (_, _) => null };
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(false, conditions.Evaluate("cond:NoSuchCondition(X)", Namespaces(("cond", "using:My.App"))));
        Assert.AreEqual(false, conditions.Evaluate("cond:NoSuchCondition(X)", Namespaces(("cond", "using:My.App"))));

        Assert.AreEqual(1, probes.Calls.Count);
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], "My.App.NoSuchCondition");
        StringAssert.Contains(probes.Reports[0], "not satisfied");
    }

    [TestMethod]
    public void CustomConditionThatThrowsIsNotSatisfiedAndReportedOnce()
    {
        var probes = new RecordingProbes { CustomAnswer = (_, _) => throw new InvalidOperationException("flags are not loaded") };
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(false, conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "using:My.App"))));
        Assert.AreEqual(false, conditions.Evaluate("cond:Flag(On)", Namespaces(("cond", "using:My.App"))));

        Assert.AreEqual(1, probes.Calls.Count);
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], "cond:Flag(On)");
        StringAssert.Contains(probes.Reports[0], "InvalidOperationException: flags are not loaded");
    }

    [TestMethod]
    public void BuiltInProbeThatThrowsMakesTheConditionNotSatisfied()
    {
        var probes = new RecordingProbes { ContractAnswer = (_, _, _) => throw new InvalidOperationException("no metadata") };
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(false, conditions.Evaluate("IsApiContractPresent(X,7)", NoNamespaces));
        Assert.AreEqual(true, conditions.Evaluate("IsApiContractNotPresent(X,7)", NoNamespaces));

        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], "no metadata");
    }

    [TestMethod]
    [DataRow("nope:Flag(On)", "prefix 'nope'")]
    [DataRow("uri:Flag(On)", "names no CLR namespace")]
    [DataRow("IsFooPresent(X)", "'IsFooPresent'")]
    public void CustomConditionWhoseTypeCannotBeNamedIsNotSatisfied(string condition, string expectedReport)
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();
        Func<string, string?> namespaces = Namespaces(("", _Presentation), ("uri", "http://example.org/conditions"));

        Assert.AreEqual(false, conditions.Evaluate(condition, namespaces));
        Assert.AreEqual(false, conditions.Evaluate(condition, namespaces));

        Assert.AreEqual(0, probes.Calls.Count);
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], expectedReport);
    }

    [TestMethod]
    public void UnknownMethodWithoutADefaultNamespaceIsNotSatisfied()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(false, conditions.Evaluate("IsFooPresent(X)", NoNamespaces));

        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], "no default namespace");
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,7")]
    [DataRow("IsApiContractPresent(X,abc)")]
    [DataRow("IsTypePresent(A,B)")]
    [DataRow("IsApiContractPresent (X,7)")]
    [DataRow("cond:Flag")]
    public void MalformedConditionIsNotSatisfiedAndReportedOnce(string condition)
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(false, conditions.Evaluate(condition, NoNamespaces));
        Assert.AreEqual(false, conditions.Evaluate(condition, NoNamespaces));

        Assert.AreEqual(0, probes.Calls.Count);
        Assert.AreEqual(1, probes.Reports.Count);
        StringAssert.Contains(probes.Reports[0], condition);
    }

    [TestMethod]
    public void EmptyConditionHoldsWithoutAskingAnyone()
    {
        var probes = new RecordingProbes();
        XamlConditionService conditions = probes.CreateService();

        Assert.AreEqual(true, conditions.Evaluate(string.Empty, NoNamespaces));

        Assert.AreEqual(0, probes.Calls.Count);
        Assert.AreEqual(0, probes.Reports.Count);
    }

    [TestMethod]
    public void BuiltInMethodIsFoundThroughAPrefixOfThePresentationNamespace()
    {
        var probes = new RecordingProbes { ContractAnswer = (_, major, _) => major <= 10 };
        XamlConditionService conditions = probes.CreateService();
        Func<string, string?> namespaces = Namespaces(("p", _Presentation), ("cond", "using:My.App"));

        Assert.AreEqual(true, conditions.Evaluate($"p:IsApiContractPresent({_Contract},7)", namespaces));
        Assert.AreEqual(false, conditions.Evaluate($"IsApiContractNotPresent({_Contract},7)", namespaces));

        // Through any other prefix the name is a type of that namespace.
        Assert.AreEqual(true, conditions.Evaluate($"cond:IsApiContractPresent({_Contract},7)", namespaces));

        CollectionAssert.AreEqual(
            new[] { $"contract {_Contract} 7", $"custom My.App.IsApiContractPresent[{_Contract},7]" },
            probes.Calls);
    }

    [TestMethod]
    public void ConditionWithoutAProbeCannotBeEvaluated()
    {
        var conditions = new XamlConditionService(new XamlConditionProbeSet());
        Func<string, string?> namespaces = Namespaces(("cond", "using:My.App"));

        Assert.IsNull(conditions.Evaluate("IsApiContractPresent(X,7)", namespaces));
        Assert.IsNull(conditions.Evaluate("IsApiContractNotPresent(X,7)", namespaces));
        Assert.IsNull(conditions.Evaluate("IsTypePresent(Windows.Foundation.Uri)", namespaces));
        Assert.IsNull(conditions.Evaluate("IsTypePresent(My.Type)", namespaces));
        Assert.IsNull(conditions.Evaluate("IsPropertyPresent(Windows.Foundation.Uri,Host)", namespaces));
        Assert.IsNull(conditions.Evaluate("IsPropertyPresent(My.Type,Size)", namespaces));
        Assert.IsNull(conditions.Evaluate("cond:Flag(On)", namespaces));

        // What needs no probe is still decided.
        Assert.AreEqual(false, conditions.Evaluate("nope:Flag(On)", namespaces));
        Assert.AreEqual(false, conditions.Evaluate("IsTypePresent(", namespaces));
        Assert.AreEqual(true, conditions.Evaluate(string.Empty, namespaces));
    }

    [TestMethod]
    public void AnswerOfAnotherProcessReplacesTheOwnAnswer()
    {
        var probes = new RecordingProbes { ContractAnswer = (_, _, _) => true };
        XamlConditionService conditions = probes.CreateService();
        XamlConditionQuery present = XamlConditionQuery.Create("IsApiContractPresent(X,7)", NoNamespaces);
        XamlConditionQuery notPresent = XamlConditionQuery.Create("IsApiContractNotPresent(X, 7)", NoNamespaces);

        Assert.AreEqual(true, conditions.Evaluate(present));

        Assert.IsTrue(conditions.SetOutcome(present.Key, false));
        Assert.AreEqual(false, conditions.Evaluate(present));
        Assert.AreEqual(true, conditions.Evaluate(notPresent));

        // The same answer again changes nothing; an answer to a question that was never asked is new.
        Assert.IsFalse(conditions.SetOutcome(notPresent.Key, false));
        Assert.IsTrue(conditions.SetOutcome(XamlConditionQuery.Create("IsTypePresent(My.Type)", NoNamespaces).Key, true));
        Assert.AreEqual(true, conditions.Evaluate("IsTypePresent(My.Type)", NoNamespaces));
        Assert.AreEqual(1, probes.Calls.Count);
    }

    [TestMethod]
    public void FailingReportReceiverDoesNotChangeTheOutcome()
    {
        var conditions = new XamlConditionService(new XamlConditionProbeSet(), _ => throw new InvalidOperationException("log is closed"));

        Assert.AreEqual(false, conditions.Evaluate("IsTypePresent(", NoNamespaces));
    }

    [TestMethod]
    public void QueryOfACustomConditionCarriesItsNamespaceAndType()
    {
        XamlConditionQuery query = XamlConditionQuery.Create("cond:Flag(A, B)", Namespaces(("cond", "using:My.App?IsTypePresent(My.Type)")));

        Assert.AreEqual(XamlConditionKind.Custom, query.Condition.Kind);
        Assert.AreEqual("using:My.App", query.PredicateNamespace);
        Assert.AreEqual("My.App.Flag", query.TypeName);
        Assert.AreEqual("using:My.App|Flag(A, B)", query.Key);
    }

    [TestMethod]
    [DataRow("IsApiContractNotPresent( X , 7 )", "IsApiContractPresent(X,7)")]
    [DataRow("IsTypeNotPresent(My.Type)", "IsTypePresent(My.Type)")]
    [DataRow("IsPropertyPresent(My.Type, Size)", "IsPropertyPresent(My.Type,Size)")]
    [DataRow("p:IsTypeNotPresent(My.Type)", "IsTypePresent(My.Type)")]
    [DataRow("IsTypePresent(", "!IsTypePresent(")]
    [DataRow("nope:Flag(On)", "?nope|Flag(On)")]
    [DataRow("uri:Flag(On)", "http://example.org/conditions|Flag(On)")]
    [DataRow("", "")]
    public void QueryKeyIdentifiesTheQuestion(string condition, string expectedKey)
    {
        Func<string, string?> namespaces = Namespaces(("p", _Presentation), ("uri", "http://example.org/conditions"));

        XamlConditionQuery query = XamlConditionQuery.Create(condition, namespaces);

        Assert.AreEqual(expectedKey, query.Key);
        if (query.Condition.Kind != XamlConditionKind.Custom)
        {
            Assert.IsNull(query.PredicateNamespace);
            Assert.IsNull(query.TypeName);
        }
    }

    [TestMethod]
    [DataRow("IsApiContractNotPresent(X,7)", null)]
    [DataRow("cond:Flag( On )", "using:My.App")]
    [DataRow("cond:Flag(On)", null)]
    [DataRow("Flag(On)", "using:My.App")]
    [DataRow("IsTypePresent(", null)]
    public void QueryIsRecreatedFromItsTextAndNamespace(string condition, string? predicateNamespace)
    {
        XamlConditionQuery original = XamlConditionQuery.Create(condition, _ => predicateNamespace);

        XamlConditionQuery recreated = XamlConditionQuery.Create(original.Condition.Text, original.PredicateNamespace);

        Assert.AreEqual(original.Key, recreated.Key);
        Assert.AreEqual(original.TypeName, recreated.TypeName);
        Assert.AreEqual(original.Condition.Kind, recreated.Condition.Kind);
        Assert.AreEqual(original.Condition.IsNegated, recreated.Condition.IsNegated);
    }

    [TestMethod]
    public void QueryOfABuiltInMethodNeverResolvesAPrefix()
    {
        XamlConditionQuery query = XamlConditionQuery.Create("IsTypePresent(My.Type)", _ => throw new InvalidOperationException("no prefix to resolve"));

        Assert.AreEqual(XamlConditionKind.Type, query.Condition.Kind);
    }

    private static string? NoNamespaces(string prefix)
    {
        return null;
    }

    private static Func<string, string?> Namespaces(params (string Prefix, string Namespace)[] declarations)
    {
        return prefix =>
        {
            foreach ((string declared, string namespaceName) in declarations)
            {
                if (declared == prefix)
                {
                    return namespaceName;
                }
            }

            return null;
        };
    }

    /// <summary>
    /// Probes that answer from delegates and record every call.
    /// </summary>
    private sealed class RecordingProbes
    {
        public List<string> Calls { get; } = [];

        public List<string> Reports { get; } = [];

        public Func<string, int, int?, bool> ContractAnswer { get; set; } = (_, _, _) => true;

        public Func<string, bool> TypeAnswer { get; set; } = _ => true;

        public Func<string, string, bool> PropertyAnswer { get; set; } = (_, _) => true;

        public Func<string, string, bool?> CustomAnswer { get; set; } = (_, _) => true;

        public XamlConditionService CreateService()
        {
            var probes = new XamlConditionProbeSet
            {
                IsApiContractPresent = (name, major, minor) =>
                {
                    Calls.Add($"contract {name} {major}{(minor.HasValue ? "." + minor.Value : string.Empty)}");
                    return ContractAnswer(name, major, minor);
                },
                IsWindowsTypePresent = name =>
                {
                    Calls.Add("windows type " + name);
                    return TypeAnswer(name);
                },
                IsWindowsPropertyPresent = (type, property) =>
                {
                    Calls.Add($"windows property {type}.{property}");
                    return PropertyAnswer(type, property);
                },
                IsManagedTypePresent = name =>
                {
                    Calls.Add("managed type " + name);
                    return TypeAnswer(name);
                },
                IsManagedPropertyPresent = (type, property) =>
                {
                    Calls.Add($"managed property {type}.{property}");
                    return PropertyAnswer(type, property);
                },
                EvaluateCustom = (type, argument) =>
                {
                    Calls.Add($"custom {type}[{argument}]");
                    return CustomAnswer(type, argument);
                },
            };

            return new XamlConditionService(probes, Reports.Add);
        }
    }
}
