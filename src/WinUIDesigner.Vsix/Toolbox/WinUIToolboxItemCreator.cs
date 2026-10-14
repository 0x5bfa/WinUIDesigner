// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.DesignerHost.HostServices;
using Microsoft.VisualStudio.DesignTools.DesignerHost.Package.Toolbox;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MS.Internal.Package.Toolbox;
using IDataObject = Microsoft.VisualStudio.OLE.Interop.IDataObject;

namespace WinUIDesigner.Toolbox;

public sealed class WinUIToolboxItemCreator : IToolboxItemCreator, IToolboxItemCreationTypeInfo
{
    private IHostPlatformService _platformService = null!;

    private IVsDataObjectStringMapManager _stringMap = null!;

    private int ClipboardFormat => DataFormats.GetDataFormat(_platformService.GetProperty(
        new PlatformIdentifier(PlatformNames.Windows10, "Managed", FrameworkNames.CurrentDotNetCore, XamlRuntimeNames.WinUI), "ClipboardFormat")).Id;

    public WinUIToolboxItemCreator(IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        WinUIDesignerLogger.LogDebug("Toolbox", $"ItemCreator constructed with IServiceProvider={serviceProvider.GetType().FullName}.");

        ThreadHelperBase.RunOnUIThread(() =>
        {
            _platformService = serviceProvider.GetHostService<IHostPlatformService>();
            _stringMap = (IVsDataObjectStringMapManager)serviceProvider.GetService(typeof(SVsDataObjectStringMapManager));
        });

        WinUIDesignerLogger.LogDebug("Toolbox", $"ItemCreator initialized. ClipboardFormat={ClipboardFormat}.");
    }

    public IInstalledToolboxItemInfo? GetItemInfo(IDataObject dataObject)
    {
        if (dataObject is null) throw new ArgumentNullException(nameof(dataObject));
        ToolDataObject? data = ToolDataObject.FromData(dataObject, ClipboardFormat);

        WinUIDesignerLogger.LogDebug("Toolbox", $"ItemCreator.GetItemInfo: parsed={(data is not null)}, type={data?.CreationTypeName ?? "<null>"}.");

        return data is null
            ? null
            : new InstalledToolboxItemInfo(
                ToolboxUtilities.RemoveAssemblyQualification(data.CreationTypeName),
                data.CreationAssemblyName,
                GetCustomProperties(data));
    }

    private static IEnumerable<KeyValuePair<string, string>> GetCustomProperties(ToolDataObject toolData)
    {
        var directoryId = string.Empty;
        var versionId = string.Empty;
        var targetPlatformId = string.Empty;
        var assemblyName = toolData.CreationAssemblyName;
        var assemblyId = assemblyName?.Name ?? string.Empty;
        var sdkDisplayName = toolData.CreationTypeSdkDisplayName;
        var sdkId = toolData.CreationTypeSdkId;

        if (!string.IsNullOrEmpty(sdkId) && !string.IsNullOrEmpty(sdkDisplayName))
        {
            directoryId = sdkDisplayName;
            versionId = FormatVersion(new FrameworkName(sdkId).Version);

            if (toolData.TargetPlatform is not null &&
                PlatformName.TryParse(toolData.TargetPlatform, out PlatformName? platform) && platform is not null)
            {
                targetPlatformId = platform.DisplayName;
            }
        }
        else if (assemblyName is not null && !string.IsNullOrEmpty(assemblyName.CodeBase) && assemblyName.Version is { } assemblyVersion)
        {
            directoryId = new Uri(assemblyName.CodeBase).LocalPath;
            versionId = FormatVersion(assemblyVersion);
        }

        return
        [
            new KeyValuePair<string, string>("DirectoryId", directoryId),
            new KeyValuePair<string, string>("VersionId", versionId),
            new KeyValuePair<string, string>("TargetPlatformId", targetPlatformId),
            new KeyValuePair<string, string>("AssemblyId", assemblyId),
        ];
    }

