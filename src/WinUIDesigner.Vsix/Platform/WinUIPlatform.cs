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
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.UI.PlatformPane;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner;
using Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views.NodeObjectConverters;

using WinUIDesigner.Vsix;

namespace WinUIDesigner.Platform;

// VS has no complete WinUI Designer backend, so this platform reuses the shared
// XAML designer model and selected UWP infrastructure, then supplies WinUI surface
// hosting and serialization where the two XAML runtimes differ.
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

    public override PlatformPaneModel PlatformPaneModel
    {
        get
        {
            if (platformPaneModel is null)
            {
                platformPaneModel = new UwpPlatformPaneModel(DesignerContext, DisplaySettingsProvider);
                WinUIDesignerLogger.LogDebug("Platform", "Temporary UwpPlatformPaneModel bridge instantiated.");
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
        foreach (string property in new[] { "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "HorizontalAlignment", "VerticalAlignment" })
            builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", property, new CategoryAttribute("Layout"));
        builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", "Width", new DefaultValueAttribute(double.NaN));
        builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", "Height", new DefaultValueAttribute(double.NaN));
        foreach (string property in new[] { "Parent", "TemplatedParent", "XamlRoot", "DispatcherQueue", "Dispatcher", "ActualWidth", "ActualHeight", "DesiredSize", "RenderSize" })
            builder.AddCustomAttributes("Microsoft.UI.Xaml.FrameworkElement", property, BrowsableAttribute.No);
        builder.AddCustomAttributes("Microsoft.UI.Xaml.Controls.Control", "FontSize", new CategoryAttribute("Text"));
        builder.AddCustomAttributes("Microsoft.UI.Xaml.Controls.TextBlock", "Text", new CategoryAttribute("Text"));
        return new[] { builder.CreateTable() };
    }

    public override IProjectContext CreateProjectContext()
    {
        WinUIDesignerLogger.LogDebug("Platform", "WinUIPlatform.CreateProjectContext reached.");
        return new WinUIProjectContext(DesignerContext, this);
    }

    public override SceneView CreateSceneView(SceneDocument document)
    {
        WinUIDesignerLogger.LogDebug("Platform", "WinUIPlatform.CreateSceneView reached.");
        UwpSceneViewModel viewModel = new UwpSceneViewModel(DesignerContext, document);
        WinUIDesignerLogger.LogDebug("Platform", "Temporary UwpSceneViewModel bridge instantiated.");

        WinUISceneView view = new WinUISceneView(viewModel);
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
        // The shared WPF frontend expects WPF primitives, while WinUI reports its
        // own serialized names. Register these layout types explicitly.
        converter.RegisterPrimitiveConverter(XamlTypes.HorizontalAlignment, ConvertHorizontalAlignment);
        converter.RegisterPrimitiveConverter(XamlTypes.VerticalAlignment, ConvertVerticalAlignment);
        converter.RegisterPrimitiveConverter(XamlTypes.Matrix, value => System.Windows.Media.Matrix.Parse(value));
        converter.RegisterPrimitiveConverter(XamlTypes.Thickness, value => new System.Windows.ThicknessConverter().ConvertFromInvariantString(value)!);
        return converter;
    }

    private static object ConvertHorizontalAlignment(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out System.Windows.HorizontalAlignment alignment)
                ? alignment
                : System.Windows.HorizontalAlignment.Stretch;
    }

    private static object ConvertVerticalAlignment(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out System.Windows.VerticalAlignment alignment)
                ? alignment
                : System.Windows.VerticalAlignment.Stretch;
    }

    protected override IGeometry CreateIsolatedSurfaceGeometry()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.CreateIsolatedSurfaceGeometry reached.");
        return new NodeObjectGeometry(PlatformConverter, value => Math.Floor(value + 0.5));
    }

    protected override void RegisterNodeBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodeBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodeBuilders();
    }

    protected override void RegisterNodeChildBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodeChildBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodeChildBuilders();
    }

    protected override void RegisterNodePropertyBuilders()
    {
        WinUIDesignerLogger.LogTrace("Platform", "WinUIPlatform.RegisterNodePropertyBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodePropertyBuilders();
    }

}
