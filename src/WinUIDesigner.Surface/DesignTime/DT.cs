using Microsoft.UI.Xaml;

namespace XSurfUwp;

public static class DT
{
    public static readonly DependencyProperty RootWidthProperty = DependencyProperty.RegisterAttached(
        "RootWidth",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, WidthPropertyChangedCallback));

    public static readonly DependencyProperty DesignWidthProperty = DependencyProperty.RegisterAttached(
        "DesignWidth",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, WidthPropertyChangedCallback));

    public static readonly DependencyProperty RuntimeWidthProperty = DependencyProperty.RegisterAttached(
        "RuntimeWidth",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, WidthPropertyChangedCallback));

    public static readonly DependencyProperty RootHeightProperty = DependencyProperty.RegisterAttached(
        "RootHeight",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, HeightPropertyChangedCallback));

    public static readonly DependencyProperty DesignHeightProperty = DependencyProperty.RegisterAttached(
        "DesignHeight",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, HeightPropertyChangedCallback));

    public static readonly DependencyProperty RuntimeHeightProperty = DependencyProperty.RegisterAttached(
        "RuntimeHeight",
        typeof(double),
        typeof(DT),
        new PropertyMetadata(double.NaN, HeightPropertyChangedCallback));

    public static void SetRootWidth(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(RootWidthProperty, value);

    public static double GetRootWidth(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(RootWidthProperty);

    public static void SetDesignWidth(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(DesignWidthProperty, value);

    public static double GetDesignWidth(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(DesignWidthProperty);

    public static void SetRuntimeWidth(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(RuntimeWidthProperty, value);

    public static double GetRuntimeWidth(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(RuntimeWidthProperty);

    public static void SetRootHeight(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(RootHeightProperty, value);

    public static double GetRootHeight(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(RootHeightProperty);

    public static void SetDesignHeight(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(DesignHeightProperty, value);

    public static double GetDesignHeight(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(DesignHeightProperty);

    public static void SetRuntimeHeight(DependencyObject dependencyObject, double value) => dependencyObject.SetValue(RuntimeHeightProperty, value);

    public static double GetRuntimeHeight(DependencyObject dependencyObject) => (double)dependencyObject.GetValue(RuntimeHeightProperty);

    private static void WidthPropertyChangedCallback(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        => UpdateWidthOrHeight(
            dependencyObject,
            FrameworkElement.WidthProperty,
            RootWidthProperty,
            DesignWidthProperty,
            RuntimeWidthProperty);

    private static void HeightPropertyChangedCallback(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        => UpdateWidthOrHeight(
            dependencyObject,
            FrameworkElement.HeightProperty,
            RootHeightProperty,
            DesignHeightProperty,
            RuntimeHeightProperty);

    private static void UpdateWidthOrHeight(
        DependencyObject dependencyObject,
        DependencyProperty propertyToSet,
        DependencyProperty rootProperty,
        DependencyProperty designProperty,
        DependencyProperty runtimeProperty)
    {
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
        => ReferenceEquals(dependencyProperty, RootWidthProperty)
            || ReferenceEquals(dependencyProperty, DesignWidthProperty)
            || ReferenceEquals(dependencyProperty, RuntimeWidthProperty)
            || ReferenceEquals(dependencyProperty, RootHeightProperty)
            || ReferenceEquals(dependencyProperty, DesignHeightProperty)
            || ReferenceEquals(dependencyProperty, RuntimeHeightProperty);

    internal static bool IsSizePropertyShadowed(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        if (ReferenceEquals(dependencyProperty, FrameworkElement.WidthProperty))
        {
            return HasValue(dependencyObject, DesignWidthProperty)
                || HasValue(dependencyObject, RuntimeWidthProperty)
                || HasValue(dependencyObject, RootWidthProperty);
        }

        if (ReferenceEquals(dependencyProperty, FrameworkElement.HeightProperty))
        {
            return HasValue(dependencyObject, DesignHeightProperty)
                || HasValue(dependencyObject, RuntimeHeightProperty)
                || HasValue(dependencyObject, RootHeightProperty);
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
            foreach (SetterBase setterBase in style.Setters)
            {
                if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty))
                {
                    return setter.Value;
                }
            }
        }

        return double.NaN;
    }

    private static object ReadPropertyValue(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
    {
        object value = dependencyObject.ReadLocalValue(dependencyProperty);
        if (!ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return value;
        }

        if (dependencyObject.GetValue(FrameworkElement.StyleProperty) is Style style)
        {
            foreach (SetterBase setterBase in style.Setters)
            {
                if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty))
                {
                    return setter.Value;
                }
            }
        }

        return DependencyProperty.UnsetValue;
    }

    private static bool HasValue(DependencyObject dependencyObject, DependencyProperty dependencyProperty)
        => !ReferenceEquals(ReadPropertyValue(dependencyObject, dependencyProperty), DependencyProperty.UnsetValue);
}
