// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    /// <summary>
    /// Prepares the evaluation of conditional XAML namespaces before a document is sent to the surface.
    /// </summary>
    private Task PrepareConditionalNamespacesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        // Conditional namespaces are not interpreted yet: without an evaluator their members stay as written.
        return Task.CompletedTask;
    }
}
