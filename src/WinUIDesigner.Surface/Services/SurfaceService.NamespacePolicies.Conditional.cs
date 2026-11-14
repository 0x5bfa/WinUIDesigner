// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    /// <summary>
    /// Configures how conditional XAML namespaces are evaluated in the surface process.
    /// </summary>
    private void ConfigureConditionalNamespacePolicy(XamlNamespaceRewriteOptions options)
    {
        // Conditional namespaces are not interpreted yet: without an evaluator their members stay as written.
    }
}
