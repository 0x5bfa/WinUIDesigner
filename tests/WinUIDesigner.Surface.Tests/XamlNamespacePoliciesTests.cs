// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;

/// <summary>
/// Covers how the verdicts of the two namespace policies are combined, independently of what each policy decides.
/// </summary>
[TestClass]
public sealed class XamlNamespacePoliciesTests
{
    [TestMethod]
    // The platform decides first: an excluded member is dropped whatever its condition says.
    [DataRow("Drop", "Leave", false, "Drop")]
    [DataRow("Drop", "Keep", true, "Drop")]
    [DataRow("Drop", "Drop", true, "Drop")]
    // Then the condition: a member of the platform whose condition does not hold is dropped.
    [DataRow("Keep", "Drop", true, "Drop")]
    [DataRow("Leave", "Drop", true, "Drop")]
    [DataRow("Keep", "Keep", true, "Keep")]
    [DataRow("Leave", "Keep", true, "Keep")]
    // Without a verdict on the condition the platform's verdict stands, unless keeping would strip the condition.
    [DataRow("Keep", "Leave", false, "Keep")]
    [DataRow("Keep", "Leave", true, "Leave")]
    [DataRow("Leave", "Leave", false, "Leave")]
    [DataRow("Leave", "Leave", true, "Leave")]
    public void PlatformVerdictIsCombinedWithTheConditionalVerdict(string platform, string conditional, bool isConditionalNamespace, string expected)
    {
        XamlPrefixDisposition actual = XamlNamespacePolicies.Combine(
            Enum.Parse<XamlPrefixDisposition>(platform),
            Enum.Parse<XamlPrefixDisposition>(conditional),
            isConditionalNamespace);

        Assert.AreEqual(Enum.Parse<XamlPrefixDisposition>(expected), actual);
    }

    [TestMethod]
    [DataRow("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation", false)]
    [DataRow("x", "http://schemas.microsoft.com/winfx/2006/xaml", false)]
    [DataRow("local", "using:My.App", false)]
    [DataRow("controls", "clr-namespace:My.App;assembly=My.App", false)]
    [DataRow("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006", false)]
    [DataRow("d", "http://schemas.microsoft.com/expression/blend/2008", true)]
    public void OrdinaryNamespacesAreLeftAlone(string prefix, string uri, bool isIgnorable)
    {
        XamlPrefixDisposition disposition = XamlNamespacePolicies.Classify(
            prefix,
            XamlNamespaceUri.Parse(uri),
            isIgnorable,
            UnoPlatformPrefixes.DefaultTargetPlatform,
            evaluateCondition: _ => throw new InvalidOperationException("An unconditional namespace has no condition to evaluate."));

        Assert.AreEqual(XamlPrefixDisposition.Leave, disposition);
    }

    [TestMethod]
    public void DocumentWithOrdinaryNamespacesNeedsNoWork()
    {
        const string xaml = """
            <Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:local="using:My.App"
                  xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  mc:Ignorable="d">
              <local:Card x:Name="card" d:Title="Design" />
            </Page>
            """;

        XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);

        Assert.IsTrue(scan.IsComplete);
        Assert.IsFalse(XamlNamespacePolicies.MayAffect(scan, UnoPlatformPrefixes.DefaultTargetPlatform, evaluateCondition: null));
    }
}
