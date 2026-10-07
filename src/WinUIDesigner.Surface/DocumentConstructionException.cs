// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;

namespace WinUIDesigner.Surface;

/// <summary>
/// Reports which XAML action failed while constructing a document.
/// </summary>
internal sealed class DocumentConstructionException(Exception cause, XamlAction action)
    : Exception("A document construction action failed.", cause)
{
    public string SerializedErrors { get; } =
        ActionErrorJsonSerializer.Serialize([new ActionError { XamlAction = action, Error = cause.ToString() }]);
}
