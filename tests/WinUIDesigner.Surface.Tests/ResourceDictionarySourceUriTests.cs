// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Markup;

/// <summary>
/// The forms a resource dictionary source takes on its way from Visual Studio to the surface. The design-time URIs
/// are copied from prepared XAML that Visual Studio 18 wrote for a WinUI application and its class libraries.
/// </summary>
[TestClass]
public sealed class ResourceDictionarySourceUriTests
{
    private const string _ContentDirectory = @"C:\DesignerCache\1-1868410451x64DA";

    [TestMethod]
    [DataRow("ms-appx:///Ecierge.Console.UI/~vs]~359530~Resources.xaml", 359530)]
    [DataRow("ms-appx:///Ecierge.Console.UI/Styles/~vs]~359453~AutoSuggestBoxStyle.xaml", 359453)]
    [DataRow("ms-appx:///~vs]~359056~App.xaml", 359056)]
    [DataRow("ms-appx:///WinUIDesignerSampleApp/Themes/~vs]~7~ConditionalResources.xaml", 7)]
    [DataRow(@"C:\DesignerCache\Themes\~VS]~42~A.xaml", 42)]
    public void DesignTimeUriNamesTheDocumentOfVisualStudio(string value, int expected)
    {
        Assert.IsTrue(ResourceDictionarySourceUri.TryGetDesignTimeDocumentId(value, out int documentId));
        Assert.AreEqual(expected, documentId);
        Assert.IsTrue(ResourceDictionarySourceUri.IsDesignTimeUri(value));
    }

    [TestMethod]
    [DataRow("ms-appx:///Themes/ConditionalResources.xaml")]
    [DataRow("ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Segmented/Segmented.xaml")]
    [DataRow("/Themes/~vs]~abc~X.xaml")]
    [DataRow("~vs]~12")]
    [DataRow("~vs]~~X.xaml")]
    [DataRow("")]
    [DataRow(null)]
    public void OtherValuesAreNoDesignTimeUris(string? value)
    {
        Assert.IsFalse(ResourceDictionarySourceUri.TryGetDesignTimeDocumentId(value, out int documentId));
        Assert.AreEqual(0, documentId);
    }

    [TestMethod]
    public void TheLastMarkerNamesTheDocument()
    {
        // Visual Studio reads the id after the last marker, and so does the surface.
        Assert.IsTrue(ResourceDictionarySourceUri.TryGetDesignTimeDocumentId("ms-appx:///~vs]~1~Folder/~vs]~2~A.xaml", out int documentId));
        Assert.AreEqual(2, documentId);
    }

    [TestMethod]
    [DataRow("ms-appx:///Ecierge.Console.UI/~vs]~359530~Resources.xaml", "Resources.xaml")]
    [DataRow("ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Segmented/Segmented.xaml", "Segmented.xaml")]
    [DataRow("ms-appx:///Themes/My%20Dictionary.xaml", "My Dictionary.xaml")]
    [DataRow("Colors.xaml", "Colors.xaml")]
    public void DisplayNameIsTheFileNameOfTheProject(string value, string expected)
    {
        Assert.AreEqual(expected, ResourceDictionarySourceUri.GetDisplayName(value));
    }

    [TestMethod]
    [DataRow("ms-appx:///Ecierge.Console.UI/~vs]~359530~Resources.xaml")]
    [DataRow("ms-appx:///Some.Project.Name/Path/File.xaml")]
    [DataRow("ms-appx:///Themes/ConditionalResources.xaml")]
    [DataRow("ms-resource:///Files/Themes/A.xaml")]
    public void AbsoluteSourceIsKeptAsWritten(string value)
    {
        Assert.AreEqual(value, ResourceDictionarySourceUri.Resolve(value, "ms-appx:///Views/~vs]~12~Page.xaml"));
        Assert.AreEqual(value, ResourceDictionarySourceUri.Resolve("  " + value + " ", baseUri: null));
    }

