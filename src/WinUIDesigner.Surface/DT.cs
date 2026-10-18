// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// Match the VS UWP designer contract: its DesignerInstanceBuilder serializes design-time properties under "XSurfUwp.DT".
namespace XSurfUwp;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Defines attached design-time properties used to preserve document sizing and style intent.
/// Keeps design, runtime, and root sizing intent separate. The real Width/Height
/// receives only the currently selected value used to lay out the preview.
/// </summary>
public static class DT
{
    private static readonly ConditionalWeakTable<FrameworkElement, Style> _SuppressedStyles = [];

    public static readonly DependencyProperty ShouldDisableImplicitStyleProperty = DependencyProperty.RegisterAttached(
        "ShouldDisableImplicitStyle", typeof(bool), typeof(DT), new PropertyMetadata(false, (owner, args) =>
        {
            if (owner is not FrameworkElement element) return;
            if ((bool)args.NewValue)
            {
                if (!ReferenceEquals(element.ReadLocalValue(FrameworkElement.StyleProperty), DependencyProperty.UnsetValue))
                {
                    return;
                }

                var style = new Style() { TargetType = element.GetType() };
                _SuppressedStyles.Remove(element);
                _SuppressedStyles.Add(element, style);
                element.Style = style;
            }
            else if (_SuppressedStyles.TryGetValue(element, out Style? style))
            {
                if (ReferenceEquals(element.Style, style))
                {
                    element.ClearValue(FrameworkElement.StyleProperty);
                }

                _SuppressedStyles.Remove(element);
            }
        }));

    public static void SetShouldDisableImplicitStyle(DependencyObject owner, bool value)
    {
        owner.SetValue(ShouldDisableImplicitStyleProperty, value);
    }

    public static bool GetShouldDisableImplicitStyle(DependencyObject owner)
    {
        return (bool)owner.GetValue(ShouldDisableImplicitStyleProperty);
    }

