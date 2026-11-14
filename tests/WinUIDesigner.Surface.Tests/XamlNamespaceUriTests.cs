// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;

[TestClass]
public sealed class XamlNamespaceUriTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void PlainNamespaceIsItsOwnBaseUri()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(_Presentation);

        Assert.AreEqual(_Presentation, uri.Original);
        Assert.AreEqual(_Presentation, uri.BaseUri);
        Assert.IsNull(uri.Condition);
        Assert.IsFalse(uri.IsConditional);
        Assert.AreEqual(0, uri.UsingNamespaces.Count);
        Assert.AreEqual(_Presentation, uri.ToString());
    }

    [TestMethod]
    public void ConditionIsEverythingAfterTheQuestionMark()
    {
        const string condition = "IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";

        XamlNamespaceUri uri = XamlNamespaceUri.Parse(_Presentation + "?" + condition);

        Assert.AreEqual(_Presentation + "?" + condition, uri.Original);
        Assert.AreEqual(_Presentation, uri.BaseUri);
        Assert.AreEqual(condition, uri.Condition);
        Assert.IsTrue(uri.IsConditional);
        Assert.AreEqual(0, uri.UsingNamespaces.Count);
    }

    [TestMethod]
    public void ClrNamespaceCanCarryACondition()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse("using:My.Controls?IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)");

        Assert.AreEqual("using:My.Controls", uri.BaseUri);
        Assert.AreEqual("IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)", uri.Condition);
    }

    [TestMethod]
    public void CustomConditionKeepsItsPrefix()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(_Presentation + "?conditions:IsFeatureEnabled(Preview)");

        Assert.AreEqual(_Presentation, uri.BaseUri);
        Assert.AreEqual("conditions:IsFeatureEnabled(Preview)", uri.Condition);
    }

    [TestMethod]
    public void UsingSuffixListsItsNamespacesInOrder()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse("http://uno.ui/android#using:My.Ns1;My.Ns2");

        Assert.AreEqual("http://uno.ui/android", uri.BaseUri);
        Assert.IsNull(uri.Condition);
        CollectionAssert.AreEqual(new[] { "My.Ns1", "My.Ns2" }, uri.UsingNamespaces.ToArray());
    }

    [TestMethod]
    public void UsingSuffixIgnoresEmptyEntries()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse("http://uno.ui/ios#using:;My.Ns1; ;  My.Ns2 ;");

        CollectionAssert.AreEqual(new[] { "My.Ns1", "My.Ns2" }, uri.UsingNamespaces.ToArray());
    }

    [TestMethod]
    [DataRow("http://uno.ui/android#using:My.Ns1;My.Ns2?IsTypePresent(My.Type)")]
    [DataRow("http://uno.ui/android?IsTypePresent(My.Type)#using:My.Ns1;My.Ns2")]
    public void UsingSuffixAndConditionCombineInEitherOrder(string value)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(value);

        Assert.AreEqual(value, uri.Original);
        Assert.AreEqual("http://uno.ui/android", uri.BaseUri);
        Assert.AreEqual("IsTypePresent(My.Type)", uri.Condition);
        CollectionAssert.AreEqual(new[] { "My.Ns1", "My.Ns2" }, uri.UsingNamespaces.ToArray());
    }

    [TestMethod]
    public void EmptyConditionIsStillACondition()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(_Presentation + "?");

        Assert.AreEqual(_Presentation, uri.BaseUri);
        Assert.AreEqual(string.Empty, uri.Condition);
        Assert.IsTrue(uri.IsConditional);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void MissingNamespaceParsesToTheEmptyNamespace(string? value)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(value);

        Assert.AreEqual(string.Empty, uri.Original);
        Assert.AreEqual(string.Empty, uri.BaseUri);
        Assert.IsNull(uri.Condition);
        Assert.AreEqual(0, uri.UsingNamespaces.Count);
    }

    [TestMethod]
    public void DefaultValueIsTheEmptyNamespace()
    {
        XamlNamespaceUri uri = default;

        Assert.AreEqual(string.Empty, uri.Original);
        Assert.AreEqual(string.Empty, uri.BaseUri);
        Assert.IsNull(uri.Condition);
        Assert.IsFalse(uri.IsConditional);
        Assert.AreEqual(0, uri.UsingNamespaces.Count);
        Assert.AreEqual(string.Empty, uri.ToString());
    }

    [TestMethod]
    [DataRow("clr-namespace:My.App;assembly=My.App")]
    [DataRow("using:My.App")]
    [DataRow("http://schemas.microsoft.com/winfx/2006/xaml")]
    [DataRow("not a uri # at all")]
    public void NamespaceWithoutConditionOrUsingSuffixIsKeptWhole(string value)
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse(value);

        Assert.AreEqual(value, uri.BaseUri);
        Assert.IsNull(uri.Condition);
        Assert.AreEqual(0, uri.UsingNamespaces.Count);
    }
}
