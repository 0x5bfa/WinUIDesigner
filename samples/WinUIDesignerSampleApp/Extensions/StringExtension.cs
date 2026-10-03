using Microsoft.UI.Xaml.Markup;

namespace WinUIDesignerSampleApp.Extensions;

[ContentProperty(Name = nameof(Key))]
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class StringExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Key.GetLocalized();
}