    private static string FormatVersion(Version version)
    {
        int major = version.Major != -1 ? version.Major : 0;
        int minor = version.Minor != -1 ? version.Minor : 0;
        int build = version.Build != -1 ? version.Build : 0;
        int revision = version.Revision != -1 ? version.Revision : 0;
        return new Version(major, minor, build, revision).ToString();
    }

    public (string typeName, string assemblyName) GetCreationTypeInfo(IDataObject dataObject)
    {
        return ToolDataObject.CreationTypeInfoFromData(dataObject, ClipboardFormat);
    }

    public IDataObject CreateToolboxItem(string typeName, AssemblyName assemblyName,
        IEnumerable<KeyValuePair<string, string>> itemProperties, IEnumerable<KeyValuePair<string, string>> extraInfo)
    {
        if (typeName is null)
        {
            throw new ArgumentNullException(nameof(typeName));
        }

        if (assemblyName is null)
        {
            throw new ArgumentNullException(nameof(assemblyName));
        }

        if (extraInfo is null)
        {
            throw new ArgumentNullException(nameof(extraInfo));
        }

        var info = extraInfo.Where(entry => entry.Key != "ToolboxTabName").ToDictionary(entry => entry.Key, entry => entry.Value);

        WinUIDesignerLogger.LogDebug("Toolbox", $"CreateToolboxItem: type={typeName}, assembly={assemblyName.FullName}, extra=[{string.Join(", ", info.Select(entry => entry.Key + "=" + entry.Value))}].");

        var assembly = new AssemblyName(assemblyName.FullName);
        if (info.TryGetValue("OriginalPath", out string? path))
        {
            assembly.CodeBase = new Uri(path).AbsoluteUri;
        }
        else if (assemblyName.CodeBase is not null)
        {
            assembly.CodeBase = new Uri(assemblyName.CodeBase).AbsoluteUri;
        }

        string? creationToolName = info.TryGetValue("CreationToolType", out string? tool) ? tool : null;
        var data = new ToolDataObject(typeName, isInternal: false, assembly, creationToolName);

        if (info.TryGetValue("SupportedFrameworks", out string? frameworks))
        {
            data.SupportedFrameworks = frameworks;
        }

        if (info.TryGetValue("CreationTypePackageId", out string? packageId))
        {
            data.SetProperty(HostToolProperty.CreationTypePackageId, packageId);
            data.SetProperty(HostToolProperty.CreationTypePackageVersion, info["CreationTypePackageVersion"]);
            data.SetProperty(HostToolProperty.ManifestFrameworkName, info["ManifestFrameworkName"]);
            data.SetProperty(HostToolProperty.AllManifestFrameworkNames, info["AllManifestFrameworkNames"]);
        }
        else if (info.TryGetValue("CreationTypeSdkIdKey", out string? sdkId))
        {
            data.SetProperty(HostToolProperty.CreationTypeSdkId, sdkId);
            data.SetProperty(HostToolProperty.CreationTypeSdkPath, info["CreationTypeSdkPath"]);
            data.SetProperty(HostToolProperty.CreationTypeSdkDisplayName, info["CreationTypeSdkDisplayName"]);

            if (info.TryGetValue("CreationTypeSdkTargetPlatformMinVersionKey", out string? minVersion))
            {
                data.SetProperty(HostToolProperty.CreationTypeSdkTargetPlatformMinVersion, minVersion);
            }
        }

        data.SetProperty(HostToolProperty.CreationTypeSdkAppliesTo, "WinUI");

        if (!data.Properties.ContainsKey(HostToolProperty.TargetPlatform) || data.Properties[HostToolProperty.TargetPlatform] is null)
        {
            data.SetProperty(HostToolProperty.TargetPlatform,
                info.TryGetValue("TargetPlatform", out string? target) ? target : "Windows, Version=10.0");
        }

        data.SetProperty(HostToolProperty.IsPlatformControl, info.TryGetValue("IsPlatformControl", out string? platformControl) && bool.Parse(platformControl));

        WinUIDesignerLogger.LogDebug("Toolbox", $"CreateToolboxItem completed: type={typeName}, codeBase={assembly.CodeBase ?? "<null>"}.");

        return ToolDataObject.ToData(data, _stringMap, ClipboardFormat);
    }
}
