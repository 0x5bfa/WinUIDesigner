// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// Covers which Uno platform prefixes apply to which previewed platform. The verdict comes from the prefix:
/// <c>win</c>, <c>not_android</c> and the default namespace all name the presentation namespace.
/// </summary>
[TestClass]
public sealed class UnoPlatformPrefixesTests
{
    private const string _Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // Every positive prefix of the "Available prefixes" tables of the Uno documentation, in all of its versions.
    private static readonly string[] _PositivePrefixes =
    [
        "win", "winappsdk", "android", "ios", "tvos", "macos", "wasm", "desktop",
        "skia", "androidskia", "iosskia", "wasmskia", "netstdref",
    ];

    [TestMethod]
    public void DesignerPreviewsWindowsByDefault()
    {
        Assert.AreEqual("win", new XamlNamespaceRewriteOptions().TargetPlatform);
        Assert.AreEqual("win", new XamlNamespaceRewriteOptions { TargetPlatform = string.Empty }.TargetPlatform);
        Assert.AreEqual("android", new XamlNamespaceRewriteOptions { TargetPlatform = "android" }.TargetPlatform);
    }

    [TestMethod]
    [DataRow("http://uno.ui/android", true)]
    [DataRow("http://uno.ui/not_win", true)]
    [DataRow("http://uno.ui/android#using:My.Droid", true)]
    [DataRow("http://uno.ui", false)]
    [DataRow("HTTP://UNO.UI/android", false)]
    [DataRow(_Presentation, false)]
    [DataRow("using:My.App", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void PlatformNamespaceIsRecognizedByItsUri(string? namespaceUri, bool expected)
    {
        Assert.AreEqual(expected, UnoPlatformPrefixes.IsPlatformUri(namespaceUri));
    }

    [TestMethod]
    public void EveryDocumentedPrefixIsRecognizedWithItsNegation()
    {
        foreach (string prefix in _PositivePrefixes)
        {
            Assert.IsTrue(UnoPlatformPrefixes.TryParse(prefix, out string platform, out bool isNegated), prefix);
            Assert.AreEqual(prefix, platform);
            Assert.IsFalse(isNegated, prefix);

            Assert.IsTrue(UnoPlatformPrefixes.TryParse("not_" + prefix, out platform, out isNegated), "not_" + prefix);
            Assert.AreEqual(prefix, platform);
            Assert.IsTrue(isNegated, "not_" + prefix);
        }
    }

    [TestMethod]
    public void XamarinStandsForEveryPlatformButWindows()
    {
        Assert.IsTrue(UnoPlatformPrefixes.TryParse("xamarin", out string platform, out bool isNegated));
        Assert.AreEqual("win", platform);
        Assert.IsTrue(isNegated);

        Assert.IsFalse(UnoPlatformPrefixes.IsIncluded("xamarin", "win"));
        foreach (string target in new[] { "android", "ios", "wasm", "macos", "skia", "desktop" })
        {
            Assert.IsTrue(UnoPlatformPrefixes.IsIncluded("xamarin", target), target);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("x")]
    [DataRow("d")]
    [DataRow("local")]
    [DataRow("Win")]
    [DataRow("ANDROID")]
    [DataRow("android2")]
    [DataRow("androidNamespace1")]
    [DataRow("windows")]
    [DataRow("not_")]
    [DataRow("not_used")]
    [DataRow("not_xamarin")]
    [DataRow("not_not_android")]
    [DataRow("NOT_android")]
    [DataRow("notandroid")]
    public void OtherPrefixesAreNotPlatformPrefixes(string? prefix)
    {
        // Uno compares the prefix exactly, so a variant spelling is an ordinary prefix.
        Assert.IsFalse(UnoPlatformPrefixes.TryParse(prefix, out string platform, out bool isNegated));
        Assert.AreEqual(string.Empty, platform);
        Assert.IsFalse(isNegated);
        Assert.IsFalse(UnoPlatformPrefixes.IsIncluded(prefix, "win"));
    }

    [TestMethod]
    // Included on Windows: the members become plain members.
    [DataRow("win", _Presentation, false, "Keep")]
    [DataRow("winappsdk", _Presentation, false, "Keep")]
    [DataRow("not_android", _Presentation, false, "Keep")]
    [DataRow("not_ios", _Presentation, false, "Keep")]
    [DataRow("not_tvos", _Presentation, false, "Keep")]
    [DataRow("not_macos", _Presentation, false, "Keep")]
    [DataRow("not_wasm", _Presentation, false, "Keep")]
    [DataRow("not_desktop", _Presentation, false, "Keep")]
    [DataRow("not_skia", _Presentation, false, "Keep")]
    [DataRow("not_androidskia", _Presentation, false, "Keep")]
    [DataRow("not_iosskia", _Presentation, false, "Keep")]
    [DataRow("not_wasmskia", _Presentation, false, "Keep")]
    // Excluded on Windows: the members are dropped.
    [DataRow("not_win", "http://uno.ui/not_win", true, "Drop")]
    [DataRow("not_winappsdk", "http://uno.ui/not_winappsdk", true, "Drop")]
    [DataRow("xamarin", "http://uno.ui/xamarin", true, "Drop")]
    [DataRow("android", "http://uno.ui/android", true, "Drop")]
    [DataRow("ios", "http://uno.ui/ios", true, "Drop")]
    [DataRow("tvos", "http://uno.ui/tvos", true, "Drop")]
    [DataRow("macos", "http://uno.ui/macos", true, "Drop")]
    [DataRow("wasm", "http://uno.ui/wasm", true, "Drop")]
    [DataRow("desktop", "http://uno.ui/desktop", true, "Drop")]
    [DataRow("skia", "http://uno.ui/skia", true, "Drop")]
    [DataRow("androidskia", "http://uno.ui/androidskia", true, "Drop")]
    [DataRow("iosskia", "http://uno.ui/iosskia", true, "Drop")]
    [DataRow("wasmskia", "http://uno.ui/wasmskia", true, "Drop")]
    [DataRow("netstdref", "http://uno.ui/netstdref", true, "Drop")]
    // Documented with an Uno namespace, which the Windows XAML compiler ignores like the one of its positive form.
    [DataRow("not_netstdref", "http://uno.ui/not_netstdref", true, "Drop")]
    public void WindowsKeepsThePrefixesThatIncludeItAndDropsTheOthers(string prefix, string uri, bool isIgnorable, string expected)
    {
        Assert.AreEqual(Enum.Parse<XamlPrefixDisposition>(expected), Classify(prefix, uri, isIgnorable, "win"));

        // "winappsdk" is the same target under its Uno 7 name, and no target at all means the default one.
        Assert.AreEqual(Enum.Parse<XamlPrefixDisposition>(expected), Classify(prefix, uri, isIgnorable, "winappsdk"));
        Assert.AreEqual(Enum.Parse<XamlPrefixDisposition>(expected), Classify(prefix, uri, isIgnorable, string.Empty));
    }

    [TestMethod]
    // target, then the positive prefixes whose members are part of a build for it
    [DataRow("win", "win winappsdk")]
    [DataRow("android", "android")]
    [DataRow("ios", "ios")]
    [DataRow("tvos", "tvos")]
    [DataRow("macos", "macos")]
    [DataRow("wasm", "wasm netstdref")]
    [DataRow("desktop", "desktop skia netstdref")]
    [DataRow("skia", "skia netstdref")]
    [DataRow("androidskia", "androidskia skia netstdref")]
    [DataRow("iosskia", "iosskia skia netstdref")]
    [DataRow("wasmskia", "wasmskia skia netstdref")]
    [DataRow("netstdref", "netstdref")]
    // A target the table does not know, like a plain netX.0 library, sees no positive prefix at all.
    [DataRow("net9.0", "")]
    public void PreviewingAnotherPlatformIsAMatterOfTheTarget(string target, string included)
    {
        string[] expected = included.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string prefix in _PositivePrefixes)
        {
            bool isIncluded = expected.Contains(prefix);
            Assert.AreEqual(isIncluded, UnoPlatformPrefixes.IsIncluded(prefix, target), $"{prefix} on {target}");
            AssertClassifiedAsDocumented(prefix, target, isIncluded);

            // The negated prefix is the exact opposite on every target.
            Assert.AreEqual(!isIncluded, UnoPlatformPrefixes.IsIncluded("not_" + prefix, target), $"not_{prefix} on {target}");
            AssertClassifiedAsDocumented("not_" + prefix, target, !isIncluded);
        }
    }

    [TestMethod]
    // The reference build of a library is neither Windows nor one of the native platforms.
    [DataRow("not_netstdref", "http://uno.ui/not_netstdref", "win", "Drop")]
    [DataRow("not_netstdref", "http://uno.ui/not_netstdref", "android", "Keep")]
    [DataRow("not_netstdref", "http://uno.ui/not_netstdref", "wasm", "Drop")]
    // A prefix that includes Windows but is bound to a namespace the Windows XAML compiler cannot resolve.
    [DataRow("not_android", "http://uno.ui/not_android", "win", "Drop")]
    [DataRow("not_android", "http://uno.ui/not_android", "ios", "Keep")]
    [DataRow("not_android", "http://nventive.com/not_android", "win", "Drop")]
    [DataRow("win", "http://uno.ui/win", "win", "Drop")]
    // Uno's compiler includes a prefix whatever its namespace is.
    [DataRow("android", _Presentation, "android", "Keep")]
    [DataRow("android", "http://nventive.com/android", "android", "Keep")]
    // A condition is not part of the namespace the prefix is bound to.
    [DataRow("win", _Presentation + "?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)", "win", "Keep")]
    public void OnWindowsAPrefixIsOnlyKeptInTheNamespaceTheWindowsCompilerResolves(string prefix, string uri, string target, string expected)
    {
        Assert.AreEqual(Enum.Parse<XamlPrefixDisposition>(expected), Classify(prefix, uri, isIgnorable: true, target));
    }

    [TestMethod]
    public void SameDeclarationsAreClassifiedDifferentlyForAndroid()
    {
        (string Prefix, string Uri, bool IsIgnorable)[] declarations =
        [
            ("win", _Presentation, false),
            ("not_android", _Presentation, false),
            ("not_win", "http://uno.ui/not_win", true),
            ("android", "http://uno.ui/android", true),
            ("ios", "http://uno.ui/ios", true),
        ];

        CollectionAssert.AreEqual(
            new[] { XamlPrefixDisposition.Keep, XamlPrefixDisposition.Keep, XamlPrefixDisposition.Drop, XamlPrefixDisposition.Drop, XamlPrefixDisposition.Drop },
            declarations.Select(declaration => Classify(declaration.Prefix, declaration.Uri, declaration.IsIgnorable, "win")).ToArray());
        CollectionAssert.AreEqual(
            new[] { XamlPrefixDisposition.Drop, XamlPrefixDisposition.Drop, XamlPrefixDisposition.Keep, XamlPrefixDisposition.Keep, XamlPrefixDisposition.Drop },
            declarations.Select(declaration => Classify(declaration.Prefix, declaration.Uri, declaration.IsIgnorable, "android")).ToArray());
    }

    [TestMethod]
    [DataRow("android", "http://uno.ui/android")]
    [DataRow("not_win", "http://uno.ui/not_win")]
    [DataRow("skia", "http://uno.ui/skia#using:My.Skia")]
    // The namespace of an excluded prefix is arbitrary: Uno never looks at it.
    [DataRow("ios", "http://nventive.com/ios")]
    [DataRow("not_win", _Presentation)]
    public void PlatformPrefixDecidesWhetherOrNotItIsIgnorable(string prefix, string uri)
    {
        // A platform prefix that is missing from mc:Ignorable is dropped all the same; the caller reports it.
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify(prefix, uri, isIgnorable: true, "win"));
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify(prefix, uri, isIgnorable: false, "win"));
    }

