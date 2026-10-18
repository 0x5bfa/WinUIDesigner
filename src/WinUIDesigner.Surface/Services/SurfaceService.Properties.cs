// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Handles designer requests for live object properties and values.
/// </summary>
internal sealed partial class SurfaceService
{
    private static readonly ConditionalWeakTable<Type, RuntimePropertyMetadata> s_runtimePropertyMetadata = new();

    private static readonly Lazy<AttachedPropertyDescriptor[]> s_attachedPropertyDescriptors = new(CreateAttachedPropertyDescriptors);

    private static readonly Type[] s_attachedPropertyOwners =
    [
        typeof(Canvas),
        typeof(Grid),
        typeof(RelativePanel),
        typeof(ScrollViewer),
        typeof(ToolTipService)
    ];

    private readonly DiagnosticsPropertySourceService _diagnosticsPropertySource;

    private readonly LiveValueSerializer _serializer;

    private LiveObjectState HandleGetProperties(PropertiesRequestInfo request)
    {
        LiveObjectState state;
        try
        {
            state = DispatcherOperation.InvokeAsync(_dispatcherQueue, () =>
            {
                var response = new LiveObjectState
                {
                    Properties = [],
                    Items = [],
                };
                if (!_objectIdentity.TryGetObject(request.Object, out object? value) || value is null)
                {
                    return response;
                }

                using var scope = _objectIdentity.EnterDocument(_objectIdentity.GetDocumentId(value));
                {
                    var runtimeType = value.GetType();
                    RuntimePropertyMetadata propertyMetadata = s_runtimePropertyMetadata.GetValue(runtimeType, CreateRuntimePropertyMetadata);
                    Dictionary<string, BaseValueSource>? diagnosticsPropertySources = null;
                    if (value is DependencyObject target)
                    {
                        if (propertyMetadata.DiagnosticPropertyNames.Length > 0 && _diagnosticsPropertySource.TryGetPropertySources(target, propertyMetadata.DiagnosticPropertyNames, out var sources))
                        {
                            diagnosticsPropertySources = sources;
                        }
                    }

                    foreach (RuntimePropertyDescriptor descriptor in propertyMetadata.Properties)
                    {
                        PropertyInfo property = descriptor.Property;
                        string name = property.Name;
                        try
                        {
                            if (value is DependencyObject dependencyObject && descriptor.DependencyProperty is DependencyProperty dependencyProperty)
                            {
                                if (name is "ActualWidth" or "ActualHeight")
                                {
                                    AddProperty(response.Properties, name, descriptor.DeclaringType, dependencyObject.GetValue(dependencyProperty), BaseValueSource.Local);
                                }
                                else
                                {
                                    AddDependencyProperty(response.Properties, dependencyObject, name, descriptor.DeclaringType, dependencyProperty, diagnosticsPropertySources);
                                }

                                continue;
                            }

                            AddProperty(response.Properties, name, descriptor.DeclaringType, property.GetValue(value), BaseValueSource.Local);
                        }
                        catch
                        {
                        }
                    }

                    if (value is FrameworkElement element)
                    {
                        // Attached layout properties belong to their owner type, not to the
                        // selected element's CLR property list.
                        foreach (AttachedPropertyDescriptor attached in propertyMetadata.AttachedProperties)
                        {
                            try
                            {
                                AddDependencyProperty(response.Properties, element, attached.Name, attached.DeclaringType, attached.DependencyProperty, diagnosticsPropertySources);
                            }
                            catch
                            {
                            }
                        }

                        try
                        {
                            AddDesignTimeProperty(response.Properties, "LayoutSlot", LayoutInformation.GetLayoutSlot(element), BaseValueSource.Local);
                            AddDesignTimeDependencyProperty(response.Properties, element, "DesignWidth", DT.DesignWidthProperty);
                            AddDesignTimeDependencyProperty(response.Properties, element, "RuntimeWidth", DT.RuntimeWidthProperty);
                            AddDesignTimeDependencyProperty(response.Properties, element, "DesignHeight", DT.DesignHeightProperty);
                            AddDesignTimeDependencyProperty(response.Properties, element, "RuntimeHeight", DT.RuntimeHeightProperty);
                        }
                        catch
                        {
                        }
                    }

                    if (value is UIElement visual)
                    {
                        try
                        {
                            AddDesignTimeProperty(response.Properties, "TransformToParent", GetTransformToParent(visual), BaseValueSource.Local);
                        }
                        catch
                        {
                        }
                    }
                }

                if (value is IEnumerable enumerable and not string)
                {
                    foreach (object? item in enumerable)
                    {
                        response.Items.Add(_serializer.Serialize(item));
                    }
                }

                return response;
            }, _protocolHandler.CancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", "Property request failed.", ex);
            int documentId = _objectIdentity.TryGetObject(request.Object, out var target) && target is not null ? _objectIdentity.GetDocumentId(target) : 0;
            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse { DocumentId = documentId, Handle = request.Object, Message = ex.Message, CallStack = ex.ToString(), IsArtboardException = true, });
            state = new LiveObjectState
            {
                Properties = [],
                Items = []
            };
        }

        return state;
    }

