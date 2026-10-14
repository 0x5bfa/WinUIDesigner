// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.Toolbox;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ProvideStaticWinUIToolboxItemsAttribute : ProvideStaticXamlToolboxItemsAttribute
{
    private const string _assemblyName = "Microsoft.WinUI, Version=3.0.0.0, Culture=neutral, PublicKeyToken=de31ebe4ad15742b";
    private static readonly IEnumerable<PlatformName> _anyWindows = [PlatformNames.CurrentWindows];

    // Keep this explicit list in sync with the public, constructible WinUI projection types.
    private static readonly ToolboxItemData[] _toolboxItems =
    [
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AnimatedIcon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AnimatedVisualPlayer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AnnotatedScrollBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AppBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AppBarButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AppBarElementContainer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AppBarSeparator"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AppBarToggleButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.AutoSuggestBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.BitmapIcon"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.Border"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.BreadcrumbBar"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.Button"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.CalendarDatePicker"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.CalendarView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Canvas"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.CheckBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ColorPicker"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.ComboBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ComboBoxItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.CommandBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ContentControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ContentDialog"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ContentPresenter"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.DatePicker"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.DropDownButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Expander"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.FlipView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.FlipViewItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.FontIcon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Frame"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.Grid"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.GridView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.GridViewItem"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.Hub"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.HubSection"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.HyperlinkButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.IconSourceElement"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.Image"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ImageIcon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.InfoBadge"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.InfoBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemContainer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsPresenter"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsRepeater"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsRepeaterScrollHost"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsStackPanel"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ItemsWrapGrid"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ListBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ListBoxItem"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.ListView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ListViewItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MapControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MediaPlayerElement"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MediaTransportControls"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MenuBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MenuBarItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MenuFlyoutItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.NavigationView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.NavigationViewItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.NavigationViewItemHeader"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.NumberBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ParallaxView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.PasswordBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.PathIcon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.PersonPicture"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.PipsPager"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Pivot"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.PivotItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ProgressBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ProgressRing"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RadioButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RadioButtons"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RadioMenuFlyoutItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RatingControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RefreshContainer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RefreshVisualizer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RelativePanel"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RichEditBox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RichTextBlock"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.RichTextBlockOverflow"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ScrollView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ScrollViewer"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SelectorBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SelectorBarItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SemanticZoom"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Slider"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SplitButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SplitMenuFlyoutItem"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.SplitView"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.StackPanel"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SwapChainPanel"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SwipeControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SymbolIcon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.SystemBackdropElement"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TabView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TabViewItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TeachingTip"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.TextBlock"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.TextBox"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.TimePicker"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TitleBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ToggleSplitButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ToggleSwitch"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.ToolTip"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TreeView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TreeViewItem"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.TwoPaneView"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.UserControl"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.VariableSizedWrapGrid"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Viewbox"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.VirtualizingStackPanel"),
        CreateToolboxItemData(true, "Microsoft.UI.Xaml.Controls.WebView2"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.WrapGrid"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.ColorSpectrum"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.Popup"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.RepeatButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.ScrollBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.ScrollPresenter"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.Thumb"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.TickBar"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Controls.Primitives.ToggleButton"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Documents.Glyphs"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Ellipse"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Line"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Path"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Polygon"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Polyline"),
        CreateToolboxItemData(false, "Microsoft.UI.Xaml.Shapes.Rectangle"),
    ];

    public override string ClipboardFormat => ClipboardFormatName;

    public override string CommonControlsGroupName => "#134";

    public override string CommonControlsGroupId => "WindowsUIXaml Common";

    public override string AllControlsGroupName => "#135";

    public override string AllControlsGroupId => "WindowsUIXaml Controls";

    public override IEnumerable<ToolboxItemData> ToolboxItems => _toolboxItems;

    public override bool TargetPlatformSupported => true;

    internal const string ClipboardFormatName = "CF_WINDOWSUIXAML_TOOL";

    internal static string AssemblyIdentity => _assemblyName;

    internal static IReadOnlyList<string> TypeNames { get; } = Array.AsReadOnly(
        _toolboxItems.Select(item => item.NamespaceQualifiedTypeName).ToArray());

    internal static string GetItemId(string typeName)
    {
        return Assembly.CreateQualifiedName(_assemblyName, typeName);
    }

    private static ToolboxItemData CreateToolboxItemData(bool isCommon, string typeName)
    {
        return CreateToolboxItemData(isCommon, typeName, _anyWindows);
    }

    private static ToolboxItemData CreateToolboxItemData(bool isCommon, string typeName, IEnumerable<PlatformName> targetPlatforms)
    {
        string assemblyQualifiedTypeName = Assembly.CreateQualifiedName(_assemblyName, typeName);
        return new ToolboxItemData(isCommon, assemblyQualifiedTypeName, null, targetPlatforms);
    }
}
