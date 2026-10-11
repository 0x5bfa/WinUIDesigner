// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.Shell;
using System.Collections.Generic;
using System.Drawing;

namespace WinUIDesigner.Toolbox;

internal sealed class XamlToolboxItemInfo(string displayName, Bitmap icon, Color transparentColor, IEnumerable<KeyValuePair<string, string>> extraInfo, IDictionary<string, string> properties)
    : IToolboxItemInfo
{
    private IDictionary<string, string> properties = properties;

    public string DisplayName { get; private set; } = displayName;

    public object Icon { get; private set; } = icon;

    public object TransparentColor { get; private set; } = transparentColor;

    public IEnumerable<KeyValuePair<string, string>> ExtraInfo { get; private set; } = extraInfo;

    public string GetPropertyValue(string name)
    {
        return properties[name];
    }
}
