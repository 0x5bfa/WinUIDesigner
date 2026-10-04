// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.HostServices;
using Microsoft.VisualStudio.DesignTools.DesignerHost.Package.Toolbox;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.DesignTools.Utility.Extensions;
using IDataObject = Microsoft.VisualStudio.OLE.Interop.IDataObject;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace WinUIDesigner.Vsix.Toolbox;

public sealed class WinUIToolboxItemCreator : IToolboxItemCreator, IToolboxItemCreationTypeInfo
{
    private IHostPlatformService platformService = null!;
    private IVsDataObjectStringMapManager stringMap = null!;
    private int ClipboardFormat => DataFormats.GetDataFormat(platformService.GetProperty(
        new PlatformIdentifier(PlatformNames.Windows10, "Managed", FrameworkNames.CurrentDotNetCore, XamlRuntimeNames.WinUI), "ClipboardFormat")).Id;

    public WinUIToolboxItemCreator(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null) throw new ArgumentNullException(nameof(serviceProvider));
        ToolboxTrace.Write($"ItemCreator constructed with IServiceProvider={serviceProvider.GetType().FullName}.");
        ThreadHelperBase.RunOnUIThread(() =>
        {
            platformService = serviceProvider.GetHostService<IHostPlatformService>();
            stringMap = (IVsDataObjectStringMapManager)serviceProvider.GetService(typeof(SVsDataObjectStringMapManager));
        });
        ToolboxTrace.Write($"ItemCreator initialized. ClipboardFormat={ClipboardFormat}.");
    }

    public IInstalledToolboxItemInfo? GetItemInfo(IDataObject dataObject)
    {
        if (dataObject is null) throw new ArgumentNullException(nameof(dataObject));
        ToolDataObject? data = ToolDataObject.FromData(dataObject, ClipboardFormat);
        ToolboxTrace.Write($"ItemCreator.GetItemInfo: parsed={(data is not null)}, type={data?.CreationTypeName ?? "<null>"}.");
        return data is null ? null : new InstalledToolboxItemInfo(data.CreationTypeName.Split(',')[0], data.CreationAssemblyName, Array.Empty<KeyValuePair<string, string>>());
    }

    public (string typeName, string assemblyName) GetCreationTypeInfo(IDataObject dataObject)
    {
        return ToolDataObject.CreationTypeInfoFromData(dataObject, ClipboardFormat);
    }

    public IDataObject CreateToolboxItem(string typeName, AssemblyName assemblyName,
        IEnumerable<KeyValuePair<string, string>> itemProperties, IEnumerable<KeyValuePair<string, string>> extraInfo)
    {
        if (typeName is null) throw new ArgumentNullException(nameof(typeName));
        if (assemblyName is null) throw new ArgumentNullException(nameof(assemblyName));
        if (extraInfo is null) throw new ArgumentNullException(nameof(extraInfo));
        var info = extraInfo.Where(entry => entry.Key != "ToolboxTabName").ToDictionary(entry => entry.Key, entry => entry.Value);
        ToolboxTrace.Write($"CreateToolboxItem: type={typeName}, assembly={assemblyName.FullName}, extra=[{string.Join(", ", info.Select(entry => entry.Key + "=" + entry.Value))}].");
        var assembly = new AssemblyName(assemblyName.FullName);
        if (info.TryGetValue("OriginalPath", out string? path)) assembly.CodeBase = new Uri(path).AbsoluteUri;
        else if (assemblyName.CodeBase is not null) assembly.CodeBase = new Uri(assemblyName.CodeBase).AbsoluteUri;
        var data = new ToolDataObject(typeName, isInternal: false, assembly,
            info.TryGetValue("CreationToolType", out string? tool) ? tool : null);
        if (info.TryGetValue("SupportedFrameworks", out string? frameworks)) data.SupportedFrameworks = frameworks;
        foreach (HostToolProperty property in new[]
        {
            HostToolProperty.CreationTypePackageId, HostToolProperty.CreationTypePackageVersion,
            HostToolProperty.ManifestFrameworkName, HostToolProperty.AllManifestFrameworkNames,
            HostToolProperty.CreationTypeSdkPath, HostToolProperty.CreationTypeSdkDisplayName,
        })
        {
            if (info.TryGetValue(property.ToString(), out string? value)) data.SetProperty(property, value);
        }
        if (info.TryGetValue("CreationTypeSdkIdKey", out string? sdkId)) data.SetProperty(HostToolProperty.CreationTypeSdkId, sdkId);
        if (info.TryGetValue("CreationTypeSdkTargetPlatformMinVersionKey", out string? minVersion))
            data.SetProperty(HostToolProperty.CreationTypeSdkTargetPlatformMinVersion, minVersion);
        data.SetProperty(HostToolProperty.CreationTypeSdkAppliesTo, "WinUI");
        data.SetProperty(HostToolProperty.TargetPlatform,
            info.TryGetValue("TargetPlatform", out string? target) ? target : "Windows, Version=10.0");
        data.SetProperty(HostToolProperty.IsPlatformControl,
            info.TryGetValue("IsPlatformControl", out string? platformControl) && bool.Parse(platformControl));
        IDataObject result = ToolDataObject.ToData(data, stringMap, ClipboardFormat);
        ToolboxTrace.Write($"CreateToolboxItem completed: type={typeName}, codeBase={assembly.CodeBase ?? "<null>"}.");
        return result;
    }
}
