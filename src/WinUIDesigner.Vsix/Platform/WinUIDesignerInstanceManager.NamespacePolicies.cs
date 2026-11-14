// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Applies the XAML namespace policies (conditional namespaces and Uno platform prefixes) where documents
/// are built from construction actions. Those carry CLR names only, so the surface cannot tell which prefix
/// a member was written with; the members have to be filtered here, on the document model.
/// </summary>
internal sealed partial class WinUIDesignerInstanceManager
{
    // Shared with the instance-builder platform of this pipeline, so both judge a node the same way.
    private readonly XamlMemberDispositionService _memberDispositions =
        (platform as WinUIInstanceBuilderPlatform)?.MemberDispositions ?? new XamlMemberDispositionService();

    public override bool ShouldIgnoreProperty(IInstanceBuilderContext context, DocumentCompositeNode parentNode, DocumentNode newNode, IProperty property, out bool shouldLogError)
    {
        // A property written under a dropped prefix does not exist in the previewed document.
        if (_memberDispositions.GetPropertyDisposition(parentNode, property) == XamlPrefixDisposition.Drop)
        {
            ReportValueLostToDroppedProperty(parentNode, property);
            shouldLogError = false;
            return true;
        }

        return base.ShouldIgnoreProperty(context, parentNode, newNode, property, out shouldLogError);
    }

    /// <summary>
    /// Lets the namespace policies prepare for the documents that are about to be sent to the surface.
    /// A policy that fails to prepare is logged and does not prevent the documents from loading.
    /// </summary>
    private async Task PrepareNamespacePoliciesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        try
        {
            // Each policy prepares itself in its own file. Continue on the caller's context:
            // the request is built from designer state right afterwards.
            await PreparePlatformPrefixesAsync(targetDocument, preparedDocuments).ConfigureAwait(true);
            await PrepareConditionalNamespacesAsync(targetDocument, preparedDocuments).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Platform", "Preparing the XAML namespace policies failed; the documents load without them.", ex);
        }
    }
}
