using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

internal sealed class LiveValueSerializer(ObjectIdentityRegistry objectIdentity)
{
    private static readonly System.Reflection.Assembly CoreLibraryAssembly = typeof(object).Assembly;
    private static readonly string SystemRuntimeAssemblyFullName =
        $"System.Runtime, Version={CoreLibraryAssembly.GetName().Version}, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    public LiveValue Serialize(object? value)
    {
        if (value is null)
        {
            return new LiveValue();
        }

        Type type = value.GetType();
        var result = new LiveValue
        {
            Type = GetSerializedTypeName(type),
            SourceInfo = objectIdentity.GetSourceInfo(value),
        };

        if (ShouldUseHandle(type, value))
        {
            result.Handle = objectIdentity.GetHandle(value);
        }

        result.Value = SerializeToString(value, type);
        return result;
    }

    public static string SerializeProperty(string name, Type declaringType)
        => $"{name}:{GetSerializedTypeName(declaringType)}";

    public static string GetSerializedTypeName(Type type)
    {
        if (type.Assembly == CoreLibraryAssembly
            && type.IsPublic
            && type.FullName is string fullName
            && !type.IsArray
            && !type.IsGenericType)
        {
            return $"{fullName}, {SystemRuntimeAssemblyFullName}";
        }

        return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
    }

    private static bool ShouldUseHandle(Type type, object value)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsValueType || value is string or Type or Uri)
        {
            return false;
        }

        return value is DependencyObject || !CanConvertToString(type);
    }

    private static string? SerializeToString(object value, Type type)
    {
        switch (value)
        {
            case string text:
                return text;
            case Type runtimeType:
                return GetSerializedTypeName(runtimeType);
            case Enum enumValue:
                return enumValue.ToString();
            case Thickness thickness:
                return FormattableString.Invariant($"{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}");
            case CornerRadius radius:
                return FormattableString.Invariant($"{radius.TopLeft},{radius.TopRight},{radius.BottomRight},{radius.BottomLeft}");
            case Point point:
                return FormattableString.Invariant($"{point.X},{point.Y}");
            case Size size:
                return FormattableString.Invariant($"{size.Width},{size.Height}");
            case Rect rect:
                return FormattableString.Invariant($"{rect.X},{rect.Y},{rect.Width},{rect.Height}");
            case Matrix matrix:
                return FormattableString.Invariant($"{matrix.M11},{matrix.M12},{matrix.M21},{matrix.M22},{matrix.OffsetX},{matrix.OffsetY}");
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertTo(typeof(string)))
            {
                return converter.ConvertToInvariantString(value);
            }
        }
        catch
        {
        }

        return value.ToString();
    }

    private static bool CanConvertToString(Type type)
    {
        try
        {
            return TypeDescriptor.GetConverter(type).CanConvertTo(typeof(string));
        }
        catch
        {
            return false;
        }
    }
}
