// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Inserting the root of one XAML document into another so that the WinUI XAML reader reads it as before. The
/// reader's rules the grafter has to respect were measured with Windows App SDK 2.5.1: it rejects
/// <c>mc:Ignorable</c> on a nested element, and it resolves the prefix of an element name with the outermost
/// declaration of that prefix.
/// </summary>
[TestClass]
public sealed class XamlFragmentGrafterTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _Blend = "http://schemas.microsoft.com/expression/blend/2008";
    private const string _MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly XNamespace _P = _Presentation;

    [TestMethod]
    public void PrefixThatTheDocumentBindsToAnotherNamespaceIsRenamedInNamesAndValues()
    {
        XElement merged = CreateMergedDictionaries($"""xmlns:local="using:My.App" xmlns:_1="{_Presentation}" """);
        XElement fragment = Parse($"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:local="using:My.Library" xmlns:_1="using:XSurfUwp.Fallback">
              <Style x:Key="S" TargetType="local:Card" BasedOn="{"{"}StaticResource Base{"}"}">
                <Setter Property="local:Card.Accent" Value="{"{"}local:Accent Kind=local:Kind.Bold{"}"}" />
              </Style>
              <local:Card x:Key="C" Tag="(local:Card.Accent)" />
              <_1:FallbackValueConverter x:Key="Converter" />
            </ResourceDictionary>
            """);

        int renamed = XamlFragmentGrafter.Append(merged, fragment);
        string xaml = Serialize(merged);

        Assert.AreEqual(2, renamed);
        XElement inserted = merged.Elements().Single();
        Assert.AreEqual("using:My.Library", (string?)inserted.Attribute(XNamespace.Xmlns + "local_1"));
        Assert.AreEqual("using:XSurfUwp.Fallback", (string?)inserted.Attribute(XNamespace.Xmlns + "_1_1"));
        StringAssert.Contains(xaml, "TargetType=\"local_1:Card\"");
        StringAssert.Contains(xaml, "Property=\"local_1:Card.Accent\"");
        StringAssert.Contains(xaml, "Value=\"{local_1:Accent Kind=local_1:Kind.Bold}\"");
        StringAssert.Contains(xaml, "Tag=\"(local_1:Card.Accent)\"");
        StringAssert.Contains(xaml, "<local_1:Card ");
        StringAssert.Contains(xaml, "<_1_1:FallbackValueConverter ");
        Assert.AreEqual(0, ResolveAsWinUIReader(xaml).Count, string.Join(Environment.NewLine, ResolveAsWinUIReader(xaml)));
    }

    [TestMethod]
    public void PrefixBoundToTheSameNamespaceOrNotDeclaredOutsideIsKept()
    {
        XElement merged = CreateMergedDictionaries("""xmlns:_0="using:XSurfUwp" """);
        XElement fragment = Parse($"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:_0="using:XSurfUwp" xmlns:controls="using:My.Controls">
              <controls:Card x:Key="C" Tag="controls:Card" />
            </ResourceDictionary>
            """);

        Assert.AreEqual(0, XamlFragmentGrafter.Append(merged, fragment));

        string xaml = Serialize(merged);
        StringAssert.Contains(xaml, "<controls:Card ");
        StringAssert.Contains(xaml, "Tag=\"controls:Card\"");
        Assert.AreEqual(0, ResolveAsWinUIReader(xaml).Count);
    }

    [TestMethod]
    public void DeclarationNestedInTheFragmentIsComparedWithItsEnclosingElements()
    {
        // The fragment binds "p" once on its root and once more on a nested element; the document binds it too.
        XElement merged = CreateMergedDictionaries("""xmlns:p="using:Outer" """);
        XElement fragment = Parse($"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:p="using:Outer">
              <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary xmlns:p="using:Inner"><p:Thing Tag="p:Thing" /></ResourceDictionary>
              </ResourceDictionary.MergedDictionaries>
              <p:Thing Tag="p:Thing" />
            </ResourceDictionary>
            """);

        Assert.AreEqual(1, XamlFragmentGrafter.Append(merged, fragment));

        string xaml = Serialize(merged);
        StringAssert.Contains(xaml, "<p_1:Thing Tag=\"p_1:Thing\" />");
        StringAssert.Contains(xaml, "<p:Thing Tag=\"p:Thing\" />");
        Assert.AreEqual(0, ResolveAsWinUIReader(xaml).Count, string.Join(Environment.NewLine, ResolveAsWinUIReader(xaml)));
    }

    [TestMethod]
    public void RenamedPrefixAvoidsEveryPrefixOfTheDocument()
    {
        XElement merged = CreateMergedDictionaries("""xmlns:p="using:A" xmlns:p_1="using:B" """);
        merged.Parent!.Add(new XElement("Holder", new XAttribute(XNamespace.Xmlns + "p_2", "using:C")));
        XElement fragment = Parse($"""<ResourceDictionary xmlns="{_Presentation}" xmlns:p="using:D"><p:Thing /></ResourceDictionary>""");

        XamlFragmentGrafter.Append(merged, fragment);

        Assert.AreEqual("using:D", (string?)merged.Elements().Single().Attribute(XNamespace.Xmlns + "p_3"));
    }

    [TestMethod]
    [DataRow("p:Rectangle", "q:Rectangle")]
    [DataRow("{p:Extension Value=p:Type.Member}", "{q:Extension Value=q:Type.Member}")]
    [DataRow("(p:Grid.Row)", "(q:Grid.Row)")]
    [DataRow("{Binding Path=(p:Attached.Value), Converter={StaticResource C}}", "{Binding Path=(q:Attached.Value), Converter={StaticResource C}}")]
    [DataRow("top:Rectangle", "top:Rectangle")]
    [DataRow("p.q:Rectangle", "p.q:Rectangle")]
    [DataRow("http://p:8080/x", "http://p:8080/x")]
    [DataRow("ms-p:Thing", "ms-p:Thing")]
    [DataRow("p:", "p:")]
    [DataRow("plain text", "plain text")]
    public void ReferencesInAValueAreRenamed(string value, string expected)
    {
        Assert.AreEqual(expected, XamlFragmentGrafter.RenameReferences(value, "p", "q"));
    }

    [TestMethod]
    public void ListsOfMarkupCompatibilityAreRenamedByToken()
    {
        XElement merged = CreateMergedDictionaries($"""xmlns:d="using:Other" """);
        XElement fragment = Parse($"""
            <ResourceDictionary xmlns="{_Presentation}">
              <ResourceDictionary xmlns:d="{_Blend}" xmlns:mc="{_MarkupCompatibility}" mc:Ignorable="d dd" mc:ProcessContent="d:Panel dd:Panel" />
            </ResourceDictionary>
            """);

        XamlFragmentGrafter.Append(merged, fragment);

        XElement nested = merged.Descendants(_P + "ResourceDictionary").Last();
        XNamespace mc = _MarkupCompatibility;
        Assert.AreEqual("d_1 dd", (string?)nested.Attribute(mc + "Ignorable"));
        Assert.AreEqual("d_1:Panel dd:Panel", (string?)nested.Attribute(mc + "ProcessContent"));
    }

    [TestMethod]
    public void IgnorableContentAndRootOnlyAttributesAreRemovedFromANestedElement()
    {
        XElement fragment = Parse($"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:d="{_Blend}" xmlns:mc="{_MarkupCompatibility}"
                                xmlns:not_win="http://uno.ui/not_win" xmlns:win="{_Presentation}"
                                mc:Ignorable="d not_win" x:Class="My.App.Resources" d:DesignWidth="100">
              <SolidColorBrush x:Key="A" Color="Red" d:Note="design only" not_win:Opacity="0.5" />
              <not_win:SolidColorBrush x:Key="B" Color="Blue"><SolidColorBrush.Opacity>0.5</SolidColorBrush.Opacity></not_win:SolidColorBrush>
              <win:SolidColorBrush x:Key="C" Color="Green" />
              <d:DesignData />
            </ResourceDictionary>
            """);

        XamlFragmentGrafter.PrepareAsNestedElement(fragment);

        CollectionAssert.AreEqual(new[] { "A", "C" }, fragment.Elements().Select(element => (string?)element.Attribute(XName.Get("Key", _XamlLanguage))).ToArray());
        Assert.IsFalse(fragment.DescendantsAndSelf().Attributes().Any(attribute => !attribute.IsNamespaceDeclaration
            && attribute.Name.NamespaceName is _Blend or "http://uno.ui/not_win" or _MarkupCompatibility));
        Assert.IsNull(fragment.Attribute(XName.Get("Class", _XamlLanguage)));

        // The declarations stay: values may still name their prefixes, and they cost nothing.
        Assert.IsNotNull(fragment.Attribute(XNamespace.Xmlns + "d"));
    }

    [TestMethod]
    public void ElementWithoutIgnorableNamespacesKeepsItsContent()
    {
        XElement fragment = Parse($"""<ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}"><SolidColorBrush x:Key="A" Color="Red" /></ResourceDictionary>""");
        string before = fragment.ToString(SaveOptions.DisableFormatting);

        XamlFragmentGrafter.PrepareAsNestedElement(fragment);

        Assert.AreEqual(before, fragment.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement CreateMergedDictionaries(string declarations)
    {
        XElement page = Parse($"""
            <Page xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" {declarations}>
              <Page.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries />
                </ResourceDictionary>
              </Page.Resources>
            </Page>
            """);
        return page.Descendants(_P + "ResourceDictionary.MergedDictionaries").Single();
    }

    private static XElement Parse(string xaml)
    {
        return XElement.Parse(xaml, LoadOptions.PreserveWhitespace);
    }

    private static string Serialize(XElement element)
    {
        return element.AncestorsAndSelf().Last().ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Reads the names of the elements of <paramref name="xaml"/> the way the WinUI XAML reader does: a prefix means
    /// what its outermost declaration says. Returns the elements for which that differs from XML.
    /// </summary>
    internal static List<string> ResolveAsWinUIReader(string xaml)
    {
        var mismatches = new List<string>();
        var scopes = new List<Dictionary<string, string>>();
        using var reader = XmlReader.Create(new StringReader(xaml));
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                scopes.RemoveAt(scopes.Count - 1);
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var scope = new Dictionary<string, string>(StringComparer.Ordinal);
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.Prefix == "xmlns")
                    {
                        scope[reader.LocalName] = reader.Value;
                    }
                }
                while (reader.MoveToNextAttribute());

                reader.MoveToElement();
            }

            scopes.Add(scope);
            if (reader.Prefix.Length != 0)
            {
                string? outermost = scopes.Select(candidate => candidate.TryGetValue(reader.Prefix, out string? value) ? value : null).FirstOrDefault(value => value is not null);
                if (!string.Equals(outermost, reader.NamespaceURI, StringComparison.Ordinal))
                {
                    mismatches.Add($"<{reader.Name}> is in '{reader.NamespaceURI}', but the reader takes '{outermost}'.");
                }
            }

            if (reader.IsEmptyElement)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }
        }

        return mismatches;
    }
}