    [TestMethod]
    [DataRow("/Themes/X.xaml", "ms-appx:///Views/~vs]~12~Page.xaml", "ms-appx:///Themes/X.xaml")]
    [DataRow("/Themes/X.xaml", null, "ms-appx:///Themes/X.xaml")]
    [DataRow("X.xaml", "ms-appx:///Views/~vs]~12~Page.xaml", "ms-appx:///Views/X.xaml")]
    [DataRow("Themes/X.xaml", "ms-appx:///Views/~vs]~12~Page.xaml", "ms-appx:///Views/Themes/X.xaml")]
    [DataRow("../Shared/Y.xaml", "ms-appx:///Views/Sub/~vs]~12~Page.xaml", "ms-appx:///Views/Shared/Y.xaml")]
    [DataRow("../../../Z.xaml", "ms-appx:///Views/~vs]~12~Page.xaml", "ms-appx:///Z.xaml")]
    [DataRow("X.xaml", null, "ms-appx:///X.xaml")]
    [DataRow("X.xaml", "file:///C:/Cache/~vs]~3~Page.xaml", "ms-appx:///X.xaml")]
    [DataRow("My Dictionary.xaml", "ms-appx:///Themes/~vs]~3~Outer.xaml", "ms-appx:///Themes/My%20Dictionary.xaml")]
    public void RelativeSourceIsResolvedAsTheRuntimeResolvesIt(string value, string? baseUri, string expected)
    {
        Assert.AreEqual(expected, ResourceDictionarySourceUri.Resolve(value, baseUri));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void EmptySourceResolvesToNothing(string? value)
    {
        Assert.IsNull(ResourceDictionarySourceUri.Resolve(value, "ms-appx:///~vs]~1~Page.xaml"));
    }

    [TestMethod]
    public void PreparedCopyIsBelowTheContentDirectory()
    {
        Assert.AreEqual(
            _ContentDirectory + @"\Ecierge.Console.UI\~vs]~359530~Resources.xaml",
            ResourceDictionarySourceUri.TryGetLocalPath("ms-appx:///Ecierge.Console.UI/~vs]~359530~Resources.xaml", _ContentDirectory));
        Assert.AreEqual(
            _ContentDirectory + @"\~vs]~359056~App.xaml",
            ResourceDictionarySourceUri.TryGetLocalPath("ms-appx:///~vs]~359056~App.xaml", _ContentDirectory + @"\"));
        Assert.AreEqual(
            _ContentDirectory + @"\My Folder\~vs]~2~A B.xaml",
            ResourceDictionarySourceUri.TryGetLocalPath("ms-appx:///My%20Folder/~vs]~2~A%20B.xaml", _ContentDirectory));
    }

    [TestMethod]
    public void FileUriIsItsLocalPath()
    {
        Assert.AreEqual(@"C:\Elsewhere\~vs]~1~A.xaml", ResourceDictionarySourceUri.TryGetLocalPath("file:///C:/Elsewhere/~vs]~1~A.xaml", _ContentDirectory));
    }

    [TestMethod]
    [DataRow("ms-appx:///../../outside.xaml")]
    [DataRow("ms-appx:///")]
    [DataRow("http://example.com/Themes/A.xaml")]
    [DataRow("ms-resource:///Files/Themes/A.xaml")]
    [DataRow("Themes/A.xaml")]
    [DataRow("/Themes/A.xaml")]
    public void NoOtherUriNamesAFileOfTheDesigner(string uri)
    {
        string? path = ResourceDictionarySourceUri.TryGetLocalPath(uri, _ContentDirectory);

        // ".." cannot climb out of an ms-appx URI; what remains is either below the content directory or nothing.
        Assert.IsTrue(path is null || path.StartsWith(_ContentDirectory + @"\", StringComparison.OrdinalIgnoreCase), path);
        if (!uri.StartsWith("ms-appx:///..", StringComparison.Ordinal))
        {
            Assert.IsNull(path);
        }
    }

    [TestMethod]
    [DataRow("ResourceDictionarySource:XSurfUwp.DT", true)]
    [DataRow("ResourceDictionarySource:XSurfUwp.DT, UwpSurface", true)]
    [DataRow(" ResourceDictionarySource : XSurfUwp.DT ", true)]
    [DataRow("Source:Microsoft.UI.Xaml.ResourceDictionary", false)]
    [DataRow("RuntimeWidth:XSurfUwp.DT", false)]
    [DataRow("ResourceDictionarySource:XSurfUwp.DTX", false)]
    [DataRow("ResourceDictionarySource", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void ConstructionActionPropertyIsRecognized(string? fullPropertyName, bool expected)
    {
        Assert.AreEqual(expected, ResourceDictionarySourceUri.IsSourceProperty(fullPropertyName));
    }
}
