// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

namespace WinUIDesigner.Surface.Designer.Surface;

/// <summary>
/// Stores a visual element handle and its position in the parent topology.
/// </summary>
internal readonly record struct VisualTreeTopologyEntry(long Handle, long ParentHandle, uint ChildIndex);
