// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.IO;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.XmlModify;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents.XamlModify;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Prepares a document a second time so that the elements under an Uno platform prefix reach the surface.
/// </summary>
internal sealed partial class WinUISurfaceProcessMarkupProvider
{
    /// <summary>
    /// Cleans a prepared document again and keeps the elements under an Uno platform prefix in the result.
    /// </summary>
    /// <param name="document">A document Visual Studio has prepared for loading.</param>
    /// <returns>
    /// <see langword="true"/> when the prepared XAML now contains those elements as they were written;
    /// <see langword="false"/> when the document is left as Visual Studio prepared it.
    /// </returns>
    /// <remarks>
    /// The cleaner cannot resolve the type of <c>&lt;android:Border&gt;</c> and replaces the element with a
    /// fallback control without its children, so the surface can no longer tell it from an ordinary element.
    /// With the fallback suppressed the element passes through as text, and the namespace policies of the
    /// surface decide what becomes of it. Input, cleaner context and the order of the steps are those of the
    /// first preparation; only the stand-in for unresolved elements differs.
    /// </remarks>
    public bool ReprepareRawPlatformElements(ISurfaceProcessDocument document)
    {
        if (document is not SurfaceProcessDocument surfaceDocument
            || surfaceDocument.ConstructionState is not (ConstructionState.Prepared or ConstructionState.Loading)
            || SurfaceInfo?.ShadowCacheContent is not { } shadowCache)
        {
            return false;
        }

        IMarkupDocument markupDocument = surfaceDocument.Document;
        IMarkupTextBuffer? textBuffer = markupDocument.TextBuffer;
        IDocumentContext? documentContext = markupDocument.DocumentContext;
        if (textBuffer is null || documentContext is null)
        {
            return false;
        }

        string text = textBuffer.GetText(0, textBuffer.Length);
        IProjectContext project = (documentContext.Project as IProjectContext) ?? Project;

        XamlResult result;
        using (WinUIInstanceBuilderPlatform.KeepUnresolvedElements())
        {
            result = XamlCleaner.Clean(text, GetXamlCleanerContext(project), documentContext);
        }

        // Nothing is touched unless the platform elements really are in the new text.
        if (result.Exception is not null || !XamlNamespaceScanner.Scan(result.Xaml).HasPlatformPrefixedObjectElements)
        {
            return false;
        }

        string shadowCachePath = GetShadowCachePath(surfaceDocument);
        shadowCache.CacheFileFromText(shadowCachePath, result.Xaml);

        // The cache swallows a failed write. The document may only take over the new mapping when the file
        // holds the new text: the mapping has to describe the XAML the surface is going to read.
        if (!HasCachedText(shadowCache, shadowCachePath, result.Xaml))
        {
            return false;
        }

        SetCleaningResult(surfaceDocument, result);
        return true;
    }

    private static bool HasCachedText(IHostShadowCacheContent shadowCache, string shadowCachePath, string expected)
    {
        try
        {
            string cachedPath = shadowCache.GetCachedPath(shadowCachePath);
            return !string.IsNullOrEmpty(cachedPath)
                && File.Exists(cachedPath)
                && string.Equals(File.ReadAllText(cachedPath), expected, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
