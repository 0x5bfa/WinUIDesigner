using System;
using Microsoft.VisualStudio.Shell;

namespace WinUIDesigner.Vsix;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
internal sealed class ProvideXamlRuntimeDesignerAttribute : RegistrationAttribute
{
    private const string DesignerTabEditorFactoryGuid = "{8d42ae58-4628-4780-ac3c-6b7e6de7a92a}";

    private readonly string xamlRuntime;

    public ProvideXamlRuntimeDesignerAttribute(string xamlRuntime)
    {
        this.xamlRuntime = xamlRuntime ?? throw new ArgumentNullException(nameof(xamlRuntime));
    }

    public override void Register(RegistrationContext context)
    {
        using Key key = context.CreateKey($"XamlDesigner\\XamlRuntimes\\{xamlRuntime}");
        key.SetValue(string.Empty, DesignerTabEditorFactoryGuid);
    }

    public override void Unregister(RegistrationContext context)
    {
        context.RemoveKey($"XamlDesigner\\XamlRuntimes\\{xamlRuntime}");
    }
}
