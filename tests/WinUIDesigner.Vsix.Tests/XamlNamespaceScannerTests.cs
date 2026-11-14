// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

[TestClass]
public sealed class XamlNamespaceScannerTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string _Android = "http://uno.ui/android";

    [TestMethod]
    public void DeclarationsOnTheRootAndOnNestedElementsAreReportedInDocumentOrder()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid xmlns:c7="http://schemas.microsoft.com/winfx/2006/xaml/presentation?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)">
                <TextBlock xmlns:android="http://uno.ui/android" />
              </Grid>
            </Page>
            """;

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.IsComplete);
        CollectionAssert.AreEqual(new[] { "", "x", "c7", "android" }, result.Declarations.Select(declaration => declaration.Prefix).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0, 1, 2 }, result.Declarations.Select(declaration => declaration.Depth).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, false, false }, result.Declarations.Select(declaration => declaration.IsOnRoot).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, result.Declarations.Select(declaration => declaration.LineNumber).ToArray());
        Assert.AreEqual(7, result.Declarations[0].LinePosition);

        XamlNamespaceUri conditional = result.Declarations[2].Uri;
        Assert.AreEqual(_Presentation, conditional.BaseUri);
        Assert.AreEqual("IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)", conditional.Condition);
        Assert.AreEqual(_Android, result.Declarations[3].Uri.Original);
    }

    [TestMethod]
    public void PrefixIsResolvedToItsFirstDeclaration()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}\"><Grid xmlns:android=\"using:Other\" /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.AreEqual(_Presentation, result.FindNamespace(string.Empty));
        Assert.AreEqual(_Android, result.FindNamespace("android"));
        Assert.IsNull(result.FindNamespace("ios"));
    }

    [TestMethod]
    [DataRow("d android ios")]
    [DataRow("  d   android  ios  ")]
    [DataRow("d&#9;android&#9;ios")]
    [DataRow("d&#10;android&#13;&#10;ios")]
    [DataRow("d\tandroid\nios")]
    public void IgnorablePrefixesAreSplitOnAnyWhitespace(string ignorable)
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:mc=\"{_MarkupCompatibility}\" mc:Ignorable=\"{ignorable}\" />";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        CollectionAssert.AreEquivalent(new[] { "d", "android", "ios" }, result.IgnorablePrefixes.ToArray());
        Assert.IsTrue(result.IsIgnorable("android"));
        Assert.IsFalse(result.IsIgnorable("win"));
    }

    [TestMethod]
    public void IgnorablePrefixesOfNestedElementsAreCollectedToo()
    {
        string xaml =
            $"<Page xmlns=\"{_Presentation}\" xmlns:mc=\"{_MarkupCompatibility}\" mc:Ignorable=\"d\">" +
            "<Grid mc:Ignorable=\"android\" /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        CollectionAssert.AreEquivalent(new[] { "d", "android" }, result.IgnorablePrefixes.ToArray());
    }

    [TestMethod]
    public void IgnorableAttributeOfAnotherNamespaceIsNotMarkupCompatibility()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:my=\"using:My\" my:Ignorable=\"android\" Ignorable=\"ios\" />";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.AreEqual(0, result.IgnorablePrefixes.Count);
    }

    [TestMethod]
    public void ConditionalNamespaceIsFlagged()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:c7=\"{_Presentation}?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)\" />";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasConditionalNamespaces);
        Assert.IsFalse(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void OrdinaryDocumentHasNoFlags()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:local=\"using:My.App\"><local:Card Title=\"A?\" /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.IsComplete);
        Assert.IsFalse(result.HasConditionalNamespaces);
        Assert.IsFalse(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
        Assert.IsFalse(XamlNamespacePolicies.MayAffect(result, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));
    }

    [TestMethod]
    public void ObjectElementUnderPlatformNamespaceIsFlagged()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}\"><StackPanel><android:Border /></StackPanel></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsTrue(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void ObjectElementUnderPlatformNamespaceWithUsingSuffixIsFlagged()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}#using:My.Droid\"><android:NativeView /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsTrue(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void AttributeUnderPlatformNamespaceIsNotAnObjectElement()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}\"><TextBlock android:Text=\"Hello\" /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void PropertyElementUnderPlatformNamespaceIsNotAnObjectElement()
    {
        string xaml =
            $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}\">" +
            "<Border><android:Border.Child><Grid /></android:Border.Child></Border></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void PresentationNamespaceUnderAPlatformPrefixIsNotAPlatformNamespace()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:win=\"{_Presentation}\"><win:Border win:Tag=\"1\" /></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsFalse(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    public void ElementPrefixIsResolvedAgainstTheDeclarationInScope()
    {
        // "p" is a platform namespace on the root but an ordinary namespace where the element is written.
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:p=\"{_Android}\"><Grid xmlns:p=\"using:My.App\"><p:Card /></Grid></Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsFalse(result.HasPlatformPrefixedObjectElements);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not xml at all")]
    [DataRow("<Page")]
    [DataRow("<Page><Grid></Page>")]
    [DataRow("<Page xmlns:a=\"\" />")]
    [DataRow("<a:Page />")]
    [DataRow("<Page /><Page />")]
    [DataRow("<Page>&undefined;</Page>")]
    public void MalformedXamlDoesNotThrow(string? xaml)
    {
        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsFalse(result.IsComplete);
    }

    [TestMethod]
    public void FactsReadBeforeMalformedXamlAreStillReported()
    {
        string xaml = $"<Page xmlns=\"{_Presentation}\" xmlns:android=\"{_Android}\" xmlns:mc=\"{_MarkupCompatibility}\" mc:Ignorable=\"android\"><android:Border /><Grid><</Page>";

        XamlNamespaceScanResult result = XamlNamespaceScanner.Scan(xaml);

        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(3, result.Declarations.Count);
        Assert.IsTrue(result.HasPlatformNamespaces);
        Assert.IsTrue(result.HasPlatformPrefixedObjectElements);
        Assert.IsTrue(result.IsIgnorable("android"));
    }

    [TestMethod]
    [DataRow("<win:Border Background=\"Red\">", "win")]
    [DataRow("<win:Border", "win")]
    [DataRow("<win:Border/>", "win")]
    [DataRow("  \r\n<win:Border.Background>", "win")]
    [DataRow("<c7:Grid\r\n    x:Name=\"a\"", "c7")]
    [DataRow("<not_android:TextBlock Text=\"a:b\" />", "not_android")]
    [DataRow("<Border>", "")]
    [DataRow("<Border/>", "")]
    [DataRow("<Border Tag=\"a:b\">", "")]
    [DataRow("</win:Border>", null)]
    [DataRow("<!-- win:Border -->", null)]
    [DataRow("<?xml version=\"1.0\"?>", null)]
    [DataRow("win:Border", null)]
    [DataRow("<win:>", null)]
    [DataRow("<:Border>", null)]
    [DataRow("<a:b:c>", null)]
    [DataRow("<", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void ElementPrefixIsReadFromTheStartTag(string? startTag, string? expected)
    {
        Assert.AreEqual(expected, XamlNamespaceScanner.ReadElementPrefix(startTag));
    }

    [TestMethod]
    [DataRow("win:FontSize=\"24\"", "win")]
    [DataRow("win:FontSize", "win")]
    [DataRow("win:Grid.Row = \"1\"", "win")]
    [DataRow("  x:Name='a'", "x")]
    [DataRow("xmlns:win=\"http://example\"", "xmlns")]
    [DataRow("FontSize=\"24\"", "")]
    [DataRow("Text=\"a:b\"", "")]
    [DataRow("xmlns=\"http://example\"", "")]
    [DataRow("win:=\"24\"", null)]
    [DataRow("=\"24\"", null)]
    [DataRow("\"24\"", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void AttributePrefixIsReadFromTheAttributeText(string? attribute, string? expected)
    {
        Assert.AreEqual(expected, XamlNamespaceScanner.ReadAttributePrefix(attribute));
    }
}