    private void AddDependencyProperty(List<LiveObjectPropertyValue> properties, DependencyObject target, string name, Type declaringType, DependencyProperty dependencyProperty, IReadOnlyDictionary<string, BaseValueSource>? diagnosticsPropertySources)
    {
        // DT size shadow properties preserve designer intent separately. Publishing
        // the effective Width/Height as well would make VS feed it back as authored.
        if (DT.IsSizePropertyShadowed(target, dependencyProperty))
        {
            return;
        }

        if (diagnosticsPropertySources is not null && diagnosticsPropertySources.TryGetValue(name, out BaseValueSource diagnosticsValueSource))
        {
            // The frontend edits according to BaseValueSource, not only the current
            // value. Omit Default values so runtime defaults are not authored to XAML.
            if (diagnosticsValueSource != BaseValueSource.Default)
            {
                AddDependencyValue(properties, target, name, declaringType, dependencyProperty, diagnosticsValueSource);
            }

            return;
        }

        object localValue = target.ReadLocalValue(dependencyProperty);
        if (!ReferenceEquals(localValue, DependencyProperty.UnsetValue))
        {
            AddDependencyValue(properties, target, name, declaringType, dependencyProperty, BaseValueSource.Local);
            return;
        }

        if (target is FrameworkElement element && HasStyleSetter(element.Style, dependencyProperty))
        {
            AddDependencyValue(properties, target, name, declaringType, dependencyProperty, BaseValueSource.Style);
        }
    }

    private void AddDependencyValue(List<LiveObjectPropertyValue> properties, DependencyObject target, string name, Type declaringType, DependencyProperty property, BaseValueSource source)
    {
        object? baseValue = XamlRuntimeUtilities.GetBaseValue(target, property);
        properties.Add(new LiveObjectPropertyValue()
        {
            Property = $"{name}:{RuntimeTypeNameSerializer.Serialize(declaringType)}",
            BaseValue = _serializer.Serialize(baseValue),
            CurrentValue = _serializer.Serialize(target.GetValue(property)),
            ValueSource = source,
        });
    }

    private void AddProperty(List<LiveObjectPropertyValue> properties, string name, Type declaringType, object? value, BaseValueSource valueSource)
    {
        properties.Add(new LiveObjectPropertyValue()
        {
            Property = $"{name}:{RuntimeTypeNameSerializer.Serialize(declaringType)}",
            BaseValue = _serializer.Serialize(value),
            CurrentValue = _serializer.Serialize(value),
            ValueSource = valueSource,
        });
    }

    private void AddDesignTimeProperty(List<LiveObjectPropertyValue> properties, string name, object? value, BaseValueSource valueSource)
    {
        properties.Add(new LiveObjectPropertyValue()
        {
            Property = $"{name}:XSurfUwp.DT",
            BaseValue = _serializer.Serialize(value),
            CurrentValue = _serializer.Serialize(value),
            ValueSource = valueSource,
        });
    }

