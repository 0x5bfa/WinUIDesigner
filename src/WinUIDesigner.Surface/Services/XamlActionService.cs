// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

// Apply the instance builder's serialized action sequence to the live WinUI tree.
// Proxy handles are scoped to the action batch and map to stable live-object handles.
internal sealed class XamlActionService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly SurfaceService surfaceService;
    private readonly ResourceScopeService resourceScopes;
    private readonly Dictionary<long, object?> actionObjects = new();
    private readonly Dictionary<long, object?> trackedActionObjects = new();
    private readonly int registrationId;

    public XamlActionService(
        ProtocolHandler protocolHandler,
        DispatcherQueue dispatcherQueue,
        ObjectIdentityRegistry objectIdentity,
        SurfaceService surfaceService)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;
        this.surfaceService = surfaceService;
        resourceScopes = new ResourceScopeService(objectIdentity);
        registrationId = protocolHandler.RegisterMessageObserver<ExecuteXamlActionsRequestInfo, ResponseWithError>(507, HandleExecuteActions);
    }

    private ResponseWithError HandleExecuteActions(ExecuteXamlActionsRequestInfo request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleExecuteActions(request));
        }

        XamlAction? failedAction = null;
        try
        {
            IList<XamlAction> actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
            int documentId = 0;
            foreach (XamlAction action in actions)
            {
                if (action is SetDispatcherAction dispatcher && objectIdentity.TryGetObject(dispatcher.DispatcherObjectHandle, out object? target) && target is not null)
                {
                    documentId = objectIdentity.GetDocumentId(target);
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
            using var documentScope = objectIdentity.EnterDocument(documentId);
            // Keep the wire order: later actions can refer to objects or names created
            // by earlier actions in this same transaction.
            for (int index = 0; index < actions.Count; index++)
            {
                if (request.Actions is { } serializedActions && index < serializedActions.Count)
                {
                    Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) action[{index}]: {serializedActions[index]}");
                }

                failedAction = actions[index];
                Execute(failedAction);
                TrackActionObjects();
            }

            surfaceService.CompleteActionBatch();
            Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) completed: count={actions.Count}.");
            return Success;
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) failed: {ex}");
            // Synchronize the surviving changes, then report the exact failed
            // action using the shared error contract. Raw exception text is not
            // valid ActionError JSON and prevents frontend error processing.
            try
            {
                surfaceService.CompleteActionBatch();
            }
            catch (Exception layoutError)
            {
                Program.WriteDiagnosticTrace($"Layout after failed action: {layoutError}");
            }
            return Failure(ex, failedAction);
        }
    }

    public LiveValue ExecuteLookupActions(ExecuteXamlActionsRequestInfo request, LiveValueSerializer serializer)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return DispatcherOperation.Invoke(dispatcherQueue, () => ExecuteLookupActions(request, serializer), protocolHandler.CancellationToken);
        }

        object? captured = null;
        IList<XamlAction> actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
        foreach (XamlAction action in actions)
        {
            Execute(action, (_, value) => captured = value);
        }

        return serializer.Serialize(captured);
    }

    public object BuildDocument(CreateDocumentInfo document)
    {
        using var scope = objectIdentity.EnterDocument(document.DocumentId);
        object? root = null;
        IList<XamlAction> actions = XamlActionJsonSerializer.Deserialize(document.Actions ?? []);
        for (int index = 0; index < actions.Count; index++)
        {
            XamlAction action = actions[index];
            if (document.Actions is { } serializedActions && index < serializedActions.Count)
                Program.WriteDiagnosticTrace($"BuildDocument ({document.DocumentId}) action[{index}]: {serializedActions[index]}");
            try
            {
            Execute(action, (id, value) =>
            {
                if (id != document.DocumentId)
                {
                    throw new InvalidOperationException("The construction action targets another document.");
                }

                root = value;
            });
            TrackActionObjects();
            }
            catch (Exception ex)
            {
                throw new DocumentConstructionException(ex, action);
            }
        }
        return root ?? throw new InvalidOperationException($"No root was constructed for document {document.DocumentId}.");
    }

    private void TrackActionObjects()
    {
        foreach (var entry in actionObjects)
        {
            if (entry.Value is not null && (!trackedActionObjects.TryGetValue(entry.Key, out var oldValue) || !ReferenceEquals(oldValue, entry.Value)))
                objectIdentity.Track(entry.Value);
            trackedActionObjects[entry.Key] = entry.Value;
        }
    }

    public void ReleaseDocument(int documentId)
    {
        var released = objectIdentity.ReleaseDocument(documentId);
        resourceScopes.RemoveObjects(released);
        foreach (long key in new List<long>(actionObjects.Keys))
        {
            if (actionObjects[key] is object value && released.Contains(value))
            {
                actionObjects.Remove(key);
                trackedActionObjects.Remove(key);
            }
        }
    }

    private void Execute(XamlAction action, Action<int, object?>? setSurfaceContentOverride = null)
    {
        switch (action)
        {
            case ConnectToLiveTreeAction connect:
                actionObjects[connect.ProxyHandle] = ResolveObject(connect.LiveHandle);
                break;

            case SetSurfaceContentAction setSurfaceContent:
                object? surfaceContent = ResolveObject(setSurfaceContent.RootProxyHandle);
                if (setSurfaceContentOverride is not null)
                {
                    setSurfaceContentOverride(setSurfaceContent.DocumentId, surfaceContent);
                }
                else
                {
                    surfaceService.SetSurfaceContent(setSurfaceContent.DocumentId, surfaceContent);
                }
                break;

            case SetDispatcherAction:
                // The stock TAP uses this live-object handle only to choose the dispatcher
                // for the following actions. This surface already executes the whole batch
                // on its single DispatcherQueue, so do not put the dispatcher into the
                // proxy/object map: the same handle still identifies the live WinUI object
                // used by a following ConnectToLiveTreeAction.
                break;

            case CreateInstanceAction create:
                ExecuteCreateInstance(create);
                break;

            case CreateArrayAction createArray:
                actionObjects[createArray.LiveHandle] = XamlRuntimeUtilities.CreateArray(
                    createArray.TypeName,
                    createArray.ItemHandles,
                    ResolveObject);
                break;

            case TypeConvertInstanceAction convert:
                actionObjects[convert.LiveHandle] = XamlRuntimeUtilities.ConvertString(convert.MemberName, convert.Value, convert.IsEnum);
                break;

            case XamlParseInstanceAction parse:
                ExecuteXamlParseInstance(parse);
                break;

            case CreateImplicitDictionaryKeyAction implicitKey:
                actionObjects[implicitKey.LiveHandle] = XamlRuntimeUtilities.CreateImplicitDictionaryKey(
                    implicitKey.Key,
                    implicitKey.KeyKind == ImplicitKeyKind.Type);
                break;

            case MeasureElementAction measure:
                XamlRuntimeUtilities.MeasureElement(RequireObject(measure.ElementHandle));
                break;

            case GetPropertyAction getProperty:
                actionObjects[getProperty.PropertyValueHandle] = XamlRuntimeUtilities.GetPropertyValue(RequireObject(getProperty.ParentHandle), getProperty.FullPropertyName);
                break;

            case SetPropertyAction setProperty:
                object propertyOwner = RequireObject(setProperty.ParentHandle);
                XamlRuntimeUtilities.SetPropertyValue(propertyOwner, setProperty.FullPropertyName,
                    ResolveActionValue(setProperty.PropertyValueHandle, propertyOwner));
                break;

            case ClearPropertyAction clearProperty:
                XamlRuntimeUtilities.ClearPropertyValue(
                    RequireObject(clearProperty.ParentHandle),
                    clearProperty.FullPropertyName,
                    clearProperty.UseDefaultValue,
                    clearProperty.DefaultValue,
                    clearProperty.DefaultValueType);
                break;

            case ClearCollectionPropertyAction clearCollection:
                XamlRuntimeUtilities.ClearCollectionProperty(
                    RequireObject(clearCollection.ParentHandle),
                    clearCollection.FullPropertyName);
                break;

            case UpdateEventHandlerAction updateEvent:
                XamlRuntimeUtilities.UpdateEventHandler(
                    RequireObject(updateEvent.EventOwner),
                    updateEvent.FullEventName,
                    RequireObject(updateEvent.HandlerOwner),
                    updateEvent.HandlerOwnerType,
                    updateEvent.OldHandler,
                    updateEvent.NewHandler);
                break;

            case GetChildAction getChild:
                actionObjects[getChild.ChildHandle] = XamlRuntimeUtilities.GetChild(RequireObject(getChild.ParentHandle), getChild.Index);
                break;

            case AddChildAction addChild:
                object childOwner = RequireObject(addChild.ParentHandle);
                XamlRuntimeUtilities.AddChild(childOwner, addChild.Index, ResolveActionValue(addChild.ChildHandle, childOwner));
                break;

            case RemoveChildAction removeChild:
                XamlRuntimeUtilities.RemoveChild(RequireObject(removeChild.ParentHandle), removeChild.Index);
                break;

            case GetDictionaryEntryAction getDictionaryEntry:
                actionObjects[getDictionaryEntry.ValueHandle] = XamlRuntimeUtilities.GetDictionaryEntry(
                    RequireObject(getDictionaryEntry.DictionaryHandle),
                    ResolveObject(getDictionaryEntry.KeyHandle));
                break;

            case AddDictionaryEntryAction addDictionaryEntry:
                object dictionaryOwner = RequireObject(addDictionaryEntry.DictionaryHandle);
                XamlRuntimeUtilities.SetDictionaryEntry(
                    dictionaryOwner,
                    ResolveObject(addDictionaryEntry.KeyHandle),
                    ResolveActionValue(addDictionaryEntry.ValueHandle, dictionaryOwner));
                break;

            case RemoveDictionaryEntryAction removeDictionaryEntry:
                XamlRuntimeUtilities.RemoveDictionaryEntry(
                    RequireObject(removeDictionaryEntry.DictionaryHandle),
                    ResolveObject(removeDictionaryEntry.KeyHandle));
                break;

            case UpdateDictionaryValueAction updateDictionaryValue:
                object updatedDictionaryOwner = RequireObject(updateDictionaryValue.DictionaryHandle);
                XamlRuntimeUtilities.SetDictionaryEntry(
                    updatedDictionaryOwner,
                    ResolveObject(updateDictionaryValue.KeyHandle),
                    ResolveActionValue(updateDictionaryValue.ValueHandle, updatedDictionaryOwner));
                break;

            case NullReferenceAction nullReference:
                actionObjects[nullReference.LiveHandle] = null;
                break;

            case MemberReferenceAction memberReference:
                actionObjects[memberReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(memberReference.MemberName);
                break;

            case StaticReferenceAction staticReference:
                actionObjects[staticReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(staticReference.MemberName);
                break;

            case FindElementAction findElement:
                actionObjects[findElement.ElementHandle] = XamlRuntimeUtilities.FindElement(
                    RequireObject(findElement.LookupContextHandle),
                    findElement.ElementName)
                    ?? throw new InvalidOperationException($"Unable to find element '{findElement.ElementName}' from handle {findElement.LookupContextHandle}.");
                break;

            case UpdateNameRegistrationAction updateName:
                XamlRuntimeUtilities.UpdateNameRegistration(
                    RequireObject(updateName.ParentHandle),
                    updateName.OldName,
                    updateName.NewName);
                break;

            case InvokeMethodAction invoke:
                actionObjects[invoke.LiveHandle] = XamlRuntimeUtilities.InvokeMethod(
                    invoke.InstanceHandle == 0 ? null : ResolveObject(invoke.InstanceHandle),
                    invoke.DeclaringType,
                    invoke.MethodName,
                    invoke.ParameterTypes,
                    invoke.ParameterHandles,
                    ResolveObject);
                break;

            case UpdateResourceMutationsAction resources:
                resourceScopes.UpdateResources(RequireObject(resources.Handle));
                break;
            case SetResourcesSearchParentAction parent:
                resourceScopes.SetParent(RequireObject(parent.ChildHandle), parent.ParentHandle == 0 ? null : RequireObject(parent.ParentHandle));
                break;
            case SetThemeResourcesEditingScopeAction theme:
                resourceScopes.SetThemeScope(theme.Assembly, theme.RelativePath);
                break;

            case DisableXBindAction disableXBind:
                XamlRuntimeUtilities.DisableXBind(
                    RequireObject(disableXBind.DataTemplateComponentHandle),
                    disableXBind.LineNumber,
                    disableXBind.ColumnNumber);
                break;

            case TapErrorAction tapError:
                throw new InvalidOperationException($"TAP action stream reported an error for handle {tapError.Handle}.");

            default:
                throw new NotSupportedException($"XAML action '{action.Action}' is not implemented by the WinUI designer surface.");
        }
    }

    private void ExecuteCreateInstance(CreateInstanceAction action)
    {
        Type? runtimeType = XamlRuntimeUtilities.ResolveType(action.TypeName)
            ?? XamlRuntimeUtilities.ResolveType(action.FallbackTypeName);
        if (runtimeType is null)
        {
            throw new TypeLoadException($"Unable to resolve '{action.TypeName}' (fallback '{action.FallbackTypeName}').");
        }

        object? instance;
        if (typeof(Application).IsAssignableFrom(runtimeType))
        {
            // The process already owns its one WinUI Application. Resource actions
            // target this instance without running the user's application startup.
            instance = Application.Current;
        }
        else if (!string.IsNullOrWhiteSpace(action.FactoryMethod))
        {
            instance = XamlRuntimeUtilities.InvokeFactory(
                runtimeType,
                action.FactoryMethod,
                action.ArgumentTypes,
                action.ArgumentHandles,
                ResolveObject);
        }
        else if (action.ArgumentHandles is { Length: > 0 })
        {
            instance = XamlRuntimeUtilities.CreateInstance(
                runtimeType,
                action.ArgumentTypes,
                action.ArgumentHandles,
                ResolveObject);
        }
        else
        {
            instance = Activator.CreateInstance(runtimeType);
        }

        if (instance is null)
        {
            throw new InvalidOperationException($"Failed to create '{runtimeType.FullName}'.");
        }

        actionObjects[action.LiveHandle] = instance;
        objectIdentity.GetHandle(instance);
        if (action.MarkupHandle != 0)
        {
            objectIdentity.RegisterMarkupInfo(instance, action.MarkupHandle, action.ChangeVersion);
        }
    }

    private void ExecuteXamlParseInstance(XamlParseInstanceAction action)
    {
        object instance = resourceScopes.Parse(action.OwnerHandle == 0 ? null : RequireObject(action.OwnerHandle), action.Xaml);
        actionObjects[action.LiveHandle] = instance;
        objectIdentity.GetHandle(instance);
        if (action.MarkupHandle != 0)
        {
            objectIdentity.RegisterMarkupInfo(instance, action.MarkupHandle, action.ChangeVersion);
        }
    }

    private object? ResolveObject(long handle)
    {
        if (actionObjects.TryGetValue(handle, out object? value))
        {
            if (value is not null)
            {
                objectIdentity.Track(value);
            }

            return value;
        }

        if (objectIdentity.TryGetObject(handle, out value))
        {
            if (value is not null)
            {
                objectIdentity.Track(value);
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
            value = resourceScopes.ResolveStaticResource(owner, reference.ResourceKey);
            // The native UWP TAP replaces the reference's object state with its value.
            actionObjects[handle] = value;
        }
        return value;
    }

    private object RequireObject(long handle)
    {
        return ResolveObject(handle) ?? throw new KeyNotFoundException($"No live object is registered for handle {handle}.");
    }

    private ResponseWithError InvokeOnDispatcher(Func<ResponseWithError> callback)
    {
        try
        {
            return DispatcherOperation.Invoke(dispatcherQueue, callback, protocolHandler.CancellationToken);
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }
    }

    private static ResponseWithError Success => new() { HResult = 0 };

    private static ResponseWithError Failure(Exception exception, XamlAction? action = null)
    {
        return new()
        {
            HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
            Error = action is null ? exception.ToString() : ActionErrorJsonSerializer.Serialize(new List<ActionError>
            {
                new() { XamlAction = action, Error = exception.ToString() },
            }),
        };
    }

    public void Dispose()
    {
        actionObjects.Clear();
        trackedActionObjects.Clear();
        protocolHandler.UnregisterMessageObserver(registrationId);
    }
}
