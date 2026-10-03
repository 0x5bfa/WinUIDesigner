using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace DesignerSmokeApp;

public sealed class ReferencedControl : Button
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string),
        typeof(ReferencedControl), new PropertyMetadata("Project control", (owner, args) => ((ReferencedControl)owner).Content = args.NewValue));
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public ReferencedControl()
    {
        Content = "Project control";
        Width = 160;
        Height = 48;
    }
}
