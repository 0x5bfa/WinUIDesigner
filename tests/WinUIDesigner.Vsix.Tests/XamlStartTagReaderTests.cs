// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;

[TestClass]
public sealed class XamlStartTagReaderTests
{
    [TestMethod]
    [DataRow("<Border c7:CornerRadius=\"4\" c7n:CornerRadius=\"0\" Tag=\"t\" />", "c7:CornerRadius|c7n:CornerRadius|Tag")]
    [DataRow("<c7:Border\r\n    x:Name = 'a'\r\n\tGrid.Row=\"1\"\r\n    c7:Grid.Column=\"2\">", "x:Name|Grid.Row|c7:Grid.Column")]
    [DataRow("<Border Tag=\"a > b\" Text='say \"hi\"' Content=\"it's\"/>", "Tag|Text|Content")]
    [DataRow("<Page xmlns=\"http://example\" xmlns:c7=\"http://example?IsTypePresent(A)\" c7:Tag=\"1\">", "xmlns|xmlns:c7|c7:Tag")]
    [DataRow("  <Border Tag=\"1\"><TextBlock Text=\"inner\" /></Border>", "Tag")]
    [DataRow("<Border>", "")]
    [DataRow("<Border/>", "")]
    [DataRow("<Border", "")]
    public void AttributeNamesAreReadInTheOrderTheyAreWritten(string startTag, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames(startTag);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    public void PrefixAndLocalNameAreSeparated()
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames("<Border c7:Grid.Row=\"1\" Tag=\"t\" />");

        Assert.AreEqual("c7", names[0].Prefix);
        Assert.AreEqual("Grid.Row", names[0].LocalName);
        Assert.AreEqual(string.Empty, names[1].Prefix);
        Assert.AreEqual("Tag", names[1].LocalName);
    }

    [TestMethod]
    // The names read before the malformed part are still returned.
    [DataRow("<Border Tag=\"1\" Broken", "Tag")]
    [DataRow("<Border Tag=\"1\" Broken=", "Tag")]
    [DataRow("<Border Tag=\"1\" Broken=value", "Tag")]
    [DataRow("<Border Tag=\"1\" Text=\"unterminated", "Tag")]
    [DataRow("<Border Tag=\"1\" a:b:c=\"2\" Text=\"3\"", "Tag")]
    [DataRow("<Border Tag=\"1\" :b=\"2\"", "Tag")]
    [DataRow("<Border Tag=\"1\" a:=\"2\"", "Tag")]
    [DataRow("</Border>", "")]
    [DataRow("Border Tag=\"1\"", "")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void MalformedTextDoesNotThrow(string? startTag, string expected)
    {
        IReadOnlyList<XamlAttributeName> names = XamlStartTagReader.ReadAttributeNames(startTag);

        Assert.AreEqual(expected, string.Join("|", names.Select(name => name.ToString())));
    }

    [TestMethod]
    [DataRow("c7:CornerRadius=\"4\"", "c7", "CornerRadius")]
    [DataRow("  c7:Grid.Row = \"1\"", "c7", "Grid.Row")]
    [DataRow("Tag='t'", "", "Tag")]
    [DataRow("Tag", "", "Tag")]
    public void AttributeNameIsReadFromTheAttributeText(string attribute, string prefix, string localName)
    {
        Assert.IsTrue(XamlStartTagReader.TryReadAttributeName(attribute, out XamlAttributeName name));

        Assert.AreEqual(prefix, name.Prefix);
        Assert.AreEqual(localName, name.LocalName);
    }

    [TestMethod]
    [DataRow("=\"4\"")]
    [DataRow(":CornerRadius=\"4\"")]
    [DataRow("c7:=\"4\"")]
    [DataRow("a:b:c=\"4\"")]
    [DataRow("   ")]
    [DataRow("")]
    [DataRow(null)]
    public void TextThatDoesNotBeginWithANameIsNoAttribute(string? attribute)
    {
        Assert.IsFalse(XamlStartTagReader.TryReadAttributeName(attribute, out XamlAttributeName name));

        Assert.AreEqual(string.Empty, name.Prefix);
        Assert.AreEqual(string.Empty, name.LocalName);
    }
}
