// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.UI.Xaml;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Stores a visible element considered by the hit-test traversal.
/// </summary>
internal readonly record struct HitCandidate(UIElement Element, bool IsVisible);
