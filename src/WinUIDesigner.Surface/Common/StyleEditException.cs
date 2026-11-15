// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Reports an edit of a style in use that the surface could not apply.
/// </summary>
/// <remarks>
/// The message is written for the designer user and travels to Visual Studio as the action error. The service
/// that throws it has already logged the cause once; callers report it without logging it again.
/// </remarks>
internal sealed class StyleEditException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
}
