// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Options;

[Guid("7D8E73D8-A714-43D1-8AF4-C6A6A3DA9EC2")]
public sealed class WinUIDesignerOptionsPage : DialogPage
{
    private WinUIDesignerOptionsControl? _window;
    private bool _enableVerboseLogging;

    [Category("Diagnostics")]
    [DisplayName("Enable verbose logging")]
    [Description("Record detailed designer diagnostics. Visual Studio logging updates when applied; newly started surface processes receive this setting.")]
    public bool EnableVerboseLogging
    {
        get => _enableVerboseLogging;
        set
        {
            _enableVerboseLogging = value;
            _window?.UpdateVerboseLogging(value);
        }
    }

    protected override IWin32Window Window
    {
        get
        {
            _window ??= new WinUIDesignerOptionsControl(this);
            _window.UpdateVerboseLogging(EnableVerboseLogging);
            return _window;
        }
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);
        WinUIDesignerLogger.ConfigureVerboseLogging(EnableVerboseLogging);
    }
}
