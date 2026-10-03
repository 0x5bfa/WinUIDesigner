// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

internal sealed class WinUISurfaceProcessMarkupProvider : UwpSurfaceProcessMarkupProvider
{
    public ISurfaceProcessDocument? DesignTimeResources { get; private set; }

    public override void PrepareApplicationDocumentsForLoading(bool ignoreAppXbf)
    {
        base.PrepareApplicationDocumentsForLoading(ignoreAppXbf: true);
        DesignTimeResources = Project.DesignTimeResources is { } resources ? PrepareDocumentForLoading(resources) : null;
    }
}
