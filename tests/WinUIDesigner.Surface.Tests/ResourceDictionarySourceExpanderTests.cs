// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Merging the dictionaries that prepared XAML names through <c>DT.ResourceDictionarySource</c>, with Visual Studio's
/// shadow cache kept in memory. The prepared copies have the shape Visual Studio 18 gives them: the cleaner's own
/// prefixes <c>_0</c> and <c>_1</c>, bound to different namespaces in different files, and design-time URIs of the form
/// <c>ms-appx:///Project/Folder/~vs]~id~File.xaml</c>. The namespace policies are the real ones.
/// </summary>
[TestClass]
public sealed class ResourceDictionarySourceExpanderTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string _XamlLanguage = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _ContentDirectory = @"C:\DesignerCache\1-1868410451x64DA";
    private const string _PageUri = "ms-appx:///App/Views/~vs]~10~Page.xaml";
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    private const string _Colors = "ms-appx:///App/Themes/~vs]~11~Colors.xaml";
    private const string _Styles = "ms-appx:///App/Themes/~vs]~12~Styles.xaml";
    private const string _Margins = "ms-appx:///Library/~vs]~13~Margins.xaml";
    private const string _Cards = "ms-appx:///Library/Styles/~vs]~14~Cards.xaml";

    private static readonly XNamespace _P = _Presentation;
    private static readonly XNamespace _X = _XamlLanguage;

    [TestMethod]
    public void PreparedDictionaryIsMergedLastIntoTheDictionaryThatNamedIt()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="AccentBrush" Color="SeaGreen" />"""));
        XElement page = Page($"""
            <ResourceDictionary.MergedDictionaries>
              <ResourceDictionary><SolidColorBrush x:Key="Inline" Color="Red" /></ResourceDictionary>
              <ResourceDictionary _0:DT.ResourceDictionarySource="{_Colors}" />
            </ResourceDictionary.MergedDictionaries>
            """);

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 0), expansion);
        XElement target = page.Descendants(_P + "ResourceDictionary").Single(element => element.Elements().Any(child => child.Name.LocalName.EndsWith(".MergedDictionaries")) && element.Parent!.Name.LocalName.EndsWith(".MergedDictionaries"));
        XElement inserted = target.Elements().Single().Elements().Single();
        Assert.IsTrue(ResourceDictionarySourceExpander.IsInserted(inserted));
        CollectionAssert.AreEqual(new[] { "AccentBrush" }, Keys(inserted));

        // The source is gone; what the document had stays where it was.
        Assert.IsFalse(page.DescendantsAndSelf().Attributes(ResourceDictionarySourceExpander.SourceAttributeName).Any());
        CollectionAssert.AreEqual(new[] { "Inline", "AccentBrush" }, page.Descendants(_P + "SolidColorBrush").Select(KeyOf).ToArray());
        Assert.IsFalse(ResourceDictionarySourceExpander.IsInserted(page.Descendants(_P + "SolidColorBrush").First()));
        Assert.AreEqual(0, cache.Warnings.Count, string.Join(Environment.NewLine, cache.Warnings));
        AssertReadableByTheXamlReader(page);
    }

    [TestMethod]
    public void ContentFollowsTheDictionariesMergedInline()
    {
        // At run time the entries of a Source win over its merged dictionaries; merged last, the content is looked up first.
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="AccentBrush" Color="SeaGreen" />"""));
        XElement dictionary = XElement.Parse($"""
            <ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" _0:DT.ResourceDictionarySource="{_Colors}">
              <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary><SolidColorBrush x:Key="AccentBrush" Color="Red" /></ResourceDictionary>
              </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """);

        cache.Expander.Expand(dictionary, _PageUri, "document 10");

        XElement[] merged = dictionary.Elements(_P + "ResourceDictionary.MergedDictionaries").Single().Elements().ToArray();
        Assert.AreEqual(2, merged.Length);
        Assert.AreEqual("SeaGreen", (string?)merged[1].Element(_P + "SolidColorBrush")!.Attribute("Color"));
        Assert.AreEqual(1, dictionary.Elements(_P + "ResourceDictionary.MergedDictionaries").Count());
    }

    [TestMethod]
    public void MergedDictionariesPropertyIsCreatedBeforeTheEntries()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="AccentBrush" Color="SeaGreen" />"""));
        XElement dictionary = XElement.Parse($"""
            <ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" _0:DT.ResourceDictionarySource="{_Colors}"><x:String x:Key="Own">own</x:String></ResourceDictionary>
            """);

        cache.Expander.Expand(dictionary, _PageUri, "document 10");

        Assert.AreEqual(_P + "ResourceDictionary.MergedDictionaries", dictionary.Elements().First().Name);
        Assert.AreEqual(_X + "String", dictionary.Elements().Last().Name);
    }

    [TestMethod]
    public void DictionariesMergedByAMergedDictionaryAreExpandedToo()
    {
        var cache = new ShadowCache();
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        cache.Add(_Cards, Dictionary($"""
            <ResourceDictionary.MergedDictionaries>{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>
            <Style x:Key="CardStyle" TargetType="Border"><Setter Property="Padding" Value="{"{"}StaticResource CardPadding{"}"}" /></Style>
            """));
        cache.Add(_Styles, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Styles)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(3, 0, 0), expansion);
        CollectionAssert.AreEquivalent(new[] { "CardPadding", "CardStyle" }, AllKeys(page));
        Assert.IsFalse(page.DescendantsAndSelf().Attributes(ResourceDictionarySourceExpander.SourceAttributeName).Any());
        Assert.AreEqual(0, cache.Warnings.Count, string.Join(Environment.NewLine, cache.Warnings));
        AssertReadableByTheXamlReader(page);
    }

    [TestMethod]
    public void DictionaryMergedTwiceIsMergedTwiceAndReadOnce()
    {
        // Each merge is an object of its own at run time too; reading the copy once is enough.
        var cache = new ShadowCache();
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        cache.Add(_Cards, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Margins)}{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(3, 0, 0), expansion);
        Assert.AreEqual(2, AllKeys(page).Count(key => key == "CardPadding"));
        Assert.AreEqual(1, cache.Parsed.Count(description => description.Contains("Margins.xaml", StringComparison.Ordinal)));
        Assert.AreEqual(0, cache.Warnings.Count);
    }

    [TestMethod]
    public void CycleIsCutWithOneWarningAndTheRestLoads()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Styles)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="A" Color="Red" />"""));
        cache.Add(_Styles, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="B" Color="Blue" />"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(2, 0, 1), expansion);
        CollectionAssert.AreEquivalent(new[] { "A", "B" }, AllKeys(page));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "Colors.xaml → Styles.xaml → Colors.xaml");
        StringAssert.Contains(cache.Warnings[0], "merged by 'Styles.xaml'");
    }

    [TestMethod]
    public void DictionaryThatMergesItselfIsCut()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="A" Color="Red" />"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 1), expansion);
        CollectionAssert.AreEqual(new[] { "A" }, AllKeys(page));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "Colors.xaml → Colors.xaml");
    }

    [TestMethod]
    public void DictionaryThatMergesTheDocumentIsCutAtTheDocument()
    {
        // A resource document, such as the design-time resources, merged again by a dictionary it merges.
        var cache = new ShadowCache();
        cache.Add(_Styles, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="B" Color="Blue" />"""));
        XElement document = XElement.Parse(Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Styles)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="A" Color="Red" />"""));

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(document, _Colors, "document 11");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 1), expansion);
        CollectionAssert.AreEqual(new[] { "B" }, AllKeys(document));
        StringAssert.Contains(cache.Warnings.Single(), "Colors.xaml → Styles.xaml → Colors.xaml");
    }

    [TestMethod]
    public void CutChainIsNotKept()
    {
        // Expanded from Colors, Styles lacks Colors; expanded on its own it would have it. Neither is kept.
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Styles)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="A" Color="Red" />"""));
        cache.Add(_Styles, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="B" Color="Blue" />"""));

        cache.Expander.Expand(Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>"""), _PageUri, "document 10");
        Assert.AreEqual(0, cache.Expander.Cache.Count);

        XElement second = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Styles)}</ResourceDictionary.MergedDictionaries>""");
        cache.Expander.Expand(second, _PageUri, "document 11");
        CollectionAssert.AreEquivalent(new[] { "A", "B" }, AllKeys(second));
    }

    [TestMethod]
    public void TooDeepChainEnds()
    {
        var cache = new ShadowCache(maxDepth: 4);
        for (int level = 0; level < 10; level++)
        {
            cache.Add(Level(level), Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(Level(level + 1))}</ResourceDictionary.MergedDictionaries><x:String x:Key="L{level}">{level}</x:String>"""));
        }

        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(Level(0))}</ResourceDictionary.MergedDictionaries>""");
        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(4, 0, 1), expansion);
        CollectionAssert.AreEquivalent(new[] { "L0", "L1", "L2", "L3" }, AllKeys(page));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "more than 4 levels deep");

        static string Level(int level) => $"ms-appx:///App/Chain/~vs]~{100 + level}~Level{level}.xaml";
    }

    [TestMethod]
    public void MissingPreparedCopyLeavesTheDictionaryEmptyWithOneWarning()
    {
        var cache = new ShadowCache();
        cache.Add(_Styles, Dictionary("""<SolidColorBrush x:Key="B" Color="Blue" />"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}{Merge(_Styles)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 1), expansion);
        CollectionAssert.AreEqual(new[] { "B" }, AllKeys(page));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "Resource dictionary 'Colors.xaml' merged by document 10 is left empty");
        StringAssert.Contains(cache.Warnings[0], _ContentDirectory + @"\App\Themes\~vs]~11~Colors.xaml");
        StringAssert.Contains(cache.Warnings[0], "does not exist");
        AssertReadableByTheXamlReader(page);
    }

    [TestMethod]
    public void MalformedCopyAndCopyThatIsNoDictionaryAreLeftOut()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, "<ResourceDictionary xmlns=\"" + _Presentation + "\"><SolidColorBrush>");
        cache.Add(_Styles, $"""<Page xmlns="{_Presentation}" />""");
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}{Merge(_Styles)}{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 2), expansion);
        CollectionAssert.AreEqual(new[] { "CardPadding" }, AllKeys(page));
        Assert.AreEqual(2, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "not well-formed XAML");
        StringAssert.Contains(cache.Warnings[1], "root element <Page> is not a resource dictionary");
    }

    [TestMethod]
    public void DictionaryClassOfTheProjectIsAcceptedThroughItsType()
    {
        var cache = new ShadowCache { DictionaryTypes = { "using:My.App:ThemeResources" } };
        cache.Add(_Colors, $"""<local:ThemeResources xmlns:local="using:My.App" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}"><x:String x:Key="K">v</x:String></local:ThemeResources>""");
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");

        Assert.AreEqual(new ResourceDictionaryExpansion(1, 0, 0), cache.Expander.Expand(page, _PageUri, "document 10"));
        Assert.AreEqual(XName.Get("ThemeResources", "using:My.App"), page.Descendants().Single(element => element.Name.LocalName == "ThemeResources").Name);
    }

    [TestMethod]
    public void EmptySourceIsAnEmptyDictionaryWithoutWarning()
    {
        var cache = new ShadowCache();
        XElement page = Page("""<ResourceDictionary.MergedDictionaries><ResourceDictionary _0:DT.ResourceDictionarySource="" /></ResourceDictionary.MergedDictionaries>""");

        Assert.AreEqual(new ResourceDictionaryExpansion(0, 0, 0), cache.Expander.Expand(page, _PageUri, "document 10"));
        Assert.AreEqual(0, cache.Warnings.Count);
        Assert.IsFalse(page.DescendantsAndSelf().Attributes(ResourceDictionarySourceExpander.SourceAttributeName).Any());
    }

    [TestMethod]
    public void SourceOutsideTheSolutionIsLeftToTheRuntimeWhenItLoads()
    {
        // What the cleaner keeps for a dictionary of a package, as in Visual Studio's copy of an application's App.xaml.
        const string segmented = "ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Segmented/Segmented.xaml";
        var cache = new ShadowCache();
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(segmented)}{Merge(segmented)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(0, 2, 0), expansion);
        XElement[] inserted = page.Descendants(_P + "ResourceDictionary").Where(ResourceDictionarySourceExpander.IsInserted).ToArray();
        Assert.AreEqual(2, inserted.Length);
        Assert.IsTrue(inserted.All(element => (string?)element.Attribute("Source") == segmented));

        // Whether the runtime loads a source does not change while the surface runs.
        CollectionAssert.AreEqual(new[] { segmented }, cache.RuntimeLoads);
    }

    [TestMethod]
    public void SourceTheRuntimeCannotLoadIsLeftOutWithOneWarning()
    {
        const string library = "ms-appx:///Ecierge.Uno.Controls/Styles.xaml";
        var cache = new ShadowCache { RuntimeErrors = { [library] = "Cannot locate resource from 'ms-appx:///Ecierge.Uno.Controls/Styles.xaml'." } };
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(library)}</ResourceDictionary.MergedDictionaries>""");

        ResourceDictionaryExpansion expansion = cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual(new ResourceDictionaryExpansion(0, 0, 1), expansion);
        Assert.IsFalse(page.Descendants().Any(ResourceDictionarySourceExpander.IsInserted));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "Visual Studio did not find it in the solution");
        StringAssert.Contains(cache.Warnings[0], "Cannot locate resource");
    }

    [TestMethod]
    public void RelativeSourceIsResolvedAgainstItsDocument()
    {
        // Visual Studio keeps a source it did not find in the solution as written.
        var cache = new ShadowCache();
        XElement page = Page("""<ResourceDictionary.MergedDictionaries><ResourceDictionary _0:DT.ResourceDictionarySource="Themes/Missing.xaml" /><ResourceDictionary _0:DT.ResourceDictionarySource="/Themes/Rooted.xaml" /></ResourceDictionary.MergedDictionaries>""");

        cache.Expander.Expand(page, _PageUri, "document 10");

        CollectionAssert.AreEqual(new[] { "ms-appx:///App/Views/Themes/Missing.xaml", "ms-appx:///Themes/Rooted.xaml" }, cache.RuntimeLoads);
    }

    [TestMethod]
    public void RelativeSourceInAMergedDictionaryIsResolvedAgainstThatDictionary()
    {
        var cache = new ShadowCache();
        cache.Add(_Cards, Dictionary("""<ResourceDictionary.MergedDictionaries><ResourceDictionary _0:DT.ResourceDictionarySource="../Shared/Brushes.xaml" /></ResourceDictionary.MergedDictionaries>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>""");

        cache.Expander.Expand(page, _PageUri, "document 10");

        CollectionAssert.AreEqual(new[] { "ms-appx:///Library/Shared/Brushes.xaml" }, cache.RuntimeLoads);
    }

    [TestMethod]
    public void NamespacePoliciesApplyToMergedDictionaries()
    {
        // The sample's Themes\ConditionalResources.xaml, as Visual Studio prepares it.
        var cache = new ShadowCache();
        cache.Add(_Colors, $"""
            <ResourceDictionary xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}"
                xmlns:contract7NotPresent="{_Presentation}?IsApiContractNotPresent({_Contract},7)"
                xmlns:contract7Present="{_Presentation}?IsApiContractPresent({_Contract},7)"
                xmlns:contract99NotPresent="{_Presentation}?IsApiContractNotPresent({_Contract},99)"
                xmlns:contract99Present="{_Presentation}?IsApiContractPresent({_Contract},99)">
              <SolidColorBrush x:Key="ConditionalAccentBrush" contract7NotPresent:Color="Firebrick" contract7Present:Color="SeaGreen" />
              <Style x:Key="ConditionalCardStyle" TargetType="Border">
                <Setter Property="Padding" Value="12" />
                <contract7Present:Setter Property="CornerRadius" Value="8" />
                <contract7NotPresent:Setter Property="CornerRadius" Value="0" />
                <contract99NotPresent:Setter Property="BorderThickness" Value="3" />
                <contract99Present:Setter Property="BorderThickness" Value="1" />
              </Style>
            </ResourceDictionary>
            """);
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");

        cache.Expander.Expand(page, _PageUri, "document 10");

        string xaml = page.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(XamlNamespaceScanner.Scan(xaml).HasConditionalNamespaces, xaml);
        Assert.AreEqual("SeaGreen", (string?)page.Descendants(_P + "SolidColorBrush").Single().Attribute("Color"));
        CollectionAssert.AreEqual(
            new[] { "Padding=12", "CornerRadius=8", "BorderThickness=3" },
            page.Descendants(_P + "Setter").Select(setter => $"{setter.Attribute("Property")?.Value}={setter.Attribute("Value")?.Value}").ToArray());
        AssertReadableByTheXamlReader(page);
    }

    [TestMethod]
    public void PrefixesOfTheCleanerDoNotClashAcrossFiles()
    {
        // In Visual Studio's copies "_1" is the presentation namespace in App.xaml and the fallback namespace elsewhere.
        var cache = new ShadowCache();
        cache.Add(_Colors, $"""
            <ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns:_1="using:XSurfUwp.Fallback" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:conv="using:My.Library.Converters">
              <_1:FallbackValueConverter x:Key="BoolToVisibilityConverter" />
              <Style x:Key="S" TargetType="_1:FallbackControl" />
            </ResourceDictionary>
            """);
        XElement application = XElement.Parse($"""
            <_0:Application xmlns:_0="using:XSurfUwp" xmlns:_1="{_Presentation}" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}" xmlns:conv="using:My.App.Converters">
              <_0:Application.Resources>
                <ResourceDictionary>
                  <ResourceDictionary.MergedDictionaries>
                    <_1:ResourceDictionary xmlns="using:Uno.Toolkit.UI" />
                    <ResourceDictionary _0:DT.ResourceDictionarySource="{_Colors}" />
                  </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
              </_0:Application.Resources>
            </_0:Application>
            """);

        cache.Expander.Expand(application, "ms-appx:///~vs]~9~App.xaml", "document 9");

        string xaml = application.ToString(SaveOptions.DisableFormatting);
        Assert.AreEqual(0, XamlFragmentGrafterTests.ResolveAsWinUIReader(xaml).Count, string.Join(Environment.NewLine, XamlFragmentGrafterTests.ResolveAsWinUIReader(xaml)));
        StringAssert.Contains(xaml, "<_1_1:FallbackValueConverter ");
        StringAssert.Contains(xaml, "TargetType=\"_1_1:FallbackControl\"");
        Assert.AreEqual(XName.Get("FallbackValueConverter", "using:XSurfUwp.Fallback"), XElement.Parse(xaml).Descendants().Single(element => element.Name.LocalName == "FallbackValueConverter").Name);
    }

    [TestMethod]
    public void WhatTheReaderIgnoresAndRootOnlyAttributesLeaveAMergedDictionary()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, $"""
            <ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}"
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:not_win="http://uno.ui/not_win"
                mc:Ignorable="not_win" x:Class="My.Library.Colors" _0:DT.DesignWidth="200">
              <SolidColorBrush x:Key="A" Color="Red" not_win:Opacity="0.5" />
              <not_win:SolidColorBrush x:Key="B" Color="Blue" />
            </ResourceDictionary>
            """);
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");

        cache.Expander.Expand(page, _PageUri, "document 10");

        XElement inserted = page.Descendants().Single(element => ResourceDictionarySourceExpander.IsInserted(element) && !ResourceDictionarySourceExpander.IsInserted(element.Parent!));
        CollectionAssert.AreEqual(new[] { "A" }, Keys(inserted));
        Assert.IsFalse(inserted.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration));
        Assert.IsFalse(inserted.DescendantsAndSelf().Attributes().Any(attribute => attribute.Name.NamespaceName == "http://uno.ui/not_win"));
    }

    [TestMethod]
    public void KeyedAndThemeDictionariesGetTheirContent()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="ThemedBrush" Color="White" />"""));
        cache.Add(_Styles, Dictionary("""<SolidColorBrush x:Key="ThemedBrush" Color="Black" />"""));
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="Padding">4</Thickness>"""));
        XElement page = Page($"""
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light" _0:DT.ResourceDictionarySource="{_Colors}" />
              <ResourceDictionary x:Key="Dark" _0:DT.ResourceDictionarySource="{_Styles}" />
            </ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="HyperlinkResources" _0:DT.ResourceDictionarySource="{_Margins}" />
            """);

        cache.Expander.Expand(page, _PageUri, "document 10");

        foreach ((string key, string expected) in new[] { ("Light", "ThemedBrush"), ("Dark", "ThemedBrush"), ("HyperlinkResources", "Padding") })
        {
            XElement keyed = page.Descendants(_P + "ResourceDictionary").Single(element => KeyOf(element) == key);
            CollectionAssert.AreEqual(new[] { expected }, AllKeys(keyed), key);
        }

        AssertReadableByTheXamlReader(page);
    }

    [TestMethod]
    public void LoadReturnsAPreparedDictionaryAsARootOfItsOwn()
    {
        var cache = new ShadowCache();
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        cache.Add(_Cards, $"""
            <ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}"
                xmlns:d="http://schemas.microsoft.com/expression/blend/2008" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                mc:Ignorable="d" x:Class="My.Library.Cards" _0:DT.RuntimeWidth="10">
              <ResourceDictionary.MergedDictionaries>{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>
              <Style x:Key="CardStyle" TargetType="Border" d:IsDesignTime="True" />
            </ResourceDictionary>
            """);

        ResourceDictionarySourceContent content = cache.Expander.Load(_Cards, documentUri: null, "document 10");

        Assert.AreEqual(ResourceDictionarySourceContentKind.Prepared, content.Kind);
        Assert.AreEqual(_Cards, content.Uri);
        XElement dictionary = content.Dictionary!;
        Assert.IsNull(dictionary.Parent);
        Assert.IsNull(dictionary.Attribute(_X + "Class"));
        Assert.IsNull(dictionary.Attribute(XName.Get("DT.RuntimeWidth", "using:XSurfUwp")));

        // Still a root: mc:Ignorable stays where the reader reads it.
        Assert.AreEqual("d", (string?)dictionary.Attribute(XName.Get("Ignorable", "http://schemas.openxmlformats.org/markup-compatibility/2006")));
        CollectionAssert.AreEquivalent(new[] { "CardPadding", "CardStyle" }, dictionary.Descendants().Select(KeyOf).OfType<string>().ToArray());

        // Each call gets an element of its own.
        Assert.AreNotSame(dictionary, cache.Expander.Load(_Cards, documentUri: null, "document 10").Dictionary);
    }

    [TestMethod]
    public void LoadLeavesASourceOutsideTheSolutionToTheRuntime()
    {
        var cache = new ShadowCache();

        ResourceDictionarySourceContent content = cache.Expander.Load("ms-appx:///Library/Themes/Generic.xaml", documentUri: null, "document 10");

        Assert.AreEqual(ResourceDictionarySourceContentKind.Runtime, content.Kind);
        Assert.AreEqual("ms-appx:///Library/Themes/Generic.xaml", content.Uri);
        Assert.IsNull(content.Dictionary);
        Assert.AreEqual(0, cache.RuntimeLoads.Count);
        Assert.AreEqual(0, cache.Warnings.Count);

        cache.Expander.ReportRuntimeLoadFailure(content.Uri!, "document 10", "Cannot locate resource.");
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "Resource dictionary 'ms-appx:///Library/Themes/Generic.xaml' merged by document 10 is left empty");
    }

    [TestMethod]
    public void LoadOfAnEmptyOrMissingSource()
    {
        var cache = new ShadowCache();

        Assert.AreSame(ResourceDictionarySourceContent.None, cache.Expander.Load("  ", documentUri: null, "document 10"));
        Assert.AreEqual(0, cache.Warnings.Count);

        Assert.AreSame(ResourceDictionarySourceContent.Failed, cache.Expander.Load(_Colors, documentUri: null, "document 10"));
        Assert.AreEqual(1, cache.Warnings.Count);
        StringAssert.Contains(cache.Warnings[0], "does not exist");
    }

    [TestMethod]
    public void ChangedPreparedCopyIsReadAgain()
    {
        var cache = new ShadowCache();
        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="Accent" Color="Red" />"""));
        cache.Expander.Expand(Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>"""), _PageUri, "document 10");

        cache.Add(_Colors, Dictionary("""<SolidColorBrush x:Key="Accent" Color="Blue" />"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Colors)}</ResourceDictionary.MergedDictionaries>""");
        cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual("Blue", (string?)page.Descendants(_P + "SolidColorBrush").Single().Attribute("Color"));
        Assert.AreEqual(2, cache.Parsed.Count);
    }

    [TestMethod]
    public void ChangedNestedCopyInvalidatesTheDictionaryThatMergesIt()
    {
        var cache = new ShadowCache();
        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        cache.Add(_Cards, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>"""));
        cache.Expander.Expand(Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>"""), _PageUri, "document 10");
        cache.Expander.Expand(Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>"""), _PageUri, "document 10");
        Assert.AreEqual(2, cache.Parsed.Count);

        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">24</Thickness>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>""");
        cache.Expander.Expand(page, _PageUri, "document 10");

        Assert.AreEqual("24", page.Descendants(_P + "Thickness").Single().Value);
        Assert.AreEqual(4, cache.Parsed.Count);
    }

    [TestMethod]
    public void NestedCopyThatAppearsLaterIsMergedNextTime()
    {
        var cache = new ShadowCache();
        cache.Add(_Cards, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_Margins)}</ResourceDictionary.MergedDictionaries>"""));
        cache.Expander.Expand(Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>"""), _PageUri, "document 10");

        cache.Add(_Margins, Dictionary("""<Thickness x:Key="CardPadding">12</Thickness>"""));
        XElement page = Page($"""<ResourceDictionary.MergedDictionaries>{Merge(_Cards)}</ResourceDictionary.MergedDictionaries>""");
        cache.Expander.Expand(page, _PageUri, "document 10");

        CollectionAssert.AreEqual(new[] { "CardPadding" }, AllKeys(page));
    }

    private static string Dictionary(string body)
    {
        return $"""<ResourceDictionary xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}">{body}</ResourceDictionary>""";
    }

    private static string Merge(string uri)
    {
        return $"""<ResourceDictionary _0:DT.ResourceDictionarySource="{uri}" />""";
    }

    /// <summary>
    /// A page as Visual Studio prepares it, with <paramref name="resources"/> inside the dictionary of its resources.
    /// </summary>
    private static XElement Page(string resources)
    {
        return XElement.Parse($"""
            <Page xmlns:_0="using:XSurfUwp" xmlns="{_Presentation}" xmlns:x="{_XamlLanguage}">
              <Page.Resources>
                <ResourceDictionary>{resources}</ResourceDictionary>
              </Page.Resources>
              <Border />
            </Page>
            """, LoadOptions.PreserveWhitespace);
    }

    private static string? KeyOf(XElement element)
    {
        return (string?)element.Attribute(_X + "Key");
    }

    /// <summary>
    /// Returns the keys of the entries of a dictionary element.
    /// </summary>
    private static string[] Keys(XElement dictionary)
    {
        return dictionary.Elements().Where(element => !element.Name.LocalName.Contains('.')).Select(KeyOf).OfType<string>().ToArray();
    }

    /// <summary>
    /// Returns the keys of the entries of every dictionary inserted at or below <paramref name="element"/>.
    /// </summary>
    private static string[] AllKeys(XElement element)
    {
        return element.DescendantsAndSelf()
            .Where(ResourceDictionarySourceExpander.IsInserted)
            .Where(entry => entry.Parent is not null && !entry.Name.LocalName.Contains('.') && !entry.Parent.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
            .Select(KeyOf)
            .OfType<string>()
            .ToArray();
    }

    /// <summary>
    /// Checks what the WinUI reader needs from the result: well-formed XML whose prefixes mean, for the names of
    /// elements, what their outermost declaration says, and no design-time source left.
    /// </summary>
    private static void AssertReadableByTheXamlReader(XElement root)
    {
        string xaml = root.ToString(SaveOptions.DisableFormatting);
        XElement reloaded = XElement.Parse(xaml);
        Assert.IsFalse(reloaded.DescendantsAndSelf().Attributes(ResourceDictionarySourceExpander.SourceAttributeName).Any(), xaml);
        List<string> mismatches = XamlFragmentGrafterTests.ResolveAsWinUIReader(xaml);
        Assert.AreEqual(0, mismatches.Count, string.Join(Environment.NewLine, mismatches));
        Assert.IsFalse(reloaded.Descendants().Attributes(XName.Get("Ignorable", "http://schemas.openxmlformats.org/markup-compatibility/2006")).Any(), xaml);
    }

    /// <summary>
    /// Visual Studio's shadow cache in memory, the WinUI runtime as a list of answers, and what the expander reported.
    /// </summary>
    private sealed class ShadowCache
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly XamlConditionService _conditions = new(new XamlConditionProbeSet
        {
            IsApiContractPresent = (name, major, _) => name == _Contract && major <= 10,
        });

        public ShadowCache(int maxDepth = ResourceDictionarySourceOptions.DefaultMaxDepth)
        {
            Expander = new ResourceDictionarySourceExpander(new ResourceDictionarySourceOptions
            {
                ContentDirectory = _ContentDirectory,
                ReadFile = path => _files.TryGetValue(path, out string? text) ? text : null,
                Parse = (xaml, description) =>
                {
                    Parsed.Add(description);
                    XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                    XamlNamespaceRewriter.Rewrite(document, new XamlNamespaceRewriteOptions { SourceXaml = xaml, EvaluateCondition = _conditions.Evaluate });
                    return document;
                },
                IsDictionaryType = name => DictionaryTypes.Contains(name.NamespaceName + ":" + name.LocalName),
                LoadWithRuntime = uri =>
                {
                    RuntimeLoads.Add(uri);
                    return RuntimeErrors.TryGetValue(uri, out string? error) ? error : null;
                },
                MaxDepth = maxDepth,
                ReportWarning = Warnings.Add,
                Report = Reports.Add,
            });
        }

        public ResourceDictionarySourceExpander Expander { get; }

        public List<string> Warnings { get; } = [];

        public List<string> Reports { get; } = [];

        public List<string> Parsed { get; } = [];

        public List<string> RuntimeLoads { get; } = [];

        public Dictionary<string, string> RuntimeErrors { get; } = new(StringComparer.Ordinal);

        public HashSet<string> DictionaryTypes { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Writes the prepared copy of the dictionary <paramref name="uri"/> names, as Visual Studio does.
        /// </summary>
        public void Add(string uri, string xaml)
        {
            _files[ResourceDictionarySourceUri.TryGetLocalPath(uri, _ContentDirectory)!] = xaml;
        }
    }
}
