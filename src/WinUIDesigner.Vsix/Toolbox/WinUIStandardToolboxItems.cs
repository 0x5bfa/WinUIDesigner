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
    // Coverage and justified exclusions are checked against the SDK in Toolbox.Tests.
    public static IReadOnlyList<string> TypeNames { get; } = Array.AsReadOnly(new[]
    {
        "AnimatedIcon", "AnimatedVisualPlayer", "AnnotatedScrollBar", "AppBar", "AppBarButton",
        "AppBarElementContainer", "AppBarSeparator", "AppBarToggleButton", "AutoSuggestBox", "BitmapIcon",
        "Border", "BreadcrumbBar", "Button", "CalendarDatePicker", "CalendarView", "Canvas", "CheckBox",
        "ColorPicker", "ComboBox", "ComboBoxItem", "CommandBar", "ContentControl", "ContentDialog",
        "ContentPresenter", "DatePicker", "DropDownButton", "Expander", "FlipView", "FlipViewItem",
        "FontIcon", "Frame", "Grid", "GridView", "GridViewItem", "Hub", "HubSection", "HyperlinkButton",
        "IconSourceElement", "Image", "ImageIcon", "InfoBadge", "InfoBar", "ItemContainer", "ItemsControl",
        "ItemsPresenter", "ItemsRepeater", "ItemsRepeaterScrollHost", "ItemsStackPanel", "ItemsView",
        "ItemsWrapGrid", "ListBox", "ListBoxItem", "ListView", "ListViewItem", "MapControl",
        "MediaPlayerElement", "MediaTransportControls", "MenuBar", "MenuBarItem", "MenuFlyoutItem",
        "MenuFlyoutSeparator", "MenuFlyoutSubItem", "NavigationView", "NavigationViewItem",
        "NavigationViewItemHeader", "NavigationViewItemSeparator", "NumberBox", "ParallaxView",
        "PasswordBox", "PathIcon", "PersonPicture", "PipsPager", "Pivot", "PivotItem", "ProgressBar",
        "ProgressRing", "RadioButton", "RadioButtons", "RadioMenuFlyoutItem", "RatingControl",
        "RefreshContainer", "RefreshVisualizer", "RelativePanel", "RichEditBox", "RichTextBlock",
        "RichTextBlockOverflow", "ScrollView", "ScrollViewer", "SelectorBar", "SelectorBarItem",
        "SemanticZoom", "Slider", "SplitButton", "SplitMenuFlyoutItem", "SplitView", "StackPanel",
        "SwapChainPanel", "SwipeControl", "SymbolIcon", "SystemBackdropElement", "TabView", "TabViewItem",
        "TeachingTip", "TextBlock", "TextBox", "TimePicker", "TitleBar", "ToggleMenuFlyoutItem",
        "ToggleSplitButton", "ToggleSwitch", "ToolTip", "TreeView", "TreeViewItem", "TwoPaneView",
        "UserControl", "VariableSizedWrapGrid", "Viewbox", "VirtualizingStackPanel", "WebView2",
        "WrapGrid",
    }.Select(name => "Microsoft.UI.Xaml.Controls." + name).Concat(new[]
    {
        "Microsoft.UI.Xaml.Controls.Primitives.ColorSpectrum",
        "Microsoft.UI.Xaml.Controls.Primitives.Popup",
        "Microsoft.UI.Xaml.Controls.Primitives.RepeatButton",
        "Microsoft.UI.Xaml.Controls.Primitives.ScrollBar",
        "Microsoft.UI.Xaml.Controls.Primitives.ScrollPresenter",
        "Microsoft.UI.Xaml.Controls.Primitives.Thumb",
        "Microsoft.UI.Xaml.Controls.Primitives.TickBar",
        "Microsoft.UI.Xaml.Controls.Primitives.ToggleButton",
        "Microsoft.UI.Xaml.Documents.Glyphs",
        "Microsoft.UI.Xaml.Shapes.Ellipse",
        "Microsoft.UI.Xaml.Shapes.Line",
        "Microsoft.UI.Xaml.Shapes.Path",
        "Microsoft.UI.Xaml.Shapes.Polygon",
        "Microsoft.UI.Xaml.Shapes.Polyline",
        "Microsoft.UI.Xaml.Shapes.Rectangle",
    }).ToArray());

    public static string GetItemId(string typeName)
    {
        return typeName + ", " + AssemblyIdentity;
    }

    public static bool ContainsItem(string itemId)
    {
        return TypeNames.Any(type =>
        string.Equals(GetItemId(type), itemId, StringComparison.Ordinal));
    }
}
