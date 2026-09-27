using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

internal sealed class XamlActionService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly SurfaceService surfaceService;
    private readonly Dictionary<long, object?> actionObjects = new();
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
        registrationId = protocolHandler.RegisterMessageObserver<ExecuteXamlActionsRequestInfo, ResponseWithError>(507, HandleExecuteActions);
    }

    private ResponseWithError HandleExecuteActions(ExecuteXamlActionsRequestInfo request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleExecuteActions(request));
        }

        try
        {
            IList<XamlAction> actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
            for (int index = 0; index < actions.Count; index++)
            {
                if (request.Actions is { } serializedActions && index < serializedActions.Count)
                {
                    Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) action[{index}]: {serializedActions[index]}");
                }

                Execute(actions[index]);
            }

            surfaceService.CompleteActionBatch();
            Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) completed: count={actions.Count}.");
            return Success;
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"ExecuteXamlActions (507) failed: {ex}");
            return Failure(ex);
        }
    }

    public LiveValue ExecuteLookupActions(ExecuteXamlActionsRequestInfo request, LiveValueSerializer serializer)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            LiveValue? value = null;
            using var completion = new System.Threading.ManualResetEventSlim();
            if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    value = ExecuteLookupActions(request, serializer);
                }
                finally
                {
                    completion.Set();
                }
            }))
            {
                return new LiveValue();
            }

            completion.Wait();
            return value ?? new LiveValue();
        }

        object? captured = null;
        IList<XamlAction> actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
        foreach (XamlAction action in actions)
        {
            Execute(action, (_, value) => captured = value);
        }

        return serializer.Serialize(captured);
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
                XamlRuntimeUtilities.SetPropertyValue(RequireObject(setProperty.ParentHandle), setProperty.FullPropertyName, ResolveObject(setProperty.PropertyValueHandle));
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
                XamlRuntimeUtilities.AddChild(RequireObject(addChild.ParentHandle), addChild.Index, ResolveObject(addChild.ChildHandle));
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
                XamlRuntimeUtilities.SetDictionaryEntry(
                    RequireObject(addDictionaryEntry.DictionaryHandle),
                    ResolveObject(addDictionaryEntry.KeyHandle),
                    ResolveObject(addDictionaryEntry.ValueHandle));
                break;

            case RemoveDictionaryEntryAction removeDictionaryEntry:
                XamlRuntimeUtilities.RemoveDictionaryEntry(
                    RequireObject(removeDictionaryEntry.DictionaryHandle),
                    ResolveObject(removeDictionaryEntry.KeyHandle));
                break;

            case UpdateDictionaryValueAction updateDictionaryValue:
                XamlRuntimeUtilities.SetDictionaryEntry(
                    RequireObject(updateDictionaryValue.DictionaryHandle),
                    ResolveObject(updateDictionaryValue.KeyHandle),
                    ResolveObject(updateDictionaryValue.ValueHandle));
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

            case UpdateResourceMutationsAction:
            case SetResourcesSearchParentAction:
            case SetThemeResourcesEditingScopeAction:
                // These actions describe lookup scope and mutation attribution. The WinUI
                // ResourceDictionary itself is already mutated by the actions above.
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
        if (!string.IsNullOrWhiteSpace(action.FactoryMethod))
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
        object instance = XamlRuntimeUtilities.ParseXaml(action.Xaml)
            ?? throw new InvalidOperationException("WinUI XamlReader returned null.");
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
            return value;
        }

        if (objectIdentity.TryGetObject(handle, out value))
        {
            return value;
        }

        return null;
    }

    private object RequireObject(long handle)
        => ResolveObject(handle) ?? throw new KeyNotFoundException($"No live object is registered for handle {handle}.");

    private ResponseWithError InvokeOnDispatcher(Func<ResponseWithError> callback)
    {
        ResponseWithError? response = null;
        using var completion = new System.Threading.ManualResetEventSlim();
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                response = callback();
            }
            finally
            {
                completion.Set();
            }
        }))
        {
            return Failure(new InvalidOperationException("Failed to enqueue XAML actions on the WinUI DispatcherQueue."));
        }

        completion.Wait();
        return response ?? Failure(new InvalidOperationException("XAML action execution did not return a response."));
    }

    private static ResponseWithError Success => new() { HResult = 0 };

    private static ResponseWithError Failure(Exception exception)
        => new()
        {
            HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
            Error = exception.ToString(),
        };

    public void Dispose() => protocolHandler.UnregisterMessageObserver(registrationId);
}
