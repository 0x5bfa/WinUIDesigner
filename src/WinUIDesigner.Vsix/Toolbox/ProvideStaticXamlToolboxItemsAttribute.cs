// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Resources;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Toolbox;

// This is the same as Microsoft.VisualStudio.DesignTools.XamlDesignerHost.dll!MS.Internal.Package.Toolbox.ProvideStaticXamlToolboxItemsAttribute
[AttributeUsage(AttributeTargets.Class)]
internal abstract class ProvideStaticXamlToolboxItemsAttribute : RegistrationAttribute
{
    private List<RegistrationAttribute> registrationAttributes = null!;

    public abstract string ClipboardFormat { get; }

    public abstract string CommonControlsGroupName { get; }

    public abstract string CommonControlsGroupId { get; }

    public abstract string AllControlsGroupName { get; }

    public abstract string AllControlsGroupId { get; }

    public abstract IEnumerable<ToolboxItemData> ToolboxItems { get; }

    public virtual bool TargetPlatformSupported => false;

    public override void Register(RegistrationContext context)
    {
        EnsureRegistrationAttributesCreated();

        foreach (var attribute in registrationAttributes)
        {
            attribute.Register(context);
        }
    }

    public override void Unregister(RegistrationContext context)
    {
        EnsureRegistrationAttributesCreated();

        foreach (var attribute in Enumerable.Reverse(registrationAttributes))
        {
            attribute.Unregister(context);
        }
    }

    private void EnsureRegistrationAttributesCreated()
    {
        if (registrationAttributes is not null)
        {
            return;
        }

        registrationAttributes = [];

        var commonGroup = new ProvideStaticToolboxGroupAttribute(CommonControlsGroupName, CommonControlsGroupId) { Index = 1000 };
        registrationAttributes.Add(commonGroup);

        int commonIndex = 1;
        foreach (ToolboxItemData item in ToolboxItems.Where(item => item.IsCommon))
        {
            AddProvideStaticToolboxItemAttribute(CommonControlsGroupId, commonIndex++, item);
        }

        var allGroup = new ProvideStaticToolboxGroupAttribute(AllControlsGroupName, AllControlsGroupId) { Index = 2000 };
        registrationAttributes.Add(allGroup);

        int allIndex = 1;
        foreach (ToolboxItemData item in ToolboxItems)
        {
            AddProvideStaticToolboxItemAttribute(AllControlsGroupId, allIndex++, item);
        }
    }

    private void AddProvideStaticToolboxItemAttribute(string groupId, int index, ToolboxItemData item)
    {
        ProvideStaticToolboxItemAttribute attribute = item.TargetFrameworks is not null
            ? new ProvideStaticToolboxItemAttribute(
                groupId,
                item.UnqualifiedTypeName,
                item.AssemblyQualifiedTypeName,
                ClipboardFormat,
                item.NamespaceQualifiedTypeName,
                GetToolboxBitmapId(item.UnqualifiedTypeName),
                0xFF00FF,
                item.AssemblyName,
                item.NamespaceQualifiedTypeName,
                null,
                item.TargetFrameworks)
            : new ProvideStaticToolboxItemAttribute(
                groupId,
                item.UnqualifiedTypeName,
                item.AssemblyQualifiedTypeName,
                ClipboardFormat,
                item.NamespaceQualifiedTypeName,
                GetToolboxBitmapId(item.UnqualifiedTypeName),
                0xFF00FF);

        attribute.Index = index;

        registrationAttributes.Add(attribute);
    }

    private static string GetToolboxBitmapId(string unqualifiedTypeName)
    {
        var resourceName = "ToolboxBitmap_" + unqualifiedTypeName;
        var defaultResourceName = "ToolboxBitmap_WinUIControl";

        try
        {
            var resourceManager = new ResourceManager("VSPackage", typeof(ProvideStaticXamlToolboxItemsAttribute).Assembly);
            return resourceManager.GetObject(resourceName, CultureInfo.InvariantCulture) is Bitmap
                ? "@" + resourceName
                : "@" + defaultResourceName;
        }
        catch (MissingManifestResourceException)
        {
            return "@" + defaultResourceName;
        }
    }
}
