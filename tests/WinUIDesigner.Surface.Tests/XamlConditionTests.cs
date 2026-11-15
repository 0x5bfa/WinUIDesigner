// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

/// <summary>
/// Covers the grammar of conditional XAML predicates. The accepted texts are the ones the WinUI XAML reader
/// accepts; most of the rejected ones end its process instead of raising an error.
/// </summary>
[TestClass]
public sealed class XamlConditionTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    [TestMethod]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)", "ApiContract", false, "Windows.Foundation.UniversalApiContract|7")]
    [DataRow("IsApiContractNotPresent(Windows.Foundation.UniversalApiContract,7)", "ApiContract", true, "Windows.Foundation.UniversalApiContract|7")]
    [DataRow("IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)", "Type", false, "Microsoft.UI.Xaml.Controls.ColorPicker")]
    [DataRow("IsTypeNotPresent(Microsoft.UI.Xaml.Controls.ColorPicker)", "Type", true, "Microsoft.UI.Xaml.Controls.ColorPicker")]
    [DataRow("IsPropertyPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)", "Property", false, "Microsoft.UI.Xaml.Controls.Control|CornerRadius")]
    [DataRow("IsPropertyNotPresent(Microsoft.UI.Xaml.Controls.Control,CornerRadius)", "Property", true, "Microsoft.UI.Xaml.Controls.Control|CornerRadius")]
    public void BuiltInMethodsAreRecognized(string text, string kind, bool isNegated, string arguments)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(kind, condition.Kind.ToString());
        Assert.AreEqual(isNegated, condition.IsNegated);
        Assert.AreEqual(arguments, string.Join("|", condition.Arguments));
        Assert.AreEqual(text, condition.Text);
        Assert.AreEqual(text.Substring(0, text.IndexOf('(')), condition.Name);
        Assert.AreEqual(string.Empty, condition.Prefix);
        Assert.IsNull(condition.Error);
        Assert.AreEqual(text, condition.ToString());
    }

    [TestMethod]
    public void ContractConditionHasAMajorVersionAndAnOptionalMinorVersion()
    {
        XamlCondition major = XamlCondition.Parse($"IsApiContractPresent({_Contract},7)");
        XamlCondition minor = XamlCondition.Parse($"IsApiContractNotPresent({_Contract},7,3)");

        Assert.AreEqual(7, major.MajorVersion);
        Assert.IsNull(major.MinorVersion);

        Assert.AreEqual(XamlConditionKind.ApiContract, minor.Kind);
        Assert.AreEqual(7, minor.MajorVersion);
        Assert.AreEqual(3, minor.MinorVersion);
        Assert.AreEqual($"IsApiContractPresent({_Contract},7,3)", minor.PositiveKey);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent( Windows.Foundation.UniversalApiContract , 7 )")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract, 7)")]
    [DataRow("IsApiContractPresent(\tWindows.Foundation.UniversalApiContract,\r\n 7)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,007)")]
    [DataRow("IsApiContractNotPresent(  Windows.Foundation.UniversalApiContract,7  )")]
    public void WhiteSpaceAroundArgumentsDoesNotChangeTheQuestion(string text)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.ApiContract, condition.Kind);
        Assert.AreEqual($"IsApiContractPresent({_Contract},7)", condition.PositiveKey);
        CollectionAssert.AreEqual(new[] { _Contract, condition.Arguments[1] }, condition.Arguments.ToArray());
        Assert.AreEqual(7, condition.MajorVersion);

        // The text itself is kept as written.
        Assert.AreEqual(text, condition.Text);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,7)", "IsApiContractNotPresent(X,7)")]
    [DataRow("IsTypePresent(My.Type)", "IsTypeNotPresent( My.Type )")]
    [DataRow("IsPropertyPresent(My.Type,Size)", "IsPropertyNotPresent(My.Type , Size)")]
    public void PresentAndNotPresentAskTheSameQuestion(string present, string notPresent)
    {
        XamlCondition positive = XamlCondition.Parse(present);
        XamlCondition negative = XamlCondition.Parse(notPresent);

        Assert.AreEqual(positive.PositiveKey, negative.PositiveKey);
        Assert.AreEqual(present, positive.PositiveKey);
        Assert.IsFalse(positive.IsNegated);
        Assert.IsTrue(negative.IsNegated);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,7)", "IsApiContractPresent(X,8)")]
    [DataRow("IsApiContractPresent(X,7)", "IsApiContractPresent(X,7,0)")]
    [DataRow("IsApiContractPresent(X,7)", "IsApiContractPresent(x,7)")]
    [DataRow("IsTypePresent(My.Type)", "IsPropertyPresent(My.Type,Size)")]
    public void DifferentQuestionsHaveDifferentKeys(string first, string second)
    {
        Assert.AreNotEqual(XamlCondition.Parse(first).PositiveKey, XamlCondition.Parse(second).PositiveKey);
    }

    [TestMethod]
    public void CustomConditionKeepsItsRawArgument()
    {
        XamlCondition condition = XamlCondition.Parse("cond:FeatureFlagCondition(A, B)");

        Assert.AreEqual(XamlConditionKind.Custom, condition.Kind);
        Assert.AreEqual("cond", condition.Prefix);
        Assert.AreEqual("FeatureFlagCondition", condition.Name);
        Assert.AreEqual("A, B", condition.Argument);
        Assert.AreEqual(0, condition.Arguments.Count);
        Assert.IsFalse(condition.IsNegated);
        Assert.AreEqual("FeatureFlagCondition(A, B)", condition.PositiveKey);
        Assert.IsNull(condition.Error);
    }

    [TestMethod]
    [DataRow("cond:Flag()", "")]
    [DataRow("cond:Flag( On )", " On ")]
    [DataRow("cond:Flag(On(1))", "On(1)")]
    [DataRow("cond:Flag(a=b; c)", "a=b; c")]
    [DataRow("my-prefix.x:Flag_2(,)", ",")]
    public void ArgumentOfACustomConditionIsEverythingBetweenTheOuterParentheses(string text, string argument)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.Custom, condition.Kind);
        Assert.AreEqual(argument, condition.Argument);
    }

    [TestMethod]
    [DataRow("IsFooPresent(X)", "IsFooPresent")]
    [DataRow("isapicontractpresent(Windows.Foundation.UniversalApiContract,7)", "isapicontractpresent")]
    [DataRow("FeatureFlagCondition(NewExperience)", "FeatureFlagCondition")]
    public void NameWithoutPrefixThatIsNoBuiltInMethodNamesAConditionTypeOfTheDefaultNamespace(string text, string name)
    {
        // The XAML reader looks such a name up as a type; method names are case-sensitive.
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.Custom, condition.Kind);
        Assert.AreEqual(string.Empty, condition.Prefix);
        Assert.AreEqual(name, condition.Name);
    }

    [TestMethod]
    public void BuiltInNameWithAPrefixIsACustomConditionUntilThePrefixIsResolved()
    {
        XamlCondition condition = XamlCondition.Parse($"p:IsApiContractNotPresent({_Contract}, 7)");

        Assert.AreEqual(XamlConditionKind.Custom, condition.Kind);
        Assert.AreEqual("p", condition.Prefix);

        XamlCondition builtIn = condition.WithoutPrefix();
        Assert.AreEqual(XamlConditionKind.ApiContract, builtIn.Kind);
        Assert.IsTrue(builtIn.IsNegated);
        Assert.AreEqual($"IsApiContractPresent({_Contract},7)", builtIn.PositiveKey);
        Assert.AreSame(builtIn, builtIn.WithoutPrefix());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void NoTextIsTheEmptyCondition(string? text)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.Empty, condition.Kind);
        Assert.AreEqual(string.Empty, condition.Text);
        Assert.IsNull(condition.Error);
    }

    [TestMethod]
    // Missing or misplaced parentheses.
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7")]
    [DataRow("IsApiContractPresent")]
    [DataRow("cond:FeatureFlagCondition")]
    [DataRow("IsApiContractPresent)")]
    // Text after the argument list, white space included.
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)x")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7) ")]
    [DataRow("IsTypePresent(A)IsTypePresent(B")]
    // White space in or around the name.
    [DataRow(" IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)")]
    [DataRow("IsApiContractPresent (Windows.Foundation.UniversalApiContract,7)")]
    [DataRow("cond:Flag (On)")]
    [DataRow("cond: Flag(On)")]
    [DataRow(" ")]
    // Wrong number of arguments.
    [DataRow("IsApiContractPresent()")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7,0,0)")]
    [DataRow("IsTypePresent()")]
    [DataRow("IsTypePresent(A,B)")]
    [DataRow("IsPropertyPresent(A)")]
    [DataRow("IsPropertyPresent(A,B,C)")]
    // Empty arguments.
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,)")]
    [DataRow("IsApiContractPresent(,7)")]
    [DataRow("IsPropertyPresent(A, )")]
    // Versions that are not plain numbers of 16 bits.
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,abc)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7.0)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7abc)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,-1)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,+7)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,0x7)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,65536)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7,x)")]
    [DataRow("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)?IsApiContractPresent(Windows.Foundation.UniversalApiContract,99)")]
    // Names that are no names.
    [DataRow(":Flag(On)")]
    [DataRow("cond:(On)")]
    [DataRow("(On)")]
    [DataRow("cond:x:Flag(On)")]
    [DataRow("cond:My.Flag(On)")]
    [DataRow("1Flag(On)")]
    [DataRow("-cond:Flag(On)")]
    [DataRow("Flag!(On)")]
    public void TextTheXamlReaderDoesNotAcceptIsUnknown(string text)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.Unknown, condition.Kind, text);
        Assert.IsFalse(string.IsNullOrEmpty(condition.Error), text);
        Assert.AreEqual(text, condition.Text);
        Assert.AreEqual(text, condition.PositiveKey);
        Assert.IsFalse(condition.IsNegated);
        Assert.AreEqual(0, condition.Arguments.Count);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent(X,0)", 0)]
    [DataRow("IsApiContractPresent(X,65535)", 65535)]
    [DataRow("IsApiContractPresent(X,00042)", 42)]
    public void VersionsCoverSixteenBits(string text, int major)
    {
        XamlCondition condition = XamlCondition.Parse(text);

        Assert.AreEqual(XamlConditionKind.ApiContract, condition.Kind);
        Assert.AreEqual(major, condition.MajorVersion);
    }

    [TestMethod]
    [DataRow("IsApiContractPresent", true)]
    [DataRow("IsApiContractNotPresent", true)]
    [DataRow("IsTypePresent", true)]
    [DataRow("IsTypeNotPresent", true)]
    [DataRow("IsPropertyPresent", true)]
    [DataRow("IsPropertyNotPresent", true)]
    [DataRow("IsMethodPresent", false)]
    [DataRow("isTypePresent", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void OnlyTheSixMethodsOfTheXamlReaderAreBuiltIn(string? name, bool expected)
    {
        Assert.AreEqual(expected, XamlCondition.IsBuiltInMethod(name));
    }

    [TestMethod]
    public void ParsingNeverThrows()
    {
        string[] texts =
        [
            "(", ")", "()", "((", "))", ")(", ":", "::", "a:", ":a", "a:b", "a(b", "a)b", "a(b)c(d)", "\0", "\uD800",
            "IsApiContractPresent(,,,,)", "IsTypePresent(()", "IsPropertyPresent(,)", new string('(', 2000), new string('a', 5000) + "(x)",
        ];

        foreach (string text in texts)
        {
            XamlCondition condition = XamlCondition.Parse(text);
            Assert.AreEqual(text, condition.Text);
        }
    }
}
