// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Vsix.Toolbox;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class ProvideStaticWinUIToolboxItemsAttribute : RegistrationAttribute
{
    public override void Register(RegistrationContext context)
    {
        foreach (RegistrationAttribute attribute in CreateAttributes()) attribute.Register(context);
    }

    public override void Unregister(RegistrationContext context)
    {
        foreach (RegistrationAttribute attribute in CreateAttributes().Reverse()) attribute.Unregister(context);
    }

    private static IEnumerable<RegistrationAttribute> CreateAttributes()
    {
        yield return new ProvideStaticToolboxGroupAttribute("WinUI 3", WinUIStandardToolboxItems.GroupId) { Index = 2100 };
        int index = 1;
        foreach (string typeName in WinUIStandardToolboxItems.TypeNames)
        {
            string name = typeName.Substring(typeName.LastIndexOf('.') + 1);
            yield return new ProvideStaticToolboxItemAttribute(WinUIStandardToolboxItems.GroupId,
                name, WinUIStandardToolboxItems.GetItemId(typeName), WinUIStandardToolboxItems.ClipboardFormat,
                typeName, "@ToolboxBitmap_WinUIControl", 0xFF00FF) { Index = index++ };
        }
    }
}
