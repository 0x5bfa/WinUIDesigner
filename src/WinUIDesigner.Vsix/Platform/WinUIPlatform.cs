// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.ComponentModel;
using Microsoft.VisualStudio.DesignTools.Extensibility.Metadata;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Pipeline;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.UI.PlatformPane;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Utility;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PlatformPane;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views.NodeObjectConverters;

namespace WinUIDesigner.Platform;

/// <summary>
/// Represents the WinUI platform for the XAML designer, providing support for WinUI-specific features and behaviors.
/// </summary>
public sealed class WinUIPlatform : XamlPlatform
{
    private UwpDisplaySettingsProvider? displaySettingsProvider;
    private PlatformPaneModel? platformPaneModel;

    protected override bool SupportsSharedResourceDictionaries => false;

    private IDisplaySettingsProvider DisplaySettingsProvider
    {
        get
        {
            if (displaySettingsProvider is null)
            {
                displaySettingsProvider = new WinUIDisplaySettingsProvider(DesignerContext.PlatformService);
                displaySettingsProvider.Initialize();

                WinUIDesignerLogger.LogInformation("Platform", "UWP device display settings initialized.");
            }

            return displaySettingsProvider;
        }
    }

    /// <summary>
    /// Gets the view model behind the designer's settings area (top and bottom). It provides settings such as
    /// device size, orientation, theme, and clipping, then applies the selected options to the design-time view context.
    /// </summary>
    public override PlatformPaneModel PlatformPaneModel
    {
        get
        {
            if (platformPaneModel is null)
            {
                // TODO: This seems to be working fine for now, but we may need to implement WinUIPlatformPaneModel.
                platformPaneModel = new UwpPlatformPaneModel(DesignerContext, DisplaySettingsProvider);

                WinUIDesignerLogger.LogDebug("Platform", "UwpPlatformPaneModel instantiated.");
            }

            return platformPaneModel;
        }
    }

    public WinUIPlatform(IPlatformReferenceAssemblyResolver referenceAssemblyResolver)
        : base(referenceAssemblyResolver)
    {
        WinUIDesignerLogger.LogInformation("Platform", "WinUIPlatform instantiated.");
    }

    public override void Initialize(IDesignerContext designerContext)
    {
        WinUIDesignerLogger.LogDebug("Platform", "WinUIPlatform.Initialize reached.");

        base.Initialize(designerContext);
    }

    public override AttributeTable[] GetAttributeMetadata()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.GetAttributeMetadata reached.");

        var builder = new AttributeTableBuilder();

        // TODO: Add more categories for other properties as needed.
        foreach (string property in new[] { "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "HorizontalAlignment", "VerticalAlignment" })
        {
            builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", property, new CategoryAttribute("Layout"));
        }

        builder.AddCustomAttributes("Microsoft.UI.Xaml.Controls.Control", "FontSize", new CategoryAttribute("Text"));
        builder.AddCustomAttributes("Microsoft.UI.Xaml.Controls.TextBlock", "Text", new CategoryAttribute("Text"));

        builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", "Width", new DefaultValueAttribute(double.NaN));
        builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", "Height", new DefaultValueAttribute(double.NaN));

        // TODO: Maybe there are more properties that should be marked as non-browsable.
        foreach (string property in new[] { "Parent", "TemplatedParent", "XamlRoot", "DispatcherQueue", "Dispatcher", "ActualWidth", "ActualHeight", "DesiredSize", "RenderSize" })
        {
            builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", property, BrowsableAttribute.No);
        }

        return [builder.CreateTable()];
    }

    public override IProjectContext CreateProjectContext()
    {
        WinUIDesignerLogger.LogDebug("Platform", "WinUIPlatform.CreateProjectContext reached.");

        return new WinUIProjectContext(DesignerContext, this);
    }

    public override SceneView CreateSceneView(SceneDocument document)
    {
        WinUIDesignerLogger.LogDebug("Platform", "WinUIPlatform.CreateSceneView reached.");

        var viewModel = new UwpSceneViewModel(DesignerContext, document);

        WinUIDesignerLogger.LogDebug("Platform", "Temporary UwpSceneViewModel bridge instantiated.");

        var view = new WinUISceneView(viewModel);

        WinUIDesignerLogger.LogInformation("Platform", "Minimal WinUISceneView instantiated.");

        return view;
    }

    public override ISurfaceProcessMarkupProvider CreateSurfaceProcessMarkupProvider()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.CreateSurfaceProcessMarkupProvider reached.");

        return new WinUISurfaceProcessMarkupProvider();
    }

    public override IInstanceBuilderPlatform CreateSurfaceInstanceBuilderPlatform(IProjectContext projectContext)
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.CreateSurfaceInstanceBuilderPlatform reached.");

        return new UwpDesignerInstanceBuilderPlatform(projectContext);
    }

    protected override IPlatformConverter CreatePlatformConverter()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.CreatePlatformConverter reached.");

        var converter = new NodeObjectPlatformConverter();

        // The shared Visual Studio designer model represents these layout values with WPF types.
        // Visual Studio's shared converter has built-in type-name mappings between WPF (System.Windows)
        // and UWP (Windows.UI.Xaml), but no equivalent general mapping for WinUI (Microsoft.UI.Xaml).
        // Register individual conversions for the primitive values WinUI needs to pass to the shared WPF designer.
        converter.RegisterPrimitiveConverter(XamlTypes.HorizontalAlignment, ConvertHorizontalAlignment);
        converter.RegisterPrimitiveConverter(XamlTypes.VerticalAlignment, ConvertVerticalAlignment);
        converter.RegisterPrimitiveConverter(XamlTypes.Matrix, value => System.Windows.Media.Matrix.Parse(value));
        converter.RegisterPrimitiveConverter(XamlTypes.Thickness, value => new System.Windows.ThicknessConverter().ConvertFromInvariantString(value));

        return converter;

        static object ConvertHorizontalAlignment(string value)
        {
            return Enum.TryParse(value, true, out System.Windows.HorizontalAlignment alignment)
                ? alignment
                : System.Windows.HorizontalAlignment.Stretch;
        }

        static object ConvertVerticalAlignment(string value)
        {
            return Enum.TryParse(value, true, out System.Windows.VerticalAlignment alignment)
                ? alignment
                : System.Windows.VerticalAlignment.Stretch;
        }
    }


    protected override IGeometry CreateIsolatedSurfaceGeometry()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.CreateIsolatedSurfaceGeometry reached.");

        // WPF uses Math.Round(), while UWP uses Math.Floor(value + 0.5). For WinUI's geometry,
        // we use the same rounding behavior as UWP.
        return new NodeObjectGeometry(PlatformConverter, value => Math.Floor(value + 0.5));
    }

    protected override void RegisterNodeBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodeBuilders reached.");

        base.RegisterSurfaceIsolatedDocumentNodeBuilders();
    }

    protected override void RegisterNodeChildBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodeChildBuilders reached.");

        base.RegisterSurfaceIsolatedDocumentNodeChildBuilders();
    }

    protected override void RegisterNodePropertyBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodePropertyBuilders reached.");

        base.RegisterSurfaceIsolatedDocumentNodePropertyBuilders();
    }
}
