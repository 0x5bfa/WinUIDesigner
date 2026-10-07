// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Restores the previously active document when the scope is disposed.
/// </summary>
internal sealed class DocumentScope(Action restore) : IDisposable
{
    private Action? restoreAction = restore;

    public void Dispose()
    {
        var action = restoreAction;
        restoreAction = null;
        action?.Invoke();
    }
}
