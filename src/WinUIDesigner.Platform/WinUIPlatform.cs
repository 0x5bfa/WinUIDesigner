// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
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

namespace WinUIDesigner.Platform;

// VS has no complete WinUI Designer backend, so this platform reuses the shared
// XAML designer model and selected UWP infrastructure, then supplies WinUI surface
// hosting and serialization where the two XAML runtimes differ.
public sealed class WinUIPlatform : XamlPlatform
{
    private static readonly string DiagnosticTracePath = CreateDiagnosticTracePath();
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
                WriteDiagnosticTrace("UWP device display settings initialized.");
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
                WriteDiagnosticTrace("Temporary UwpPlatformPaneModel bridge instantiated.");
            }

            return platformPaneModel;
        }
    }

    public WinUIPlatform(IPlatformReferenceAssemblyResolver referenceAssemblyResolver)
        : base(referenceAssemblyResolver)
    {
        WriteDiagnosticTrace("WinUIPlatform instantiated.");
    }

    public override void Initialize(IDesignerContext designerContext)
    {
        WriteDiagnosticTrace("WinUIPlatform.Initialize reached.");
        base.Initialize(designerContext);
    }

    public override AttributeTable[] GetAttributeMetadata()
    {
        WriteDiagnosticTrace("WinUIPlatform.GetAttributeMetadata reached.");
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
        WriteDiagnosticTrace("WinUIPlatform.CreateProjectContext reached.");
        return new WinUIProjectContext(DesignerContext, this);
    }

    public override SceneView CreateSceneView(SceneDocument document)
    {
        WriteDiagnosticTrace("WinUIPlatform.CreateSceneView reached.");
        UwpSceneViewModel viewModel = new UwpSceneViewModel(DesignerContext, document);
        WriteDiagnosticTrace("Temporary UwpSceneViewModel bridge instantiated.");

        WinUISceneView view = new WinUISceneView(viewModel);
        WriteDiagnosticTrace("Minimal WinUISceneView instantiated.");
        return view;
    }

    public override ISurfaceProcessMarkupProvider CreateSurfaceProcessMarkupProvider()
    {
        WriteDiagnosticTrace("WinUIPlatform.CreateSurfaceProcessMarkupProvider reached.");
        return new WinUISurfaceProcessMarkupProvider();
    }

    public override IInstanceBuilderPlatform CreateSurfaceInstanceBuilderPlatform(IProjectContext projectContext)
    {
        WriteDiagnosticTrace("WinUIPlatform.CreateSurfaceInstanceBuilderPlatform reached.");
        return new UwpDesignerInstanceBuilderPlatform(projectContext);
    }

    protected override IPlatformConverter CreatePlatformConverter()
    {
        WriteDiagnosticTrace("WinUIPlatform.CreatePlatformConverter reached.");
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
        WriteDiagnosticTrace("WinUIPlatform.CreateIsolatedSurfaceGeometry reached.");
        return new NodeObjectGeometry(PlatformConverter, value => Math.Floor(value + 0.5));
    }

    protected override void RegisterNodeBuilders()
    {
        WriteDiagnosticTrace("WinUIPlatform.RegisterNodeBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodeBuilders();
    }

    protected override void RegisterNodeChildBuilders()
    {
        WriteDiagnosticTrace("WinUIPlatform.RegisterNodeChildBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodeChildBuilders();
    }

    protected override void RegisterNodePropertyBuilders()
    {
        WriteDiagnosticTrace("WinUIPlatform.RegisterNodePropertyBuilders reached.");
        RegisterSurfaceIsolatedDocumentNodePropertyBuilders();
    }

    internal static void WriteDiagnosticTrace(string message)
    {
        Trace.WriteLine($"[WinUIDesigner] {message}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticTracePath)!);
            File.AppendAllText(DiagnosticTracePath, $"{DateTime.UtcNow:O} Platform: {message}\r\n");
        }
        catch (IOException)
        {
            // Multiple designer processes write to diagnostic traces.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostic logging must not interrupt designer activation.
        }
    }

    private static string CreateDiagnosticTracePath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        return Path.Combine(
            basePath,
            "WinUIDesigner",
            "Logs",
            $"WinUIDesigner-{Process.GetCurrentProcess().Id}.log");
    }
}
