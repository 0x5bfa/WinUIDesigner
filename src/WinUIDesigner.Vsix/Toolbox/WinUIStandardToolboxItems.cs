// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIDesigner.Vsix.Toolbox;

internal static class WinUIStandardToolboxItems
{
    public const string ClipboardFormat = "CF_WINDOWSUIXAML_TOOL";
    public const string AssemblyIdentity = "Microsoft.WinUI, Version=3.0.0.0, Culture=neutral, PublicKeyToken=de31ebe4ad15742b";
    public const string GroupId = "WinUIDesigner.WinUI3.Controls";

    // Explicit placement catalog; do not expose every public type in the projection
    // (Page, Window, flyouts, abstract bases, and template parts are not assets).
    public static IReadOnlyList<string> TypeNames { get; } = Array.AsReadOnly(new[]
    {
        "AppBarButton", "AppBarSeparator", "AppBarToggleButton", "AutoSuggestBox",
        "Border", "Button", "CalendarDatePicker", "CalendarView", "Canvas", "CheckBox",
        "ColorPicker", "ComboBox", "CommandBar", "ContentControl", "ContentPresenter",
        "DatePicker", "DropDownButton", "Expander", "FlipView", "Frame", "Grid", "GridView",
        "HyperlinkButton", "Image", "InfoBar", "ItemsControl", "ItemsRepeater", "ListBox",
        "ListView", "MediaPlayerElement", "NavigationView", "NumberBox", "ParallaxView",
        "PasswordBox", "PersonPicture", "Pivot", "PivotItem", "ProgressBar", "ProgressRing",
        "RadioButton", "RadioButtons", "RatingControl", "RelativePanel", "RichEditBox",
        "RichTextBlock", "RichTextBlockOverflow", "ScrollViewer", "SemanticZoom", "Slider",
        "SplitButton", "SplitView", "StackPanel", "TabView", "TextBlock", "TextBox", "TimePicker",
        "ToggleSplitButton", "ToggleSwitch", "TreeView", "UserControl", "VariableSizedWrapGrid",
        "Viewbox", "WebView2",
    }.Select(name => "Microsoft.UI.Xaml.Controls." + name).Concat(new[]
    {
        "Microsoft.UI.Xaml.Controls.Primitives.RepeatButton",
        "Microsoft.UI.Xaml.Controls.Primitives.ScrollBar",
        "Microsoft.UI.Xaml.Controls.Primitives.ToggleButton",
        "Microsoft.UI.Xaml.Shapes.Ellipse", "Microsoft.UI.Xaml.Shapes.Line",
        "Microsoft.UI.Xaml.Shapes.Path", "Microsoft.UI.Xaml.Shapes.Polygon",
        "Microsoft.UI.Xaml.Shapes.Polyline", "Microsoft.UI.Xaml.Shapes.Rectangle",
    }).ToArray());

    public static string GetItemId(string typeName) => typeName + ", " + AssemblyIdentity;

    public static bool ContainsItem(string itemId) => TypeNames.Any(type =>
        string.Equals(GetItemId(type), itemId, StringComparison.Ordinal));
}
