// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// The conditional-namespace policy does not interpret conditions yet. These tests pin that:
/// until it does, conditional members must reach the XAML reader exactly as they were written.
/// </summary>
[TestClass]
public sealed class ConditionalNamespacePolicyTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _Conditional = _Presentation + "?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [DataRow(null)]
    public void ConditionalNamespaceIsLeftAloneWhateverItsConditionEvaluatesTo(bool? outcome)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(_Conditional);

        Assert.AreEqual(XamlPrefixDisposition.Leave, ConditionalNamespacePolicy.Classify(uri, _ => outcome));
        Assert.AreEqual(XamlPrefixDisposition.Leave, ConditionalNamespacePolicy.Classify(uri, evaluateCondition: null));
    }

    [TestMethod]
    public void UnconditionalNamespaceIsLeftAlone()
    {
        XamlPrefixDisposition disposition = ConditionalNamespacePolicy.Classify(XamlNamespaceUri.Parse(_Presentation), _ => true);

        Assert.AreEqual(XamlPrefixDisposition.Leave, disposition);
    }

    [TestMethod]
    public void DocumentWithConditionalMembersIsNotRewritten()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}" xmlns:c7="{_Conditional}">
              <c7:Border c7:CornerRadius="4">
                <c7:Border.Child><TextBlock c7:Text="new" /></c7:Border.Child>
              </c7:Border>
            </Page>
            """;
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        string before = document.ToString(SaveOptions.DisableFormatting);

        XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(
            document,
            new XamlNamespaceRewriteOptions { SourceXaml = xaml, EvaluateCondition = (_, _) => true });

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, document.ToString(SaveOptions.DisableFormatting));
    }
}
