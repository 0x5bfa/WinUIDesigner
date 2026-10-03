// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

// Resolve and mutate the serialized XAML values used by VS instance-builder actions.
// This maps protocol type names to WinUI without introducing WPF into the Surface.
internal static class XamlRuntimeUtilities
{
    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<string, object?>> NameChanges = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> NameScopeRoots = new();

    public static void RegisterNameScope(FrameworkElement root)
        => NameScopeRoots.GetValue(root, _ => new object());
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

        return ProjectRuntimeResolver.ResolveType(fullName);
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
        DependencyProperty? dependencyProperty = declaringType is null ? null : FindDependencyProperty(declaringType, propertyName);
        if (dependencyProperty is not null)
        {
            // VS also asks about properties of candidate parent/control types. The
            // actual selected element need not derive from the property's owner.
            Type metadataType = targetType is not null && declaringType!.IsAssignableFrom(targetType)
                ? targetType : declaringType!;
            object? value = dependencyProperty.GetMetadata(metadataType).DefaultValue;
            return ReferenceEquals(value, DependencyProperty.UnsetValue) ? null : value;
        }

        PropertyInfo? property = (declaringType ?? targetType)?.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property?.GetCustomAttribute<DefaultValueAttribute>() is { } attribute) return attribute.Value;
        return property?.PropertyType.IsValueType == true ? Activator.CreateInstance(property.PropertyType) : null;
    }

    public static object? GetBaseValue(DependencyObject target, DependencyProperty property)
    {
        if (target is FrameworkElement element && element.GetBindingExpression(property) is { } expression)
            return expression.ParentBinding;
        object local = target.ReadLocalValue(property);
        if (!ReferenceEquals(local, DependencyProperty.UnsetValue)) return local;
        if (target is FrameworkElement styled)
            for (Style? style = styled.Style; style is not null; style = style.BasedOn)
                foreach (SetterBase setterBase in style.Setters)
                    if (setterBase is Setter setter && ReferenceEquals(setter.Property, property)) return setter.Value;
        return target.GetValue(property);
    }

    public static object? GetUnderlyingValue(object target, string fullPropertyName)
    {
        ResolveProperty(target, fullPropertyName, out PropertyInfo? property, out DependencyProperty? dependencyProperty);
        if (dependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            if (XSurfUwp.DT.IsSizeShadowProperty(dependencyProperty))
                return XSurfUwp.DT.GetUnderlyingSizeShadowValue(dependencyObject, dependencyProperty);
            if (target is FrameworkElement element)
                for (Style? style = element.Style; style is not null; style = style.BasedOn)
                    foreach (SetterBase setterBase in style.Setters)
                        if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty)) return setter.Value;
            // A value query must never clear and recreate a binding on the live object.
            return GetDefaultValue(fullPropertyName, target.GetType().AssemblyQualifiedName);
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
            FrameworkElement owner = FindVisualRoot(frameworkElement);
            if (NameChanges.TryGetValue(owner, out var changes) && changes.TryGetValue(elementName, out var changed)) return changed;
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
        if (!string.IsNullOrEmpty(newName) && FindElement(scopeOwner, newName) is { } existing && !ReferenceEquals(existing, element))
            throw new InvalidOperationException($"The name '{newName}' is already registered in this scope.");
        var changes = NameChanges.GetOrCreateValue(scopeOwner);
        if (!string.IsNullOrEmpty(oldName)) changes[oldName] = null;
        element.Name = newName ?? string.Empty;
        if (!string.IsNullOrEmpty(newName)) changes[newName] = element;
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
        if (parameterTypes.Length != arguments.Length)
            throw new ArgumentException("Constructor argument types and handles have different lengths.");
        ConstructorInfo? constructor = RuntimeMemberResolver.FindConstructor(runtimeType, parameterTypes);
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
        object? collection = GetChildCollectionOrSelf(parent);
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
        object? collection = GetChildCollectionOrSelf(parent);
        if (collection is IList list)
        {
            int listInsertionIndex = index < 0 || index > list.Count ? list.Count : index;
            list.Insert(listInsertionIndex, child);
            return;
        }

        int insertionIndex = index;
        if (TryGetCollectionCount(collection, out int count) && (insertionIndex < 0 || insertionIndex > count))
        {
            insertionIndex = count;
        }

        if (TryInvokeCollectionMutation(collection, "Insert", insertionIndex, child)
            || TryInvokeCollectionMutation(collection, "InsertAt", insertionIndex, child)
            || TryInvokeCollectionMutation(collection, "Add", child)
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
        object? collection = GetChildCollectionOrSelf(parent);
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
            if (NameScopeRoots.TryGetValue(current, out _)) break;
            current = parent;
        }
        return current;
    }

    private static object? FindVisualDescendantByName(DependencyObject root, string elementName)
    {
        // Search authored content only. Template-generated visuals participate
        // in their native FindName scope and must not shadow page names here.
        var children = new List<DependencyObject>();
        foreach (string propertyName in new[] { "Children", "Content", "Child" })
        {
            object? value = root.GetType().GetProperty(propertyName)?.GetValue(root);
            if (value is DependencyObject single) children.Add(single);
            else if (value is IEnumerable collection)
                children.AddRange(collection.OfType<DependencyObject>());
        }
        foreach (DependencyObject child in children)
        {
            if (child is FrameworkElement boundary && NameScopeRoots.TryGetValue(boundary, out _)) continue;
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
        return RuntimeMemberResolver.FindMethod(declaringType, methodName, parameterTypes, staticOnly);
    }

    private static object?[] CoerceArguments(object?[] arguments, ParameterInfo[] parameters)
    {
        if (arguments.Length != parameters.Length)
            throw new ArgumentException("Method argument types and handles have different lengths.");
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

    private static object? GetChildCollectionOrSelf(object parent)
    {
        object? collection = GetChildCollection(parent);
        if (collection is not null)
        {
            return collection;
        }

        Type type = parent.GetType();
        if (parent is IList
            || type.GetInterfaces().Any(interfaceType =>
                interfaceType.IsGenericType
                && (interfaceType.GetGenericTypeDefinition() == typeof(IList<>)
                    || interfaceType.GetGenericTypeDefinition() == typeof(ICollection<>))))
        {
            return parent;
        }

        return null;
    }

    private static bool TryGetCollectionCount(object? collection, out int count)
    {
        count = 0;
        if (collection is null)
        {
            return false;
        }

        if (collection is ICollection nonGenericCollection)
        {
            count = nonGenericCollection.Count;
            return true;
        }

        PropertyInfo? countProperty = collection.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        if (countProperty?.PropertyType == typeof(int) && countProperty.GetValue(collection) is int value)
        {
            count = value;
            return true;
        }

        return false;
    }

    private static bool TryInvokeCollectionMutation(object? collection, string methodName, params object?[] arguments)
    {
        if (collection is null)
        {
            return false;
        }

        Type collectionType = collection.GetType();
        IEnumerable<MethodInfo> candidates = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Concat(collectionType.GetInterfaces().SelectMany(interfaceType => interfaceType.GetMethods()))
            .Where(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);

        foreach (MethodInfo method in candidates)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object?[] coerced = new object?[arguments.Length];
            bool compatible = true;
            for (int index = 0; index < arguments.Length; index++)
            {
                try
                {
                    if (arguments[index] is null
                        && parameters[index].ParameterType.IsValueType
                        && Nullable.GetUnderlyingType(parameters[index].ParameterType) is null)
                    {
                        compatible = false;
                        break;
                    }

                    coerced[index] = CoerceValue(arguments[index], parameters[index].ParameterType);
                }
                catch (Exception) when (arguments[index] is not null)
                {
                    compatible = false;
                    break;
                }
            }

            if (!compatible)
            {
                continue;
            }

            method.Invoke(collection, coerced);
            return true;
        }

        return false;
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
