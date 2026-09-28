using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

internal sealed class PropertyService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly XamlActionService xamlActionService;
    private readonly DiagnosticsPropertySourceService diagnosticsPropertySource;
    private readonly LiveValueSerializer serializer;
    private readonly List<int> registrationIds = new();

    public PropertyService(
        ProtocolHandler protocolHandler,
        DispatcherQueue dispatcherQueue,
        ObjectIdentityRegistry objectIdentity,
        XamlActionService xamlActionService)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;
        this.xamlActionService = xamlActionService;
        diagnosticsPropertySource = new DiagnosticsPropertySourceService();
        diagnosticsPropertySource.StartInitialization();
        serializer = new LiveValueSerializer(objectIdentity);

        registrationIds.Add(protocolHandler.RegisterMessageObserver<PropertiesRequestInfo, LiveObjectState>(522, HandleGetProperties));
        registrationIds.Add(protocolHandler.RegisterMessageObserver<DefaultValueRequestInfo, LiveValueResponse>(523, HandleGetDefaultValue));
        registrationIds.Add(protocolHandler.RegisterMessageObserver<UnderlyingValueSourceRequest, UnderlyingValueSourceInformation>(528, HandleGetUnderlyingValue));
        registrationIds.Add(protocolHandler.RegisterMessageObserver<ExecuteXamlActionsRequestInfo, UnderlyingValueSourceInformation>(539, HandleExecuteLookupActions));
        registrationIds.Add(protocolHandler.RegisterMessageObserver<EvaluateStaticExtensionRequest, LiveValue>(546, HandleEvaluateStaticExtension));
    }

    private LiveObjectState HandleGetProperties(PropertiesRequestInfo request)
    {
        LiveObjectState response = InvokeOnDispatcher(() => CreateObjectState(request.Object));
        Program.WriteDiagnosticTrace($"GetProperties (522) completed for handle {request.Object}: properties={response.Properties.Count}, items={response.Items.Count}.");
        return response;
    }

    private LiveObjectState CreateObjectState(long handle)
    {
        var response = new LiveObjectState
        {
            Properties = new List<LiveObjectPropertyValue>(),
            Items = new List<LiveValue>(),
        };

        if (!objectIdentity.TryGetObject(handle, out object? value) || value is null)
        {
            return response;
        }

        AddKnownProperties(value, response.Properties);
        if (value is FrameworkElement element)
        {
            Program.WriteDiagnosticTrace($"Layout properties (522): handle={handle}, type={value.GetType().FullName}, size={element.ActualWidth}x{element.ActualHeight}, margin={element.Margin}, transform={GetTransformToParent(element)}.");
        }

        if (value is IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                response.Items.Add(serializer.Serialize(item));
            }
        }

        return response;
    }

    private void AddKnownProperties(object value, List<LiveObjectPropertyValue> properties)
    {
        string[] propertyNames = value is FrameworkElement
            ? [
                "DesiredSize", "RenderSize", "Visibility", "Name",
                "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
                "ActualWidth", "ActualHeight", "Margin", "HorizontalAlignment", "VerticalAlignment",
                "FlowDirection", "Resources", "Style", "UseLayoutRounding", "RenderTransform", "RenderTransformOrigin"
            ]
            : ["Visibility"];

        Type runtimeType = value.GetType();
        foreach (string name in propertyNames)
        {
            PropertyInfo? property = runtimeType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                Type declaringType = property.DeclaringType ?? runtimeType;
                if (value is DependencyObject dependencyObject
                    && FindDependencyProperty(declaringType, runtimeType, name) is DependencyProperty dependencyProperty)
                {
                    if (name is "ActualWidth" or "ActualHeight")
                    {
                        AddProperty(properties, name, declaringType, dependencyObject.GetValue(dependencyProperty), BaseValueSource.Local);
                    }
                    else
                    {
                        AddDependencyProperty(properties, dependencyObject, name, declaringType, dependencyProperty);
                    }

                    continue;
                }

                AddProperty(properties, name, declaringType, property.GetValue(value), BaseValueSource.Local);
            }
            catch
            {
            }
        }

        if (value is FrameworkElement element)
        {
            try
            {
                AddDesignTimeProperty(
                    properties,
                    "LayoutSlot",
                    LayoutInformation.GetLayoutSlot(element),
                    BaseValueSource.Local);
                AddDesignTimeDependencyProperty(properties, element, "DesignWidth", XSurfUwp.DT.DesignWidthProperty);
                AddDesignTimeDependencyProperty(properties, element, "RuntimeWidth", XSurfUwp.DT.RuntimeWidthProperty);
                AddDesignTimeDependencyProperty(properties, element, "DesignHeight", XSurfUwp.DT.DesignHeightProperty);
                AddDesignTimeDependencyProperty(properties, element, "RuntimeHeight", XSurfUwp.DT.RuntimeHeightProperty);
            }
            catch
            {
            }
        }

        if (value is UIElement visual)
        {
            try
            {
                AddDesignTimeProperty(
                    properties,
                    "TransformToParent",
                    GetTransformToParent(visual),
                    BaseValueSource.Local);
            }
            catch
            {
            }
        }
    }

    private void AddDependencyProperty(
        List<LiveObjectPropertyValue> properties,
        DependencyObject target,
        string name,
        Type declaringType,
        DependencyProperty dependencyProperty)
    {
        if (XSurfUwp.DT.IsSizePropertyShadowed(target, dependencyProperty))
        {
            return;
        }

        if (diagnosticsPropertySource.TryGetPropertySource(target, name, out BaseValueSource diagnosticsValueSource))
        {
            if (diagnosticsValueSource != BaseValueSource.Default)
            {
                AddProperty(properties, name, declaringType, target.GetValue(dependencyProperty), diagnosticsValueSource);
            }

            return;
        }

        object localValue = target.ReadLocalValue(dependencyProperty);
        if (!ReferenceEquals(localValue, DependencyProperty.UnsetValue))
        {
            AddProperty(properties, name, declaringType, target.GetValue(dependencyProperty), BaseValueSource.Local);
            return;
        }

        if (target is FrameworkElement element && HasStyleSetter(element.Style, dependencyProperty))
        {
            AddProperty(properties, name, declaringType, target.GetValue(dependencyProperty), BaseValueSource.Style);
        }
    }

    private void AddProperty(
        List<LiveObjectPropertyValue> properties,
        string name,
        Type declaringType,
        object? value,
        BaseValueSource valueSource)
    {
        properties.Add(new LiveObjectPropertyValue
        {
            Property = LiveValueSerializer.SerializeProperty(name, declaringType),
            BaseValue = serializer.Serialize(value),
            ValueSource = valueSource,
        });
    }

    private void AddDesignTimeProperty(
        List<LiveObjectPropertyValue> properties,
        string name,
        object? value,
        BaseValueSource valueSource)
    {
        properties.Add(new LiveObjectPropertyValue
        {
            Property = $"{name}:XSurfUwp.DT",
            BaseValue = serializer.Serialize(value),
            ValueSource = valueSource,
        });
    }

    private void AddDesignTimeDependencyProperty(
        List<LiveObjectPropertyValue> properties,
        DependencyObject target,
        string name,
        DependencyProperty dependencyProperty)
    {
        object localValue = target.ReadLocalValue(dependencyProperty);
        if (!ReferenceEquals(localValue, DependencyProperty.UnsetValue))
        {
            AddDesignTimeProperty(properties, name, target.GetValue(dependencyProperty), BaseValueSource.Local);
            return;
        }

        if (target is FrameworkElement element && HasStyleSetter(element.Style, dependencyProperty))
        {
            AddDesignTimeProperty(properties, name, target.GetValue(dependencyProperty), BaseValueSource.Style);
        }
    }

    private static bool HasStyleSetter(Style? style, DependencyProperty dependencyProperty)
    {
        for (Style? current = style; current is not null; current = current.BasedOn)
        {
            foreach (SetterBase setterBase in current.Setters)
            {
                if (setterBase is Setter setter && ReferenceEquals(setter.Property, dependencyProperty))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DependencyProperty? FindDependencyProperty(Type declaringType, Type runtimeType, string propertyName)
    {
        DependencyProperty? result = FindDependencyProperty(declaringType, propertyName);
        return result ?? (declaringType == runtimeType ? null : FindDependencyProperty(runtimeType, propertyName));
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

    private static Matrix GetTransformToParent(UIElement visual)
    {
        if (VisualTreeHelper.GetParent(visual) is not UIElement parent)
        {
            return Matrix.Identity;
        }

        GeneralTransform transform = visual.TransformToVisual(parent);
        Point origin = transform.TransformPoint(new Point(0, 0));
        Point xAxis = transform.TransformPoint(new Point(1, 0));
        Point yAxis = transform.TransformPoint(new Point(0, 1));
        Matrix matrix = new()
        {
            M11 = xAxis.X - origin.X,
            M12 = xAxis.Y - origin.Y,
            M21 = yAxis.X - origin.X,
            M22 = yAxis.Y - origin.Y,
            OffsetX = origin.X,
            OffsetY = origin.Y,
        };

        return matrix;
    }

    private LiveValueResponse HandleGetDefaultValue(DefaultValueRequestInfo request)
    {
        Program.WriteDiagnosticTrace($"GetDefaultValue (523): property={request.FullPropertyName}, target={request.TargetTypeName}.");
        LiveValue value = InvokeOnDispatcher(() => serializer.Serialize(GetDefaultValue(request.FullPropertyName, request.TargetTypeName)));
        return new LiveValueResponse { Value = value };
    }

    private static object? GetDefaultValue(string fullPropertyName, string? targetTypeName)
    {
        const string DesignTimeTypeName = "XSurfUwp.DT";
        int separator = fullPropertyName.IndexOf(':');
        if (separator >= 0 && fullPropertyName.AsSpan(separator + 1).StartsWith(DesignTimeTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return fullPropertyName[..separator] switch
            {
                "TransformToParent" => Matrix.Identity,
                "LayoutSlot" => new Rect(0, 0, 0, 0),
                "RuntimeWidth" or "RuntimeHeight" or "DesignWidth" or "DesignHeight" => double.NaN,
                _ => null,
            };
        }

        return XamlRuntimeUtilities.GetDefaultValue(fullPropertyName, targetTypeName);
    }

    private UnderlyingValueSourceInformation HandleGetUnderlyingValue(UnderlyingValueSourceRequest request)
    {
        Program.WriteDiagnosticTrace($"GetUnderlyingValue (528): handle={request.Object}, property={request.Property}.");
        LiveValue value = InvokeOnDispatcher(() =>
        {
            if (!objectIdentity.TryGetObject(request.Object, out object? target) || target is null)
            {
                return new LiveValue();
            }

            return serializer.Serialize(XamlRuntimeUtilities.GetUnderlyingValue(target, request.Property));
        });
        return new UnderlyingValueSourceInformation { UnderlyingValue = value };
    }

    private UnderlyingValueSourceInformation HandleExecuteLookupActions(ExecuteXamlActionsRequestInfo request)
    {
        Program.WriteDiagnosticTrace($"Execute lookup actions (539): count={request.Actions?.Count ?? 0}.");
        return new UnderlyingValueSourceInformation
        {
            UnderlyingValue = xamlActionService.ExecuteLookupActions(request, serializer),
        };
    }

    private LiveValue HandleEvaluateStaticExtension(EvaluateStaticExtensionRequest request)
    {
        Program.WriteDiagnosticTrace($"EvaluateStaticExtension (546): member={request.MemberName}, type={request.TypeName}.");
        return InvokeOnDispatcher(() => serializer.Serialize(XamlRuntimeUtilities.ResolveMember(request.MemberName)));
    }

    private T InvokeOnDispatcher<T>(Func<T> callback)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            return InvokeSafely(callback);
        }

        T? result = default;
        using var completion = new System.Threading.ManualResetEventSlim();
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                result = InvokeSafely(callback);
            }
            finally
            {
                completion.Set();
            }
        }))
        {
            Program.WriteDiagnosticTrace("Property request could not be queued on the WinUI DispatcherQueue.");
            return default!;
        }

        completion.Wait();
        return result!;
    }

    private static T InvokeSafely<T>(Func<T> callback)
    {
        try
        {
            return callback();
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"Property request failed: {ex}");
            return default!;
        }
    }

    public void Dispose()
    {
        foreach (int registrationId in registrationIds)
        {
            protocolHandler.UnregisterMessageObserver(registrationId);
        }
    }
}
