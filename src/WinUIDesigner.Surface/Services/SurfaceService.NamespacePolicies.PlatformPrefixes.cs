// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    /// <summary>
    /// Configures which Uno platform the surface previews.
    /// </summary>
    private void ConfigurePlatformPrefixPolicy(XamlNamespaceRewriteOptions options)
    {
        // The surface is a WinUI process and previews the Windows flavor of the markup: members under win:
        // and not_android: become plain members, members under android: or not_win: are dropped.
        // Previewing another platform means setting another target here; the policy does not change.
        options.TargetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;
    }
}
