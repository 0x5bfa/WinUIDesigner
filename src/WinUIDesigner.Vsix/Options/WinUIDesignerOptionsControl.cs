// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinUIDesigner.Options;

internal sealed class WinUIDesignerOptionsControl : UserControl
{
    private readonly WinUIDesignerOptionsPage _optionsPage;
    private readonly CheckBox _verboseLoggingCheckBox;
    private readonly CheckBox _conditionalXamlSwitchesCheckBox;
    private bool _updatingControls;

    public WinUIDesignerOptionsControl(WinUIDesignerOptionsPage optionsPage)
    {
        _optionsPage = optionsPage;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _conditionalXamlSwitchesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Show conditional XAML switches",
            AccessibleDescription = "Show the Conditions drop-down in the designer's device toolbar.",
            Margin = new Padding(0, 0, 0, 6),
        };
        _conditionalXamlSwitchesCheckBox.CheckedChanged += OnConditionalXamlSwitchesChanged;

        var conditionalXamlSwitchesDescription = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = "The Conditions drop-down lists the conditions of the conditional XAML namespaces of a document and lets you choose whether each one holds in the preview. Turning it off hides the drop-down at once; turning it on takes effect in designers opened afterwards.",
            Margin = new Padding(0, 0, 0, 18),
        };

        _verboseLoggingCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Enable verbose logging",
            AccessibleDescription = "Record detailed WinUI designer diagnostics.",
            Margin = new Padding(0, 0, 0, 6),
        };
        _verboseLoggingCheckBox.CheckedChanged += OnVerboseLoggingChanged;

        var description = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = "Detailed logs can help diagnose designer failures and may slow designer interactions. Visual Studio logging updates when applied; newly started surface processes receive this setting.",
            Margin = new Padding(0, 0, 0, 12),
        };

        var logFolderLink = new LinkLabel
        {
            AutoSize = true,
            Text = "Open log folder",
            AccessibleDescription = WinUIDesignerLogger.LogDirectoryPath,
            Margin = new Padding(0, 0, 0, 4),
        };
        logFolderLink.LinkClicked += OnLogFolderLinkClicked;

        var logFolderPath = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = WinUIDesignerLogger.LogDirectoryPath,
            Margin = new Padding(0),
        };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        layout.Controls.Add(_conditionalXamlSwitchesCheckBox);
        layout.Controls.Add(conditionalXamlSwitchesDescription);
        layout.Controls.Add(_verboseLoggingCheckBox);
        layout.Controls.Add(description);
        layout.Controls.Add(logFolderLink);
        layout.Controls.Add(logFolderPath);
        Controls.Add(layout);
    }

    internal void UpdateVerboseLogging(bool enabled)
    {
        _updatingControls = true;
        _verboseLoggingCheckBox.Checked = enabled;
        _updatingControls = false;
    }

    internal void UpdateShowConditionalXamlSwitches(bool shown)
    {
        _updatingControls = true;
        _conditionalXamlSwitchesCheckBox.Checked = shown;
        _updatingControls = false;
    }

    private void OnVerboseLoggingChanged(object? sender, EventArgs e)
    {
        if (!_updatingControls)
        {
            _optionsPage.EnableVerboseLogging = _verboseLoggingCheckBox.Checked;
        }
    }

    private void OnConditionalXamlSwitchesChanged(object? sender, EventArgs e)
    {
        if (!_updatingControls)
        {
            _optionsPage.ShowConditionalXamlSwitches = _conditionalXamlSwitchesCheckBox.Checked;
        }
    }

    private void OnLogFolderLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        string logDirectory = WinUIDesignerLogger.LogDirectoryPath;

        try
        {
            Directory.CreateDirectory(logDirectory);
            Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Options", $"Failed to open the log directory '{logDirectory}'.", exception);
            MessageBox.Show(
                this,
                $"Could not open the log directory.\r\n{logDirectory}\r\n\r\n{exception.Message}",
                "WinUI Designer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
