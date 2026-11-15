// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    /// <summary>
    /// Prepares the Uno platform the designer previews before a document is sent to the surface.
    /// </summary>
    private Task PreparePlatformPrefixesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        // Platform prefixes are not interpreted yet: the default target platform stays in effect.
        return Task.CompletedTask;
    }
}