    private void AddDesignTimeDependencyProperty(List<LiveObjectPropertyValue> properties, DependencyObject target, string name, DependencyProperty dependencyProperty)
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

    private static RuntimePropertyMetadata CreateRuntimePropertyMetadata(Type runtimeType)
    {
        string[] propertyNames = [.. runtimeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                property.Name is not ("Parent" or "TemplatedParent" or "XamlRoot" or "DispatcherQueue" or "Dispatcher"))
            .Select(property => property.Name).Distinct()];
        var descriptors = new List<RuntimePropertyDescriptor>(propertyNames.Length);
        foreach (string name in propertyNames)
        {
            PropertyInfo? property = runtimeType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            Type declaringType = property.DeclaringType ?? runtimeType;
            DependencyProperty? dependencyProperty = null;
            if (typeof(DependencyObject).IsAssignableFrom(runtimeType))
            {
                try
                {
                    dependencyProperty = FindDependencyProperty(declaringType, name) ?? (declaringType == runtimeType ? null : FindDependencyProperty(runtimeType, name));
                }
                catch
                {
                    continue;
                }
            }

            descriptors.Add(new RuntimePropertyDescriptor(property, declaringType, dependencyProperty));
        }

        RuntimePropertyDescriptor[] runtimeProperties = [.. descriptors];
        AttachedPropertyDescriptor[] attachedProperties = typeof(FrameworkElement).IsAssignableFrom(runtimeType)
            ? s_attachedPropertyDescriptors.Value
            : [];
        var propertySourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimePropertyDescriptor descriptor in runtimeProperties)
        {
            if (descriptor.DependencyProperty is not null && descriptor.Property.Name is not ("ActualWidth" or "ActualHeight"))
            {
                propertySourceNames.Add(descriptor.Property.Name);
            }
        }

        foreach (AttachedPropertyDescriptor descriptor in attachedProperties)
        {
            propertySourceNames.Add(descriptor.Name);
        }

        return new RuntimePropertyMetadata(runtimeProperties, attachedProperties, [.. propertySourceNames]);
    }

    private static AttachedPropertyDescriptor[] CreateAttachedPropertyDescriptors()
    {
        var descriptors = new List<AttachedPropertyDescriptor>();
        foreach (Type owner in s_attachedPropertyOwners)
        {
            foreach (PropertyInfo attached in owner.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (!attached.Name.EndsWith("Property", StringComparison.Ordinal) || attached.PropertyType != typeof(DependencyProperty))
                {
                    continue;
                }

                string name = attached.Name[..^8];
                if (owner.GetMethod("Get" + name, BindingFlags.Public | BindingFlags.Static) is null)
                {
                    continue;
                }

                try
                {
                    if (attached.GetValue(null) is DependencyProperty dependencyProperty)
                    {
                        descriptors.Add(new AttachedPropertyDescriptor(name, owner, dependencyProperty));
                    }
                }
                catch
                {
                }
            }
        }

        return [.. descriptors];
    }

    private sealed record RuntimePropertyMetadata(RuntimePropertyDescriptor[] Properties, AttachedPropertyDescriptor[] AttachedProperties, string[] DiagnosticPropertyNames);

    private sealed record RuntimePropertyDescriptor(PropertyInfo Property, Type DeclaringType, DependencyProperty? DependencyProperty);

    private sealed record AttachedPropertyDescriptor(string Name, Type DeclaringType, DependencyProperty DependencyProperty);

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty dependencyProperty)
            {
                return dependencyProperty;
            }

            var property = current.GetProperty(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
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
        try
        {
            var value = DispatcherOperation.InvokeAsync(_dispatcherQueue, () =>
            {
                const string DesignTimeTypeName = "XSurfUwp.DT";
                int separator = request.FullPropertyName.IndexOf(':');
                if (separator >= 0 && request.FullPropertyName.AsSpan(separator + 1).StartsWith(DesignTimeTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    return _serializer.Serialize(request.FullPropertyName[..separator] switch
                    {
                        "TransformToParent" => Matrix.Identity,
                        "LayoutSlot" => new Rect(0, 0, 0, 0),
                        "RuntimeWidth" or "RuntimeHeight" or "DesignWidth" or "DesignHeight" => double.NaN,
                        _ => null,
                    });
                }

                return _serializer.Serialize(XamlRuntimeUtilities.GetDefaultValue(request.FullPropertyName, request.TargetTypeName));
            },
            _protocolHandler.CancellationToken)
                .GetAwaiter().GetResult();

            return new LiveValueResponse()
            {
                Value = value
            };
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", "Property request failed.", ex);
            int documentId = _objectIdentity.TryGetObject(0, out var target) && target is not null ? _objectIdentity.GetDocumentId(target) : 0;
            _protocolHandler.PostMessage((int)DesignerMessageId.UnhandledException, new UnhandledExceptionResponse { DocumentId = documentId, Handle = 0, Message = ex.Message, CallStack = ex.ToString(), IsArtboardException = true, });

            return new LiveValueResponse()
            {
                Value = new LiveValue()
            };
        }
    }

    private UnderlyingValueSourceInformation HandleGetUnderlyingValue(UnderlyingValueSourceRequest request)
    {

        try
        {
            var value = DispatcherOperation.InvokeAsync(_dispatcherQueue, () =>
            {
                if (!_objectIdentity.TryGetObject(request.Object, out object? target) || target is null)
                {
                    return new LiveValue();
                }

                return _serializer.Serialize(XamlRuntimeUtilities.GetUnderlyingValue(target, request.Property));
            }, _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            return new UnderlyingValueSourceInformation
            {
                UnderlyingValue = value
            };
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", "Property request failed.", ex);

            int documentId = _objectIdentity.TryGetObject(request.Object, out var target) && target is not null ? _objectIdentity.GetDocumentId(target) : 0;

            _protocolHandler.PostMessage(
                (int)DesignerMessageId.UnhandledException,
                new UnhandledExceptionResponse()
                {
                    DocumentId = documentId,
                    Handle = request.Object,
                    Message = ex.Message,
                    CallStack = ex.ToString(),
                    IsArtboardException = true,
                });

            return new UnderlyingValueSourceInformation()
            {
                UnderlyingValue = new LiveValue()
            };
        }
    }

    private UnderlyingValueSourceInformation HandleExecuteLookupActions(ExecuteXamlActionsRequestInfo request)
    {

        try
        {
            return new UnderlyingValueSourceInformation()
            {
                UnderlyingValue = DispatcherOperation.InvokeAsync(_dispatcherQueue, () => ExecuteLookupActions(request, _serializer), _protocolHandler.CancellationToken).GetAwaiter().GetResult(),
            };
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", "Property request failed.", ex);

            int documentId = _objectIdentity.TryGetObject(0, out var target) && target is not null ? _objectIdentity.GetDocumentId(target) : 0;

            _protocolHandler.PostMessage(
                (int)DesignerMessageId.UnhandledException,
                new UnhandledExceptionResponse()
                {
                    DocumentId = documentId,
                    Handle = 0,
                    Message = ex.Message,
                    CallStack = ex.ToString(),
                    IsArtboardException = true,
                });

            return new UnderlyingValueSourceInformation()
            {
                UnderlyingValue = new LiveValue()
            };
        }
    }

    private LiveValue HandleEvaluateStaticExtension(EvaluateStaticExtensionRequest request)
    {

        try
        {
            return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => _serializer.Serialize(XamlRuntimeUtilities.ResolveMember(request.MemberName)), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", "Property request failed.", ex);

            int documentId = _objectIdentity.TryGetObject(0, out var target) && target is not null ? _objectIdentity.GetDocumentId(target) : 0;

            _protocolHandler.PostMessage(
                (int)DesignerMessageId.UnhandledException,
                new UnhandledExceptionResponse()
                {
                    DocumentId = documentId,
                    Handle = 0,
                    Message = ex.Message,
                    CallStack = ex.ToString(),
                    IsArtboardException = true,
                });

            return new LiveValue();
        }
    }
}
