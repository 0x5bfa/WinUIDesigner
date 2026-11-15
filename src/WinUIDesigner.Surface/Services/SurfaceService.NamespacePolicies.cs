// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Xml.Linq;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies the XAML namespace policies (conditional namespaces and Uno platform prefixes)
/// to XAML before the WinUI XAML reader loads it.
/// </summary>
internal sealed partial class SurfaceService
{
    /// <summary>
    /// Parses prepared XAML once, with line information, and applies the namespace policies to the result.
    /// </summary>
    /// <remarks>
    /// A policy failure never fails the document: it is logged and the XAML is used as prepared.
    /// </remarks>
    private XDocument LoadPreparedXaml(string xaml, int documentId)
    {
        const LoadOptions options = LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo;

        XDocument document = XDocument.Parse(xaml, options);
        try
        {
            XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(
                document,
                CreateNamespaceRewriteOptions(xaml, $"document {documentId}"));
            if (result.Changed)
            {
                WinUIDesignerLogger.LogTrace("Surface", $"Namespace policies rewrote document {documentId}: dropped {result.DroppedElements} element(s) and {result.DroppedAttributes} attribute(s), normalized {result.NormalizedMembers} member(s).");
            }

            return document;
        }
        catch (Exception ex)
        {
            // The rewriter works in place, so a failed run may have left the document half rewritten.
            WinUIDesignerLogger.LogWarning("Surface", $"Namespace policies failed for document {documentId}; continuing with the XAML as prepared.", ex);
            return XDocument.Parse(xaml, options);
        }
    }

    /// <summary>
    /// Applies the namespace policies to a XAML snippet that is parsed on its own, such as a template.
    /// Returns the snippet unchanged when no policy applies or when applying them fails.
    /// </summary>
    private string ApplyNamespacePoliciesToSnippet(string xaml)
    {
        try
        {
            // Nearly every snippet is ordinary XAML. Two text searches rule those out before
            // anything is parsed: a conditional namespace needs '?', a platform namespace its URI.
            if (xaml.IndexOf('?') < 0 && !xaml.Contains(UnoPlatformPrefixes.PlatformUriPrefix, StringComparison.Ordinal))
            {
                return xaml;
            }

            XamlNamespaceScanResult scan = XamlNamespaceScanner.Scan(xaml);
            if (!scan.HasConditionalNamespaces && !scan.HasPlatformNamespaces)
            {
                return xaml;
            }

            XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            XamlNamespaceRewriteResult result = XamlNamespaceRewriter.Rewrite(document, CreateNamespaceRewriteOptions(xaml, "snippet"));
            return result.Changed ? document.ToString(SaveOptions.DisableFormatting) : xaml;
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Surface", "Namespace policies failed for a XAML snippet; parsing it as received.", ex);
            return xaml;
        }
    }

    private XamlNamespaceRewriteOptions CreateNamespaceRewriteOptions(string xaml, string description)
    {
        var options = new XamlNamespaceRewriteOptions
        {
            SourceXaml = xaml,
            Report = message => WinUIDesignerLogger.LogTrace("Surface", $"Namespace policies ({description}): {message}"),
        };

        // Each policy configures itself in its own file. The platform decides first, then the condition.
        ConfigurePlatformPrefixPolicy(options);
        ConfigureConditionalNamespacePolicy(options);

        return options;
    }
}