    // Metadata callbacks run after the attached properties have been registered.
    public static readonly DependencyProperty RootWidthProperty = DependencyProperty.RegisterAttached(
        "RootWidth", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.WidthProperty, RootWidthProperty!, DesignWidthProperty!, RuntimeWidthProperty!)));

    public static readonly DependencyProperty DesignWidthProperty = DependencyProperty.RegisterAttached(
        "DesignWidth", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.WidthProperty, RootWidthProperty!, DesignWidthProperty!, RuntimeWidthProperty!)));

    public static readonly DependencyProperty RuntimeWidthProperty = DependencyProperty.RegisterAttached(
        "RuntimeWidth", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.WidthProperty, RootWidthProperty!, DesignWidthProperty!, RuntimeWidthProperty!)));

    public static readonly DependencyProperty RootHeightProperty = DependencyProperty.RegisterAttached(
        "RootHeight", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.HeightProperty, RootHeightProperty!, DesignHeightProperty!, RuntimeHeightProperty!)));

    public static readonly DependencyProperty DesignHeightProperty = DependencyProperty.RegisterAttached(
        "DesignHeight", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.HeightProperty, RootHeightProperty!, DesignHeightProperty!, RuntimeHeightProperty!)));

    public static readonly DependencyProperty RuntimeHeightProperty = DependencyProperty.RegisterAttached(
        "RuntimeHeight", typeof(double), typeof(DT), new PropertyMetadata(double.NaN, static (dependencyObject, _) => UpdateWidthOrHeight(dependencyObject, FrameworkElement.HeightProperty, RootHeightProperty!, DesignHeightProperty!, RuntimeHeightProperty!)));

    public static void SetRootWidth(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(RootWidthProperty, value);
    }

    public static double GetRootWidth(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(RootWidthProperty);
    }

    public static void SetDesignWidth(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(DesignWidthProperty, value);
    }

    public static double GetDesignWidth(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(DesignWidthProperty);
    }

    public static void SetRuntimeWidth(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(RuntimeWidthProperty, value);
    }

    public static double GetRuntimeWidth(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(RuntimeWidthProperty);
    }

    public static void SetRootHeight(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(RootHeightProperty, value);
    }

    public static double GetRootHeight(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(RootHeightProperty);
    }

    public static void SetDesignHeight(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(DesignHeightProperty, value);
    }

    public static double GetDesignHeight(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(DesignHeightProperty);
    }

    public static void SetRuntimeHeight(DependencyObject dependencyObject, double value)
    {
        dependencyObject.SetValue(RuntimeHeightProperty, value);
    }

    public static double GetRuntimeHeight(DependencyObject dependencyObject)
    {
        return (double)dependencyObject.GetValue(RuntimeHeightProperty);
    }

    private static void UpdateWidthOrHeight(
        DependencyObject dependencyObject,
        DependencyProperty propertyToSet,
        DependencyProperty rootProperty,
        DependencyProperty designProperty,
        DependencyProperty runtimeProperty)
    {
        // Design intent wins over runtime sizing, then root/device sizing. NaN means
        // that source is unspecified; clearing the real property preserves its default.
        double value = double.NaN;

        if (!ReferenceEquals(ReadPropertyValue(dependencyObject, designProperty), DependencyProperty.UnsetValue))
        {
            value = (double)dependencyObject.GetValue(designProperty);
        }

        if (double.IsNaN(value) && !ReferenceEquals(ReadPropertyValue(dependencyObject, runtimeProperty), DependencyProperty.UnsetValue))
        {
            value = (double)dependencyObject.GetValue(runtimeProperty);
        }

        if (double.IsNaN(value) && !ReferenceEquals(ReadPropertyValue(dependencyObject, rootProperty), DependencyProperty.UnsetValue))
        {
            value = (double)dependencyObject.GetValue(rootProperty);
        }

        if (double.IsNaN(value))
        {
            dependencyObject.ClearValue(propertyToSet);
        }
        else
        {
            dependencyObject.SetValue(propertyToSet, value);
        }
    }

    internal static bool IsSizeShadowProperty(DependencyProperty dependencyProperty)
    {
        return ReferenceEquals(dependencyProperty, RootWidthProperty) ||
            ReferenceEquals(dependencyProperty, DesignWidthProperty) ||
            ReferenceEquals(dependencyProperty, RuntimeWidthProperty) ||
            ReferenceEquals(dependencyProperty, RootHeightProperty) ||
            ReferenceEquals(dependencyProperty, DesignHeightProperty) ||
            ReferenceEquals(dependencyProperty, RuntimeHeightProperty);
    }

    internal static bool IsSizePropertyShadowed(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        if (ReferenceEquals(dependencyProperty, FrameworkElement.WidthProperty))
        {
            return !ReferenceEquals(ReadPropertyValue(dependencyObject, DesignWidthProperty), DependencyProperty.UnsetValue) || !ReferenceEquals(ReadPropertyValue(dependencyObject, RuntimeWidthProperty), DependencyProperty.UnsetValue) || !ReferenceEquals(ReadPropertyValue(dependencyObject, RootWidthProperty), DependencyProperty.UnsetValue);
        }

        if (ReferenceEquals(dependencyProperty, FrameworkElement.HeightProperty))
        {
            return !ReferenceEquals(ReadPropertyValue(dependencyObject, DesignHeightProperty), DependencyProperty.UnsetValue) || !ReferenceEquals(ReadPropertyValue(dependencyObject, RuntimeHeightProperty), DependencyProperty.UnsetValue) || !ReferenceEquals(ReadPropertyValue(dependencyObject, RootHeightProperty), DependencyProperty.UnsetValue);
        }

        return false;
    }

    internal static object GetUnderlyingSizeShadowValue(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        if (ReferenceEquals(dependencyObject.ReadLocalValue(dependencyProperty), DependencyProperty.UnsetValue))
        {
            return dependencyObject.GetValue(dependencyProperty);
        }

        if (dependencyObject.GetValue(FrameworkElement.StyleProperty) is Style style)
        {
            for (var current = style; current is not null; current = current.BasedOn)
            {
                foreach (SetterBase setterBase in current.Setters)
                {
                    if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty))
                    {
                        return setter.Value;
                    }
                }
            }
        }

        return double.NaN;
    }

    private static object ReadPropertyValue(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        var value = dependencyObject.ReadLocalValue(dependencyProperty);
        if (!ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return value;
        }

        if (dependencyObject.GetValue(FrameworkElement.StyleProperty) is Style style)
        {
            for (var current = style; current is not null; current = current.BasedOn)
            {
                foreach (SetterBase setterBase in current.Setters)
                {
                    if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty))
                    {
                        return setter.Value;
                    }
                }
            }
        }

        return DependencyProperty.UnsetValue;
    }
}
