// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

// Resolve and mutate the serialized XAML values used by VS instance-builder actions.
// This maps protocol type names to WinUI without introducing WPF into the Surface.
internal static class XamlRuntimeUtilities
{
    public static Type? ResolveType(string? serializedTypeName)
    {
        if (string.IsNullOrWhiteSpace(serializedTypeName))
        {
            return null;
        }

        // Shared VS action contracts can spell framework types with the UWP prefix;
        // translate that prefix before looking in the WinUI runtime assemblies.
        string candidate = NormalizeWinUITypeName(serializedTypeName.Trim());
        Type? type = Type.GetType(candidate, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        string fullName = candidate.Split(',')[0].Trim();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    public static object? ConvertString(string? serializedTypeName, string? value, bool isEnum = false)
    {
        Type? targetType = ResolveType(serializedTypeName);
        if (targetType is null)
        {
            throw new InvalidOperationException($"Unable to resolve runtime type '{serializedTypeName}'.");
        }

        return ConvertString(targetType, value, isEnum);
    }

    public static object? ConvertString(Type targetType, string? value, bool isEnum = false)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(string))
        {
            return value;
        }

        if (type == typeof(DependencyProperty))
        {
            return ResolveDependencyProperty(value);
        }

        if (type == typeof(Type))
        {
            return ResolveType(value);
        }

        if (isEnum || type.IsEnum)
        {
            return Enum.Parse(type, value ?? string.Empty, ignoreCase: true);
        }

        try
        {
            return XamlBindingHelper.ConvertValue(type, value ?? string.Empty);
        }
        catch
        {
        }

        TypeConverter converter = TypeDescriptor.GetConverter(type);
        if (converter.CanConvertFrom(typeof(string)))
        {
            return converter.ConvertFromInvariantString(value ?? string.Empty);
        }

        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    public static object? GetPropertyValue(object target, string fullPropertyName)
    {
        ResolveProperty(target, fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty);
        if (dependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            return dependencyObject.GetValue(dependencyProperty);
        }

        if (property?.CanRead == true)
        {
            return property.GetValue(target);
        }

        throw new MissingMemberException(target.GetType().FullName, GetPropertyName(fullPropertyName));
    }

    public static void SetPropertyValue(object target, string fullPropertyName, object? value)
    {
        ResolveProperty(target, fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty);
        if (dependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            dependencyObject.SetValue(dependencyProperty, value);
            return;
        }

        if (property?.CanWrite == true)
        {
            property.SetValue(target, CoerceValue(value, property.PropertyType));
            return;
        }

        throw new MissingMemberException(target.GetType().FullName, GetPropertyName(fullPropertyName));
    }

    public static void ClearPropertyValue(object target, string fullPropertyName, bool useDefaultValue, string? defaultValue, string? defaultValueType)
    {
        ResolveProperty(target, fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty);
        if (dependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            if (useDefaultValue && defaultValueType is not null)
            {
                dependencyObject.SetValue(dependencyProperty, ConvertString(defaultValueType, defaultValue));
            }
            else
            {
                dependencyObject.ClearValue(dependencyProperty);
            }
            return;
        }

        if (property?.CanWrite == true)
        {
            object? value = useDefaultValue && defaultValueType is not null
                ? ConvertString(defaultValueType, defaultValue)
                : property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
            property.SetValue(target, value);
            return;
        }

        throw new MissingMemberException(target.GetType().FullName, GetPropertyName(fullPropertyName));
    }

    public static object? GetDefaultValue(string fullPropertyName, string? targetTypeName)
    {
        Type? targetType = ResolveType(targetTypeName);
        string propertyName = GetPropertyName(fullPropertyName);
        Type? declaringType = GetDeclaringType(fullPropertyName) ?? targetType;
        if (targetType is not null &&
            typeof(DependencyObject).IsAssignableFrom(targetType) &&
            !targetType.IsAbstract &&
            targetType.GetConstructor(Type.EmptyTypes) is not null)
        {
            object? target = Activator.CreateInstance(targetType);
            if (target is DependencyObject dependencyObject)
            {
                DependencyProperty? dependencyProperty = FindDependencyProperty(declaringType ?? targetType, propertyName)
                    ?? FindDependencyProperty(targetType, propertyName);
                if (dependencyProperty is not null)
                {
                    return dependencyObject.GetValue(dependencyProperty);
                }
            }
        }

        PropertyInfo? property = (declaringType ?? targetType)?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        return property?.PropertyType.IsValueType == true ? Activator.CreateInstance(property.PropertyType) : null;
    }

    public static object? GetUnderlyingValue(object target, string fullPropertyName)
    {
        ResolveProperty(target, fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty);
        if (dependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            if (XSurfUwp.DT.IsSizeShadowProperty(dependencyProperty))
            {
                return XSurfUwp.DT.GetUnderlyingSizeShadowValue(dependencyObject, dependencyProperty);
            }

            object localValue = dependencyObject.ReadLocalValue(dependencyProperty);
            bool hadLocalValue = !ReferenceEquals(localValue, DependencyProperty.UnsetValue);
            if (hadLocalValue)
            {
                dependencyObject.ClearValue(dependencyProperty);
            }

            try
            {
                return dependencyObject.GetValue(dependencyProperty);
            }
            finally
            {
                if (hadLocalValue)
                {
                    dependencyObject.SetValue(dependencyProperty, localValue);
                }
            }
        }

        return property?.CanRead == true ? property.GetValue(target) : null;
    }

    public static object? ResolveMember(string memberName)
    {
        int separator = memberName.IndexOf(':');
        if (separator > 0)
        {
            string name = memberName[..separator];
            Type? declaringType = ResolveType(memberName[(separator + 1)..]);
            return ResolveStaticMember(declaringType, name);
        }

        int lastDot = memberName.LastIndexOf('.');
        if (lastDot > 0)
        {
            Type? declaringType = ResolveType(memberName[..lastDot]);
            return ResolveStaticMember(declaringType, memberName[(lastDot + 1)..]);
        }

        return null;
    }

    public static Array CreateArray(string serializedItemType, IReadOnlyList<long> itemHandles, Func<long, object?> resolveObject)
    {
        Type itemType = ResolveType(serializedItemType)
            ?? throw new TypeLoadException($"Unable to resolve array item type '{serializedItemType}'.");
        Array array = Array.CreateInstance(itemType, itemHandles.Count);
        for (int index = 0; index < itemHandles.Count; index++)
        {
            array.SetValue(CoerceValue(resolveObject(itemHandles[index]), itemType), index);
        }
        return array;
    }

    public static object? ParseXaml(string xaml)
        => XamlReader.Load(xaml);

    public static object CreateImplicitDictionaryKey(string key, bool isType)
        => isType
            ? ResolveType(key) ?? throw new TypeLoadException($"Unable to resolve implicit dictionary key type '{key}'.")
            : key;

    public static void MeasureElement(object target)
    {
        if (target is not UIElement element)
        {
            throw new InvalidOperationException($"'{target.GetType().FullName}' cannot be measured as a UIElement.");
        }

        double width = target is FrameworkElement frameworkElement && frameworkElement.ActualWidth > 0
            ? frameworkElement.ActualWidth
            : double.PositiveInfinity;
        double height = target is FrameworkElement frameworkElement2 && frameworkElement2.ActualHeight > 0
            ? frameworkElement2.ActualHeight
            : double.PositiveInfinity;
        element.Measure(new Size(width, height));
        element.UpdateLayout();
    }

    public static void ClearCollectionProperty(object target, string fullPropertyName)
    {
        object? collection = GetPropertyValue(target, fullPropertyName);
        if (collection is IList list)
        {
            list.Clear();
            return;
        }

        MethodInfo? clear = collection?.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        if (clear is not null)
        {
            clear.Invoke(collection, null);
            return;
        }

        throw new InvalidOperationException($"Property '{fullPropertyName}' on '{target.GetType().FullName}' is not a clearable collection.");
    }

    public static void UpdateEventHandler(
        object eventOwner,
        string fullEventName,
        object handlerOwner,
        string? handlerOwnerTypeName,
        string? oldHandler,
        string? newHandler)
    {
        string eventName = GetPropertyName(fullEventName);
        EventInfo eventInfo = eventOwner.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(eventOwner.GetType().FullName, eventName);
        Type handlerType = eventInfo.EventHandlerType
            ?? throw new InvalidOperationException($"Event '{eventName}' does not expose a handler type.");
        Type searchType = ResolveType(handlerOwnerTypeName) ?? handlerOwner.GetType();

        if (!string.IsNullOrWhiteSpace(oldHandler))
        {
            Delegate? oldDelegate = CreateEventDelegate(handlerType, handlerOwner, searchType, oldHandler);
            if (oldDelegate is not null)
            {
                eventInfo.RemoveEventHandler(eventOwner, oldDelegate);
            }
        }

        if (!string.IsNullOrWhiteSpace(newHandler))
        {
            Delegate newDelegate = CreateEventDelegate(handlerType, handlerOwner, searchType, newHandler)
                ?? throw new MissingMethodException(searchType.FullName, newHandler);
            eventInfo.AddEventHandler(eventOwner, newDelegate);
        }
    }

    public static object? FindElement(object lookupContext, string elementName)
    {
        if (lookupContext is FrameworkElement frameworkElement)
        {
            object? named = frameworkElement.FindName(elementName);
            if (named is not null)
            {
                return named;
            }
        }

        return lookupContext is DependencyObject dependencyObject
            ? FindVisualDescendantByName(dependencyObject, elementName)
            : null;
    }

    public static void UpdateNameRegistration(object target, string? oldName, string? newName)
    {
        if (target is not FrameworkElement element)
        {
            throw new InvalidOperationException($"'{target.GetType().FullName}' does not participate in a WinUI namescope.");
        }

        FrameworkElement scopeOwner = FindVisualRoot(element);
        MethodInfo? unregisterName = scopeOwner.GetType().GetMethod("UnregisterName", BindingFlags.Public | BindingFlags.Instance, [typeof(string)]);
        MethodInfo? registerName = scopeOwner.GetType().GetMethod("RegisterName", BindingFlags.Public | BindingFlags.Instance, [typeof(string), typeof(object)]);

        if (!string.IsNullOrEmpty(oldName) && unregisterName is not null)
        {
            unregisterName.Invoke(scopeOwner, [oldName]);
        }

        if (!string.IsNullOrEmpty(newName))
        {
            element.Name = newName;
            if (registerName is not null)
            {
                registerName.Invoke(scopeOwner, [newName, element]);
            }
        }
        else
        {
            element.Name = string.Empty;
        }
    }

    public static object? InvokeMethod(
        object? instance,
        string declaringTypeName,
        string methodName,
        IReadOnlyList<string> parameterTypeNames,
        IReadOnlyList<long> parameterHandles,
        Func<long, object?> resolveObject)
    {
        Type declaringType = ResolveType(declaringTypeName)
            ?? throw new TypeLoadException($"Unable to resolve declaring type '{declaringTypeName}'.");
        Type[] parameterTypes = ResolveParameterTypes(parameterTypeNames);
        object?[] arguments = ResolveArguments(parameterHandles, resolveObject);
        MethodInfo method = FindMethod(declaringType, methodName, parameterTypes, instance is null)
            ?? throw new MissingMethodException(declaringType.FullName, methodName);
        return method.Invoke(instance, CoerceArguments(arguments, method.GetParameters()));
    }

    public static object? InvokeFactory(
        Type runtimeType,
        string factoryMethod,
        IReadOnlyList<string>? argumentTypeNames,
        IReadOnlyList<long>? argumentHandles,
        Func<long, object?> resolveObject)
    {
        Type[] parameterTypes = ResolveParameterTypes(argumentTypeNames);
        object?[] arguments = ResolveArguments(argumentHandles, resolveObject);
        MethodInfo method = FindMethod(runtimeType, factoryMethod, parameterTypes, staticOnly: true)
            ?? throw new MissingMethodException(runtimeType.FullName, factoryMethod);
        return method.Invoke(null, CoerceArguments(arguments, method.GetParameters()));
    }

    public static object? CreateInstance(
        Type runtimeType,
        IReadOnlyList<string>? argumentTypeNames,
        IReadOnlyList<long>? argumentHandles,
        Func<long, object?> resolveObject)
    {
        Type[] parameterTypes = ResolveParameterTypes(argumentTypeNames);
        object?[] arguments = ResolveArguments(argumentHandles, resolveObject);
        ConstructorInfo? constructor = parameterTypes.Length == arguments.Length && parameterTypes.Length > 0
            ? runtimeType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null)
            : null;
        constructor ??= runtimeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.GetParameters().Length == arguments.Length);
        if (constructor is null)
        {
            throw new MissingMethodException(runtimeType.FullName, ".ctor");
        }
        return constructor.Invoke(CoerceArguments(arguments, constructor.GetParameters()));
    }

    public static void DisableXBind(object component, int lineNumber, int columnNumber)
    {
        // Generated x:Bind component implementations are project-specific. Some
        // versions expose a design-time disable method; invoke it when present.
        MethodInfo? method = component.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                if (!candidate.Name.Contains("Disable", StringComparison.OrdinalIgnoreCase)
                    || !candidate.Name.Contains("Bind", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].ParameterType == typeof(int);
            });
        if (method is null)
        {
            throw new NotSupportedException($"The x:Bind component '{component.GetType().FullName}' does not expose a design-time disable hook.");
        }

        method.Invoke(component, [lineNumber, columnNumber]);
    }

    public static object? GetChild(object parent, int index)
    {
        object? collection = GetChildCollection(parent);
        if (collection is IList list && index >= 0 && index < list.Count)
        {
            return list[index];
        }

        if (collection is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().ElementAtOrDefault(index);
        }

        if (index == 0)
        {
            PropertyInfo? content = parent.GetType().GetProperty("Content", BindingFlags.Public | BindingFlags.Instance)
                ?? parent.GetType().GetProperty("Child", BindingFlags.Public | BindingFlags.Instance);
            return content?.GetValue(parent);
        }

        return null;
    }

    public static void AddChild(object parent, int index, object? child)
    {
        object? collection = GetChildCollection(parent);
        if (collection is IList list)
        {
            int insertionIndex = index < 0 || index > list.Count ? list.Count : index;
            list.Insert(insertionIndex, child);
            return;
        }

        if (TryInvokeCollectionMutation(collection, "Insert", index, child)
            || TryInvokeCollectionMutation(collection, "InsertAt", index, child)
            || TryInvokeCollectionMutation(collection, "Append", child))
        {
            return;
        }

        PropertyInfo? content = parent.GetType().GetProperty("Content", BindingFlags.Public | BindingFlags.Instance)
            ?? parent.GetType().GetProperty("Child", BindingFlags.Public | BindingFlags.Instance);
        if (content?.CanWrite == true)
        {
            content.SetValue(parent, CoerceValue(child, content.PropertyType));
            return;
        }

        throw new InvalidOperationException($"'{parent.GetType().FullName}' does not expose a mutable child collection.");
    }

    public static void RemoveChild(object parent, int index)
    {
        object? collection = GetChildCollection(parent);
        if (collection is IList list && index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
            return;
        }

        if (TryInvokeCollectionMutation(collection, "RemoveAt", index))
        {
            return;
        }

        if (index == 0)
        {
            PropertyInfo? content = parent.GetType().GetProperty("Content", BindingFlags.Public | BindingFlags.Instance)
                ?? parent.GetType().GetProperty("Child", BindingFlags.Public | BindingFlags.Instance);
            if (content?.CanWrite == true)
            {
                content.SetValue(parent, null);
                return;
            }
        }

        throw new InvalidOperationException($"'{parent.GetType().FullName}' does not expose a mutable child collection.");
    }

    public static object? GetDictionaryEntry(object dictionary, object? key)
    {
        if (dictionary is IDictionary map)
        {
            return map[key!];
        }

        PropertyInfo? indexer = dictionary.GetType().GetProperty("Item", [typeof(object)]);
        return indexer?.GetValue(dictionary, [key]);
    }

    public static void SetDictionaryEntry(object dictionary, object? key, object? value)
    {
        if (dictionary is IDictionary map)
        {
            map[key!] = value;
            return;
        }

        PropertyInfo? indexer = dictionary.GetType().GetProperty("Item", [typeof(object)]);
        if (indexer?.CanWrite == true)
        {
            indexer.SetValue(dictionary, value, [key]);
            return;
        }

        MethodInfo? add = dictionary.GetType().GetMethod("Add", [typeof(object), typeof(object)]);
        if (add is not null)
        {
            add.Invoke(dictionary, [key, value]);
            return;
        }

        throw new InvalidOperationException($"'{dictionary.GetType().FullName}' is not a supported dictionary.");
    }

    public static void RemoveDictionaryEntry(object dictionary, object? key)
    {
        if (dictionary is IDictionary map)
        {
            map.Remove(key!);
            return;
        }

        MethodInfo? remove = dictionary.GetType().GetMethod("Remove", [typeof(object)]);
        if (remove is not null)
        {
            remove.Invoke(dictionary, [key]);
            return;
        }

        throw new InvalidOperationException($"'{dictionary.GetType().FullName}' is not a supported dictionary.");
    }

    private static void ResolveProperty(object target, string fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty)
    {
        string propertyName = GetPropertyName(fullPropertyName);
        Type? declaringType = GetDeclaringType(fullPropertyName);
        Type targetType = target.GetType();
        property = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            ?? declaringType?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        dependencyProperty = FindDependencyProperty(declaringType ?? targetType, propertyName)
            ?? FindDependencyProperty(targetType, propertyName);
    }

    private static string GetPropertyName(string fullPropertyName)
    {
        int separator = fullPropertyName.IndexOf(':');
        return separator < 0 ? fullPropertyName : fullPropertyName[..separator];
    }

    private static Type? GetDeclaringType(string fullPropertyName)
    {
        int separator = fullPropertyName.IndexOf(':');
        return separator < 0 ? null : ResolveType(fullPropertyName[(separator + 1)..]);
    }

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dependencyProperty)
            {
                return dependencyProperty;
            }

            PropertyInfo? property = current.GetProperty(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (property?.GetValue(null) is DependencyProperty propertyValue)
            {
                return propertyValue;
            }
        }

        return null;
    }

    private static DependencyProperty? ResolveDependencyProperty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = NormalizeWinUITypeName(value);
        int separator = normalized.LastIndexOf('.');
        if (separator <= 0)
        {
            return null;
        }

        Type? owner = ResolveType(normalized[..separator]);
        return owner is null ? null : FindDependencyProperty(owner, normalized[(separator + 1)..]);
    }

    private static object? ResolveStaticMember(Type? declaringType, string memberName)
    {
        if (declaringType is null)
        {
            return null;
        }

        FieldInfo? field = declaringType.GetField(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?? declaringType.GetField(memberName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (field is not null)
        {
            return field.GetValue(null);
        }

        PropertyInfo? property = declaringType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?? declaringType.GetProperty(memberName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        return property?.GetValue(null);
    }

    private static Delegate? CreateEventDelegate(Type handlerType, object target, Type searchType, string methodName)
    {
        MethodInfo? method = searchType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        return method is null ? null : Delegate.CreateDelegate(handlerType, target, method, throwOnBindFailure: false);
    }

    private static FrameworkElement FindVisualRoot(FrameworkElement element)
    {
        FrameworkElement current = element;
        while (VisualTreeHelper.GetParent(current) is FrameworkElement parent)
        {
            current = parent;
        }
        return current;
    }

    private static object? FindVisualDescendantByName(DependencyObject root, string elementName)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement { Name: var name } && string.Equals(name, elementName, StringComparison.Ordinal))
            {
                return child;
            }

            object? descendant = FindVisualDescendantByName(child, elementName);
            if (descendant is not null)
            {
                return descendant;
            }
        }
        return null;
    }

    private static Type[] ResolveParameterTypes(IReadOnlyList<string>? serializedTypes)
    {
        if (serializedTypes is null || serializedTypes.Count == 0)
        {
            return [];
        }

        var types = new Type[serializedTypes.Count];
        for (int index = 0; index < types.Length; index++)
        {
            types[index] = ResolveType(serializedTypes[index])
                ?? throw new TypeLoadException($"Unable to resolve parameter type '{serializedTypes[index]}'.");
        }
        return types;
    }

    private static object?[] ResolveArguments(IReadOnlyList<long>? handles, Func<long, object?> resolveObject)
    {
        if (handles is null || handles.Count == 0)
        {
            return [];
        }

        var arguments = new object?[handles.Count];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = resolveObject(handles[index]);
        }
        return arguments;
    }

    private static MethodInfo? FindMethod(Type declaringType, string methodName, Type[] parameterTypes, bool staticOnly)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (staticOnly ? BindingFlags.Static : BindingFlags.Static | BindingFlags.Instance);
        if (parameterTypes.Length > 0)
        {
            MethodInfo? exact = declaringType.GetMethod(methodName, flags, null, parameterTypes, null);
            if (exact is not null)
            {
                return exact;
            }
        }

        return declaringType.GetMethods(flags)
            .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterTypes.Length);
    }

    private static object?[] CoerceArguments(object?[] arguments, ParameterInfo[] parameters)
    {
        var coerced = new object?[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            coerced[index] = CoerceValue(arguments[index], parameters[index].ParameterType);
        }
        return coerced;
    }

    private static object? GetChildCollection(object parent)
    {
        foreach (string name in new[] { "Children", "Items" })
        {
            PropertyInfo? property = parent.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            object? value = property?.GetValue(parent);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryInvokeCollectionMutation(object? collection, string methodName, params object?[] arguments)
    {
        if (collection is null)
        {
            return false;
        }

        MethodInfo? method = collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
        if (method is null)
        {
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object?[] coerced = new object?[arguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            coerced[index] = CoerceValue(arguments[index], parameters[index].ParameterType);
        }
        method.Invoke(collection, coerced);
        return true;
    }

    private static object? CoerceValue(object? value, Type targetType)
    {
        if (value is null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is string text)
        {
            return ConvertString(targetType, text);
        }

        return Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture);
    }

    private static string NormalizeWinUITypeName(string value)
        => value.Replace("Windows.UI.Xaml", "Microsoft.UI.Xaml", StringComparison.Ordinal);
}
