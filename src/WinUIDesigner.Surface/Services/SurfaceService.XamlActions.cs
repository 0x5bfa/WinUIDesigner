// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies instance-builder actions to the live WinUI document tree.
/// Scopes proxy handles to each action batch and maps them to stable live-object handles.
/// </summary>
internal sealed partial class SurfaceService
{
    private readonly record struct DeferredSetterInsertion(XamlAction Action, SetterBaseCollection Collection, int Index, SetterBase Setter);

    private readonly ResourceScopeService _resourceScopes;
    private readonly StyleEditService _styleEdits;
    private readonly Dictionary<long, object?> _actionObjects = [];
    private readonly Dictionary<long, object?> _trackedActionObjects = [];

    private ResponseWithError HandleExecuteActions(ExecuteXamlActionsRequestInfo request)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => HandleExecuteActions(request), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new ResponseWithError()
                {
                    HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                    Error = ex.ToString(),
                };
            }
        }

        XamlAction? failedAction = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();

        try
        {
            var actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
            var documentId = 0;

            foreach (XamlAction action in actions)
            {
                if (action is SetDispatcherAction dispatcher && _objectIdentity.TryGetObject(dispatcher.DispatcherObjectHandle, out object? target) && target is not null)
                {
                    documentId = _objectIdentity.GetDocumentId(target);
                }

                if (action is SetSurfaceContentAction content)
                {
                    documentId = content.DocumentId;
                }

                if (documentId != 0)
                {
                    break;
                }
            }

            // Keep the wire order: later actions can refer to objects or names created
            // by earlier actions in this same transaction.
            using var documentScope = _objectIdentity.EnterDocument(documentId);
            foreach (XamlAction action in actions)
            {
                failedAction = action;
                Execute(failedAction, deferredSetterInsertions: deferredSetterInsertions);
                TrackActionObjects();
            }

            foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
            {
                failedAction = insertion.Action;
                InsertSetter(insertion);
            }

            _styleEdits.CompleteBatch(GetSurfaceRoots());
            CompleteActionBatch();

            return new ResponseWithError()
            {
                HResult = 0
            };
        }
        catch (Exception ex)
        {
            // Synchronize the surviving changes, then report the exact failed
            // action using the shared error contract. Raw exception text is not
            // valid ActionError JSON and prevents frontend error processing.
            if (ex is StyleEditException)
            {
                // StyleEditService has logged the cause once; a repeated edit must not flood the log.
                WinUIDesignerLogger.LogTrace("Surface", $"ExecuteXamlActions ({(int)DesignerMessageId.ExecuteXamlActions}) failed during '{failedAction?.Action}': {ex.Message}");
            }
            else
            {
                WinUIDesignerLogger.LogError("Surface", $"ExecuteXamlActions ({(int)DesignerMessageId.ExecuteXamlActions}) failed during '{failedAction?.Action}'.", ex);
            }

            try
            {
                _styleEdits.CompleteBatch(GetSurfaceRoots());
                CompleteActionBatch();
            }
            catch (Exception layoutError)
            {
                WinUIDesignerLogger.LogWarning("Surface", "Layout after failed action also failed.", layoutError);
            }

            return new ResponseWithError()
            {
                HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                Error = failedAction is null ? ex.ToString() : ActionErrorJsonSerializer.Serialize([new() { XamlAction = failedAction, Error = ex.ToString() },]),
            };
        }
    }

    private LiveValue ExecuteLookupActions(ExecuteXamlActionsRequestInfo request, LiveValueSerializer serializer)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => ExecuteLookupActions(request, serializer), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
        }

        object? captured = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();
        var actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
        foreach (XamlAction action in actions)
        {
            Execute(action, (_, value) => captured = value, deferredSetterInsertions);
        }

        foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
        {
            InsertSetter(insertion);
        }

        return serializer.Serialize(captured);
    }

    private object BuildDocument(CreateDocumentInfo document)
    {
        using var scope = _objectIdentity.EnterDocument(document.DocumentId);

        object? root = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();
        var actions = XamlActionJsonSerializer.Deserialize(document.Actions ?? []);

        for (int index = 0; index < actions.Count; index++)
        {
            XamlAction action = actions[index];

            try
            {
                Execute(action, (id, value) =>
                {
                    if (id != document.DocumentId)
                    {
                        throw new InvalidOperationException("The construction action targets another document.");
                    }

                    root = value;
                }, deferredSetterInsertions);

                TrackActionObjects();
            }
            catch (Exception ex)
            {
#if DEBUG
                if (WinUIDesignerLogger.IsVerboseLoggingEnabled
                    && document.Actions is { } serializedActions
                    && index < serializedActions.Count)
                {
                    WinUIDesignerLogger.LogTrace("Surface", $"BuildDocument ({document.DocumentId}) failed at action[{index}]: {serializedActions[index]}", ex);
                }
#endif
                throw new DocumentConstructionException(ex, action);
            }
        }

        foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
        {
            try
            {
                InsertSetter(insertion);
            }
            catch (Exception ex)
            {
                throw new DocumentConstructionException(ex, insertion.Action);
            }
        }

        return root ?? throw new InvalidOperationException($"No root was constructed for document {document.DocumentId}.");
    }

    private static DeferredInsertionQueue<DeferredSetterInsertion> CreateDeferredSetterInsertions()
    {
        return new(insertion => insertion.Collection);
    }

    // Visual Studio sets a Setter's Property and Value after it adds the setter. Once they are set the setter is
    // valid, and an insertion into a sealed collection through XAML Diagnostics also reaches styles BasedOn it.
    private void InsertSetter(DeferredSetterInsertion insertion)
    {
        if (!_styleEdits.TryInsertSetter(insertion.Collection, insertion.Index, insertion.Setter))
        {
            XamlRuntimeUtilities.AddChild(insertion.Collection, insertion.Index, insertion.Setter);
        }
    }

    // Keeps the order of operations on one setter collection: the indices of a batch assume the earlier actions ran.
    private void InsertDeferredSetters(DeferredInsertionQueue<DeferredSetterInsertion>? deferredSetterInsertions, object? collection)
    {
        if (deferredSetterInsertions is null || collection is not SetterBaseCollection)
        {
            return;
        }

        foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeFor(collection))
        {
            InsertSetter(insertion);
        }
    }

    private List<FrameworkElement> GetSurfaceRoots()
    {
        var roots = new List<FrameworkElement>(_surfaces.Count);
        foreach (DesignerSurface surface in _surfaces.Values)
        {
            roots.Add(surface.Content);
        }

        return roots;
    }

    private void TrackActionObjects()
    {
        foreach (var entry in _actionObjects)
        {
            if (entry.Value is not null && (!_trackedActionObjects.TryGetValue(entry.Key, out var oldValue) || !ReferenceEquals(oldValue, entry.Value)))
            {
                _objectIdentity.Track(entry.Value);
            }

            _trackedActionObjects[entry.Key] = entry.Value;
        }
    }

    private void ReleaseDocument(int documentId)
    {
        var released = _objectIdentity.ReleaseDocument(documentId);
        _resourceScopes.RemoveObjects(released);

        foreach (var key in new List<long>(_actionObjects.Keys))
        {
            if (_actionObjects[key] is object value && released.Contains(value))
            {
                _actionObjects.Remove(key);
                _trackedActionObjects.Remove(key);
            }
        }
    }

    private void Execute(
        XamlAction action,
        Action<int, object?>? setSurfaceContentOverride = null,
        DeferredInsertionQueue<DeferredSetterInsertion>? deferredSetterInsertions = null)
    {
        switch (action)
        {
            case ConnectToLiveTreeAction connect:
                _actionObjects[connect.ProxyHandle] = ResolveObject(connect.LiveHandle);
                break;
            case SetSurfaceContentAction setSurfaceContent:
                object? surfaceContent = ResolveObject(setSurfaceContent.RootProxyHandle);
                if (setSurfaceContentOverride is not null)
                {
                    setSurfaceContentOverride(setSurfaceContent.DocumentId, surfaceContent);
                }
                else
                {
                    SetSurfaceContent(setSurfaceContent.DocumentId, surfaceContent);
                }

                break;
            // The stock TAP uses this live-object handle only to choose the dispatcher
            // for the following actions. This surface already executes the whole batch
            // on its single DispatcherQueue, so do not put the dispatcher into the
            // proxy/object map: the same handle still identifies the live WinUI object
            // used by a following ConnectToLiveTreeAction.
            case SetDispatcherAction:
                break;
            case CreateInstanceAction create:
                {
                    Type? runtimeType = XamlRuntimeUtilities.ResolveType(create.TypeName) ?? XamlRuntimeUtilities.ResolveType(create.FallbackTypeName);
                    if (runtimeType is null)
                    {
                        throw new TypeLoadException($"Unable to resolve '{create.TypeName}' (fallback '{create.FallbackTypeName}').");
                    }

                    object? instance;
                    if (typeof(Application).IsAssignableFrom(runtimeType))
                    {
                        // The process already owns its one WinUI Application. Resource actions
                        // target this instance without running the user's application startup.
                        instance = Application.Current;
                    }
                    else if (!string.IsNullOrWhiteSpace(create.FactoryMethod))
                    {
                        instance = XamlRuntimeUtilities.InvokeFactory(runtimeType, create.FactoryMethod, create.ArgumentTypes, create.ArgumentHandles, ResolveObject);
                    }
                    else if (create.ArgumentHandles is { Length: > 0 })
                    {
                        instance = XamlRuntimeUtilities.CreateInstance(runtimeType, create.ArgumentTypes, create.ArgumentHandles, ResolveObject);
                    }
                    else
                    {
                        instance = Activator.CreateInstance(runtimeType);
                    }

                    if (instance is null)
                    {
                        throw new InvalidOperationException($"Failed to create '{runtimeType.FullName}'.");
                    }

                    _actionObjects[create.LiveHandle] = instance;
                    _objectIdentity.GetHandle(instance);
                    if (create.MarkupHandle != 0)
                    {
                        _objectIdentity.RegisterMarkupInfo(instance, create.MarkupHandle, create.ChangeVersion);
                    }

                    break;
                }

            case CreateArrayAction createArray:
                _actionObjects[createArray.LiveHandle] = XamlRuntimeUtilities.CreateArray(
                    createArray.TypeName,
                    createArray.ItemHandles,
                    ResolveObject);
                break;
            case TypeConvertInstanceAction convert:
                _actionObjects[convert.LiveHandle] = XamlRuntimeUtilities.ConvertString(convert.MemberName, convert.Value, convert.IsEnum);
                break;
            case XamlParseInstanceAction parse:
                {
                    object instance = _resourceScopes.Parse(parse.OwnerHandle == 0 ? null : (ResolveObject(parse.OwnerHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parse.OwnerHandle}.")), parse.Xaml);
                    _actionObjects[parse.LiveHandle] = instance;
                    _objectIdentity.GetHandle(instance);
                    if (parse.MarkupHandle != 0)
                    {
                        _objectIdentity.RegisterMarkupInfo(instance, parse.MarkupHandle, parse.ChangeVersion);
                    }

                    break;
                }

            case CreateImplicitDictionaryKeyAction implicitKey:
                _actionObjects[implicitKey.LiveHandle] = XamlRuntimeUtilities.CreateImplicitDictionaryKey(
                    implicitKey.Key,
                    implicitKey.KeyKind == ImplicitKeyKind.Type);
                break;
            case MeasureElementAction measure:
                XamlRuntimeUtilities.MeasureElement((ResolveObject(measure.ElementHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {measure.ElementHandle}.")));
                break;
            case GetPropertyAction getProperty:
                object valueOwner = (ResolveObject(getProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getProperty.ParentHandle}."));
                object? propertyValue = XamlRuntimeUtilities.GetPropertyValue(valueOwner, getProperty.FullPropertyName);
                _actionObjects[getProperty.PropertyValueHandle] = propertyValue;
                _styleEdits.TrackPropertyValue(valueOwner, propertyValue);
                break;
            case SetPropertyAction setProperty:
                object propertyOwner = (ResolveObject(setProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {setProperty.ParentHandle}."));
                object? newValue = ResolveActionValue(setProperty.PropertyValueHandle, propertyOwner);
                if (!_styleEdits.TrySetProperty(propertyOwner, setProperty.FullPropertyName, newValue))
                {
                    XamlRuntimeUtilities.SetPropertyValue(propertyOwner, setProperty.FullPropertyName, newValue);
                }

                break;
            case ClearPropertyAction clearProperty:
                object clearedOwner = (ResolveObject(clearProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {clearProperty.ParentHandle}."));
                if (!_styleEdits.TrySetProperty(clearedOwner, clearProperty.FullPropertyName, null))
                {
                    XamlRuntimeUtilities.ClearPropertyValue(clearedOwner, clearProperty.FullPropertyName, clearProperty.UseDefaultValue, clearProperty.DefaultValue, clearProperty.DefaultValueType);
                }

                break;
            case ClearCollectionPropertyAction clearCollection:
                object collectionOwner = (ResolveObject(clearCollection.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {clearCollection.ParentHandle}."));
                object? clearedCollection = XamlRuntimeUtilities.GetPropertyValue(collectionOwner, clearCollection.FullPropertyName);
                InsertDeferredSetters(deferredSetterInsertions, clearedCollection);
                if (!_styleEdits.TryClearSetters(collectionOwner, clearedCollection))
                {
                    XamlRuntimeUtilities.ClearCollectionProperty(collectionOwner, clearCollection.FullPropertyName);
                }

                break;
            case UpdateEventHandlerAction updateEvent:
                XamlRuntimeUtilities.UpdateEventHandler((ResolveObject(updateEvent.EventOwner) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateEvent.EventOwner}.")), updateEvent.FullEventName, (ResolveObject(updateEvent.HandlerOwner) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateEvent.HandlerOwner}.")), updateEvent.HandlerOwnerType, updateEvent.OldHandler, updateEvent.NewHandler);
                break;
            case GetChildAction getChild:
                object indexedParent = (ResolveObject(getChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getChild.ParentHandle}."));
                InsertDeferredSetters(deferredSetterInsertions, indexedParent);
                _actionObjects[getChild.ChildHandle] = XamlRuntimeUtilities.GetChild(indexedParent, getChild.Index);
                break;
            case AddChildAction addChild:
                object childOwner = (ResolveObject(addChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {addChild.ParentHandle}."));
                object? child = ResolveActionValue(addChild.ChildHandle, childOwner);
                if (childOwner is SetterBaseCollection setterCollection && child is SetterBase setter)
                {
                    var insertion = new DeferredSetterInsertion(action, setterCollection, addChild.Index, setter);
                    if (deferredSetterInsertions is not null)
                    {
                        deferredSetterInsertions.Add(insertion);
                    }
                    else
                    {
                        InsertSetter(insertion);
                    }
                }
                else
                {
                    XamlRuntimeUtilities.AddChild(childOwner, addChild.Index, child);
                }

                break;
            case RemoveChildAction removeChild:
                object removalParent = (ResolveObject(removeChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {removeChild.ParentHandle}."));
                InsertDeferredSetters(deferredSetterInsertions, removalParent);
                if (removalParent is not SetterBaseCollection removalSetters || !_styleEdits.TryRemoveSetter(removalSetters, removeChild.Index))
                {
                    XamlRuntimeUtilities.RemoveChild(removalParent, removeChild.Index);
                }

                break;
            case GetDictionaryEntryAction getDictionaryEntry:
                _actionObjects[getDictionaryEntry.ValueHandle] = XamlRuntimeUtilities.GetDictionaryEntry((ResolveObject(getDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getDictionaryEntry.DictionaryHandle}.")), ResolveObject(getDictionaryEntry.KeyHandle));
                break;
            case AddDictionaryEntryAction addDictionaryEntry:
                object dictionaryOwner = (ResolveObject(addDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {addDictionaryEntry.DictionaryHandle}."));
                object? addedKey = ResolveObject(addDictionaryEntry.KeyHandle);
                object? addedValue = ResolveActionValue(addDictionaryEntry.ValueHandle, dictionaryOwner);
                if (!_styleEdits.TryAddDictionaryEntry(dictionaryOwner, addedKey, addedValue))
                {
                    XamlRuntimeUtilities.SetDictionaryEntry(dictionaryOwner, addedKey, addedValue);
                }

                break;
            case RemoveDictionaryEntryAction removeDictionaryEntry:
                object removalDictionary = (ResolveObject(removeDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {removeDictionaryEntry.DictionaryHandle}."));
                object? removedKey = ResolveObject(removeDictionaryEntry.KeyHandle);
                if (!_styleEdits.TryRemoveDictionaryEntry(removalDictionary, removedKey))
                {
                    XamlRuntimeUtilities.RemoveDictionaryEntry(removalDictionary, removedKey);
                }

                break;
            case UpdateDictionaryValueAction updateDictionaryValue:
                object updatedDictionaryOwner = (ResolveObject(updateDictionaryValue.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateDictionaryValue.DictionaryHandle}."));
                object? updatedKey = ResolveObject(updateDictionaryValue.KeyHandle);
                object? updatedValue = ResolveActionValue(updateDictionaryValue.ValueHandle, updatedDictionaryOwner);
                if (!_styleEdits.TryReplaceDictionaryEntry(updatedDictionaryOwner, updatedKey, updatedValue))
                {
                    XamlRuntimeUtilities.SetDictionaryEntry(updatedDictionaryOwner, updatedKey, updatedValue);
                }

                break;
            case NullReferenceAction nullReference:
                _actionObjects[nullReference.LiveHandle] = null;
                break;
            case MemberReferenceAction memberReference:
                _actionObjects[memberReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(memberReference.MemberName);
                break;
            case StaticReferenceAction staticReference:
                _actionObjects[staticReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(staticReference.MemberName);
                break;
            case FindElementAction findElement:
                _actionObjects[findElement.ElementHandle] = XamlRuntimeUtilities.FindElement((ResolveObject(findElement.LookupContextHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {findElement.LookupContextHandle}.")), findElement.ElementName) ?? throw new InvalidOperationException($"Unable to find element '{findElement.ElementName}' from handle {findElement.LookupContextHandle}.");
                break;
            case UpdateNameRegistrationAction updateName:
                XamlRuntimeUtilities.UpdateNameRegistration((ResolveObject(updateName.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateName.ParentHandle}.")), updateName.OldName, updateName.NewName);
                break;
            case InvokeMethodAction invoke:
                _actionObjects[invoke.LiveHandle] = XamlRuntimeUtilities.InvokeMethod(
                    invoke.InstanceHandle == 0 ? null : ResolveObject(invoke.InstanceHandle),
                    invoke.DeclaringType,
                    invoke.MethodName,
                    invoke.ParameterTypes,
                    invoke.ParameterHandles,
                    ResolveObject);
                break;
            case UpdateResourceMutationsAction resources:
                _resourceScopes.UpdateResources((ResolveObject(resources.Handle) ?? throw new KeyNotFoundException($"No live object is registered for handle {resources.Handle}.")));
                break;
            case SetResourcesSearchParentAction parent:
                _resourceScopes.SetParent((ResolveObject(parent.ChildHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parent.ChildHandle}.")), parent.ParentHandle == 0 ? null : (ResolveObject(parent.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parent.ParentHandle}.")));
                break;
            case SetThemeResourcesEditingScopeAction theme:
                _resourceScopes.SetThemeScope(theme.Assembly, theme.RelativePath);
                break;
            case DisableXBindAction disableXBind:
                XamlRuntimeUtilities.DisableXBind((ResolveObject(disableXBind.DataTemplateComponentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {disableXBind.DataTemplateComponentHandle}.")), disableXBind.LineNumber, disableXBind.ColumnNumber);
                break;
            case TapErrorAction tapError:
                throw new InvalidOperationException($"TAP action stream reported an error for handle {tapError.Handle}.");
            default:
                throw new NotSupportedException($"XAML action '{action.Action}' is not implemented by the WinUI designer surface.");
        }
    }

    private object? ResolveObject(long handle)
    {
        if (_actionObjects.TryGetValue(handle, out var value))
        {
            if (value is not null)
            {
                _objectIdentity.Track(value);
            }

            return value;
        }

        if (_objectIdentity.TryGetObject(handle, out value))
        {
            if (value is not null)
            {
                _objectIdentity.Track(value);
            }

            return value;
        }

        return null;
    }

    private object? ResolveActionValue(long handle, object owner)
    {
        object? value = ResolveObject(handle);
        if (value is StaticResourceReference reference)
        {
            // The native UWP TAP replaces the reference's object state with its value.
            value = _resourceScopes.ResolveStaticResource(owner, reference.ResourceKey);
            _actionObjects[handle] = value;
        }

        return value;
    }
}
