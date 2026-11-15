// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using WinUIDesigner.Platform;

namespace WinUIDesigner.Options;

[Guid("7D8E73D8-A714-43D1-8AF4-C6A6A3DA9EC2")]
public sealed class WinUIDesignerOptionsPage : DialogPage
{
    private WinUIDesignerOptionsControl? _window;
    private bool _enableVerboseLogging;
    private bool _showConditionalXamlSwitches = true;

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

    [Category("Designer")]
    [DisplayName("Show conditional XAML switches")]
    [Description("Show the Conditions drop-down in the designer's device toolbar, which lets you choose whether each condition of the conditional XAML namespaces of a document holds in the preview. Turning it off hides the drop-down at once; turning it on takes effect in designers opened afterwards.")]
    [DefaultValue(true)]
    public bool ShowConditionalXamlSwitches
    {
        get => _showConditionalXamlSwitches;
        set
        {
            _showConditionalXamlSwitches = value;
            _window?.UpdateShowConditionalXamlSwitches(value);
        }
    }

    protected override IWin32Window Window
    {
        get
        {
            _window ??= new WinUIDesignerOptionsControl(this);
            _window.UpdateVerboseLogging(EnableVerboseLogging);
            _window.UpdateShowConditionalXamlSwitches(ShowConditionalXamlSwitches);
            return _window;
        }
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);
        WinUIDesignerLogger.ConfigureVerboseLogging(EnableVerboseLogging);
        ApplyDesignerSettings();
    }

    /// <summary>
    /// Puts the settings of the designer's user interface into effect. Never throws: the package loads, and the
    /// options apply, whatever happens to the user interface.
    /// </summary>
    internal void ApplyDesignerSettings()
    {
        try
        {
            ConditionalXamlSwitches.IsEnabled = ShowConditionalXamlSwitches;
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Options", "The option that shows the conditional XAML switches could not be applied.", exception);
        }
    }
}
