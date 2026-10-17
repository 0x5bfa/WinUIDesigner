// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Converts live WinUI values into the designer protocol representation.
/// Serializes complex DependencyObjects as handles so later protocol messages preserve object identity.
/// </summary>
internal sealed class LiveValueSerializer(ObjectIdentityRegistry objectIdentity)
{

    public LiveValue Serialize(object? value)
    {
        // WinUI exposes its no-default sentinel as a WinRT.IInspectable. It is
        // absence of a value, not a user object that should acquire a live handle.
        if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return new LiveValue();
        }

        Type type = value.GetType();
        var result = new LiveValue
        {
            Type = RuntimeTypeNameSerializer.Serialize(type),
            SourceInfo = objectIdentity.GetSourceInfo(value),
        };
        if (!type.IsPrimitive && !type.IsEnum && !type.IsValueType && value is not (string or Type or Uri))
        {
            // Collections retain a handle even when their converter produces a string.
            bool useHandle = value is DependencyObject or IEnumerable;
            if (!useHandle)
            {
                try
                {
                    useHandle = !TypeDescriptor.GetConverter(type).CanConvertTo(typeof(string));
                }
                catch
                {
                    useHandle = true;
                }
            }

            if (useHandle)
            {
                result.Handle = objectIdentity.GetHandle(value);
            }
        }

        switch (value)
        {
            case string text:
                result.Value = text;
                break;
            case Type runtimeType:
                result.Value = RuntimeTypeNameSerializer.Serialize(runtimeType);
                break;
            case Enum enumValue:
                result.Value = enumValue.ToString();
                break;
            case Thickness thickness:
                result.Value = FormattableString.Invariant($"{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}");
                break;
            case CornerRadius radius:
                result.Value = FormattableString.Invariant($"{radius.TopLeft},{radius.TopRight},{radius.BottomRight},{radius.BottomLeft}");
                break;
            case Point point:
                result.Value = FormattableString.Invariant($"{point.X},{point.Y}");
                break;
            case Size size:
                result.Value = FormattableString.Invariant($"{size.Width},{size.Height}");
                break;
            case Rect rect:
                result.Value = FormattableString.Invariant($"{rect.X},{rect.Y},{rect.Width},{rect.Height}");
                break;
            case Matrix matrix:
                result.Value = FormattableString.Invariant($"{matrix.M11},{matrix.M12},{matrix.M21},{matrix.M22},{matrix.OffsetX},{matrix.OffsetY}");
                break;
            case IFormattable formattable:
                result.Value = formattable.ToString(null, CultureInfo.InvariantCulture);
                break;
            default:
                try
                {
                    TypeConverter converter = TypeDescriptor.GetConverter(type);
                    if (converter.CanConvertTo(typeof(string)))
                    {
                        result.Value = converter.ConvertToInvariantString(value);
                        break;
                    }
                }
                catch
                {
                }

                result.Value = value.ToString();
                break;
        }

        return result;
    }
}
