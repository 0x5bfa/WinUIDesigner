// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    /// <summary>
    /// Configures which Uno platform the surface previews.
    /// </summary>
    private void ConfigurePlatformPrefixPolicy(XamlNamespaceRewriteOptions options)
    {
        // Platform prefixes are not interpreted yet: the options keep their default target platform.
    }
}
