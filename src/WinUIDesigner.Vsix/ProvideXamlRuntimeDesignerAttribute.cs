// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner;

/// <summary>
/// Connect this XAML runtime name to the editor factory VS uses for its designer tab.
/// RegistrationAttribute lets VS create and remove the key with the VSIX lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
internal sealed class ProvideXamlRuntimeDesignerAttribute(string xamlRuntime) : RegistrationAttribute
{
    // VS's XAML designer-tab editor factory registration used by the built-in XAML editor.
    private const string DesignerTabEditorFactoryGuid = "{8d42ae58-4628-4780-ac3c-6b7e6de7a92a}";

    private readonly string xamlRuntime = xamlRuntime ?? throw new ArgumentNullException(nameof(xamlRuntime));

    public override void Register(RegistrationContext context)
    {
        // The key name is the runtime identifier consumed by Visual Studio's XAML editor.
        using Key key = context.CreateKey($"XamlDesigner\\XamlRuntimes\\{xamlRuntime}");
        key.SetValue(string.Empty, DesignerTabEditorFactoryGuid);
    }

    public override void Unregister(RegistrationContext context)
    {
        // Remove only this runtime mapping when the package is unregistered.
        context.RemoveKey($"XamlDesigner\\XamlRuntimes\\{xamlRuntime}");
    }
}