    [TestMethod]
    public void WindowsPrefixIsDroppedOnAnotherPlatformAlthoughItNamesThePresentationNamespace()
    {
        Assert.AreEqual(XamlPrefixDisposition.Keep, Classify("win", _Presentation, isIgnorable: false, "win"));
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify("win", _Presentation, isIgnorable: false, "android"));
        Assert.AreEqual(XamlPrefixDisposition.Keep, Classify("not_android", _Presentation, isIgnorable: false, "win"));
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify("not_android", _Presentation, isIgnorable: false, "android"));

        // The default namespace is the same namespace and is never a platform prefix.
        Assert.AreEqual(XamlPrefixDisposition.Leave, Classify(string.Empty, _Presentation, isIgnorable: false, "win"));
        Assert.AreEqual(XamlPrefixDisposition.Leave, Classify(string.Empty, _Presentation, isIgnorable: false, "android"));
    }

    [TestMethod]
    [DataRow("win")]
    [DataRow("android")]
    [DataRow("skia")]
    [DataRow("net9.0")]
    public void UnknownUnoPrefixIsDroppedOnlyWhenItIsIgnorable(string target)
    {
        // Nothing resolves an Uno namespace: when it is ignorable, every XAML reader skips its members.
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify("legacy", "http://uno.ui/legacy", isIgnorable: true, target));
        Assert.AreEqual(XamlPrefixDisposition.Drop, Classify("tvosskia", "http://uno.ui/tvosskia#using:My.Tv", isIgnorable: true, target));

        // When it is not, the XAML reader rejects the member, as the XAML compiler does.
        Assert.AreEqual(XamlPrefixDisposition.Leave, Classify("legacy", "http://uno.ui/legacy", isIgnorable: false, target));
    }

    [TestMethod]
    [DataRow("d", "http://schemas.microsoft.com/expression/blend/2008", true)]
    [DataRow("d", "using:XSurfUwp", true)]
    [DataRow("_0", "using:XSurfUwp.Fallback", false)]
    [DataRow("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006", false)]
    [DataRow("x", "http://schemas.microsoft.com/winfx/2006/xaml", false)]
    [DataRow("local", "using:My.App", false)]
    [DataRow("ignored", "http://example.com/whatever", true)]
    public void PrefixThatIsNeitherAPlatformNorAnUnoNamespaceIsLeftAlone(string prefix, string uri, bool isIgnorable)
    {
        foreach (string target in new[] { "win", "android", "skia" })
        {
            Assert.AreEqual(XamlPrefixDisposition.Leave, Classify(prefix, uri, isIgnorable, target), target);
        }
    }

    [TestMethod]
    [DataRow("skia", "using:SkiaSharp.Views.Windows")]
    [DataRow("desktop", "using:My.App.Desktop")]
    [DataRow("win", "using:My.App.Win")]
    [DataRow("android", "clr-namespace:My.App.Droid;assembly=My.App")]
    [DataRow("not_win", "using:My.App.Everywhere")]
    public void ClrNamespaceUnderAPlatformNameIsAnOrdinaryNamespace(string prefix, string uri)
    {
        // The prefix names real types here, in an Uno application or in any other one.
        foreach (string target in new[] { "win", "android", "skia", "desktop" })
        {
            Assert.AreEqual(XamlPrefixDisposition.Leave, Classify(prefix, uri, isIgnorable: false, target), target);
            Assert.AreEqual(XamlPrefixDisposition.Leave, Classify(prefix, uri, isIgnorable: true, target), target);
        }
    }

    [TestMethod]
    public void UsingSuffixNamesClrNamespacesAndLeavesTheVerdictToThePrefix()
    {
        XamlNamespaceUri uri = XamlNamespaceUri.Parse("http://uno.ui/android#using:My.Droid;My.Other");

        Assert.AreEqual("http://uno.ui/android", uri.BaseUri);
        CollectionAssert.AreEqual(new[] { "My.Droid", "My.Other" }, uri.UsingNamespaces.ToArray());
        Assert.AreEqual(XamlPrefixDisposition.Drop, UnoPlatformPrefixes.Classify("android", uri, isIgnorable: true, "win"));
        Assert.AreEqual(XamlPrefixDisposition.Keep, UnoPlatformPrefixes.Classify("android", uri, isIgnorable: true, "android"));
    }

    [TestMethod]
    public void PlatformVerdictReachesTheCombinedPolicies()
    {
        Assert.AreEqual(
            XamlPrefixDisposition.Keep,
            XamlNamespacePolicies.Classify("win", XamlNamespaceUri.Parse(_Presentation), isIgnorable: false, "win", evaluateCondition: null));
        Assert.AreEqual(
            XamlPrefixDisposition.Drop,
            XamlNamespacePolicies.Classify("android", XamlNamespaceUri.Parse("http://uno.ui/android"), isIgnorable: true, "win", evaluateCondition: null));

        // An excluded platform wins over whatever a condition says.
        Assert.AreEqual(
            XamlPrefixDisposition.Drop,
            XamlNamespacePolicies.Classify(
                "not_win",
                XamlNamespaceUri.Parse(_Presentation + "?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)"),
                isIgnorable: true,
                "win",
                evaluateCondition: _ => true));
    }

    [TestMethod]
    public void DocumentWithPlatformPrefixesNeedsWork()
    {
        XamlNamespaceScanResult windowsOnly = XamlNamespaceScanner.Scan($"<Page xmlns=\"{_Presentation}\" xmlns:win=\"{_Presentation}\" win:Tag=\"1\" />");
        XamlNamespaceScanResult ordinary = XamlNamespaceScanner.Scan($"<Page xmlns=\"{_Presentation}\" xmlns:local=\"using:My.App\" />");

        Assert.IsTrue(XamlNamespacePolicies.MayAffect(windowsOnly, "win", evaluateCondition: null));
        Assert.IsTrue(XamlNamespacePolicies.MayAffect(windowsOnly, "android", evaluateCondition: null));
        Assert.IsFalse(XamlNamespacePolicies.MayAffect(ordinary, "win", evaluateCondition: null));
    }

    [TestMethod]
    public void UnoNamespaceThatIsMissingFromIgnorableIsFound()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}"
                  xmlns:win="{_Presentation}"
                  xmlns:android="http://uno.ui/android"
                  xmlns:ios="http://uno.ui/ios#using:My.Ios"
                  xmlns:not_win="http://uno.ui/not_win"
                  xmlns:skia="using:SkiaSharp.Views.Windows"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  mc:Ignorable="android">
              <Grid xmlns:legacy="http://uno.ui/legacy" />
            </Page>
            """;

        IReadOnlyList<XamlNamespaceDeclaration> missing = UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml));

        CollectionAssert.AreEqual(new[] { "ios", "not_win", "legacy" }, missing.Select(declaration => declaration.Prefix).ToArray());
        CollectionAssert.AreEqual(new[] { 4, 5, 9 }, missing.Select(declaration => declaration.LineNumber).ToArray());
    }

    [TestMethod]
    public void DocumentThatListsItsUnoNamespacesHasNothingMissing()
    {
        string xaml = $"""
            <Page xmlns="{_Presentation}"
                  xmlns:win="{_Presentation}"
                  xmlns:not_android="{_Presentation}"
                  xmlns:android="http://uno.ui/android"
                  xmlns:not_win="http://uno.ui/not_win"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  mc:Ignorable="android not_win" />
            """;

        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanner.Scan(xaml)).Count);
        Assert.AreEqual(0, UnoPlatformPrefixes.FindMissingIgnorable(XamlNamespaceScanResult.Empty).Count);
    }

    private static XamlPrefixDisposition Classify(string prefix, string uri, bool isIgnorable, string target)
    {
        return UnoPlatformPrefixes.Classify(prefix, XamlNamespaceUri.Parse(uri), isIgnorable, target);
    }

    /// <summary>
    /// Classifies a prefix bound to the namespace the Uno documentation gives it: the presentation namespace
    /// for what Windows includes, <c>http://uno.ui/&lt;prefix&gt;</c> listed in <c>mc:Ignorable</c> otherwise.
    /// </summary>
    private static void AssertClassifiedAsDocumented(string prefix, string target, bool isIncluded)
    {
        bool isDocumentedInPresentation = prefix is "win" or "winappsdk"
            || (prefix.StartsWith("not_", StringComparison.Ordinal) && prefix is not ("not_win" or "not_winappsdk" or "not_netstdref"));
        string uri = isDocumentedInPresentation ? _Presentation : "http://uno.ui/" + prefix;

        // Uno includes a prefix whatever its namespace is; the Windows XAML compiler needs one it resolves.
        bool isKept = isIncluded && (target != "win" || isDocumentedInPresentation);

        Assert.AreEqual(
            isKept ? XamlPrefixDisposition.Keep : XamlPrefixDisposition.Drop,
            Classify(prefix, uri, isIgnorable: !isDocumentedInPresentation, target),
            $"{prefix} on {target}");
    }
}
