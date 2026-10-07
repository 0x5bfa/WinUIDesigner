// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Stores the content and document bounds used to detect surface changes.
/// </summary>
internal readonly record struct SurfaceBoundsSnapshot(Rect ContentBounds, Rect DocumentBounds);
