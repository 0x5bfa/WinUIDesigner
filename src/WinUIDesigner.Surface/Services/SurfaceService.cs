// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Implements the surface-side Visual Studio designer protocol for WinUI documents.
/// Translates designer requests into live objects, visual-tree mutations, bounds, and property operations.
/// </summary>
internal sealed partial class SurfaceService : IDisposable
{
    private const long ApplicationLiveHandle = 1;

    private readonly ProtocolHandler _protocolHandler;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ObjectIdentityRegistry _objectIdentity;
    private readonly List<int> _registrationIds = [];

    public SurfaceService(ProtocolHandler protocolHandler, DispatcherQueue dispatcherQueue, ObjectIdentityRegistry objectIdentity)
    {
        _protocolHandler = protocolHandler;
        _dispatcherQueue = dispatcherQueue;
        _objectIdentity = objectIdentity;

        // Construction actions use live handle 1 for the surface application's
        // Application.Current instance, which is not created by an action.
        _objectIdentity.RegisterHandle(ApplicationLiveHandle, Application.Current);
        _visualTreeService = new(protocolHandler, objectIdentity);
        _resourceScopes = new ResourceScopeService(objectIdentity);
        _diagnosticsPropertySource = new DiagnosticsPropertySourceService();
        _serializer = new LiveValueSerializer(objectIdentity);

        // The matching request/response types come from the Visual Studio side.
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<CreateSurfaceRequestInfo, CreateSurfaceResponseInfo>((int)DesignerMessageId.CreateSurface, HandleCreateSurfaceAsync));
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<CloseDocumentRequestInfo, ResponseWithError>((int)DesignerMessageId.CloseDocument, HandleCloseDocumentAsync));
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetPanZoomTransformRequestInfo, ResponseWithError>((int)DesignerMessageId.SetPanZoomTransform, HandleSetPanZoomTransformAsync));
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetArtboardColorsRequestInfo, ResponseWithError>((int)DesignerMessageId.SetArtboardColors, HandleSetArtboardColorsAsync));
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetDeviceSizeRequestInfo, ResponseWithError>((int)DesignerMessageId.SetDeviceSize, HandleSetDeviceSizeAsync));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<SetFreezeStateInfo>((int)DesignerMessageId.SetFreezeState, HandleSetFreezeState));
        _registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetSurfacePositionRequestInfo, ResponseWithError>((int)DesignerMessageId.SetSurfacePosition, HandleSetSurfacePositionAsync));

        // Visual Studio's HitTestRequest uses System.Windows.Point/Rect even for the
        // platform-neutral wire protocol. Keep the WinUI surface independent of WPF
        // by deserializing the same DataContract JSON shape into local DTOs.
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<HitTestRequestContract, HitResponse>((int)DesignerMessageId.HitTest, HandleHitTest));

        _registrationIds.Add(protocolHandler.RegisterMessageObserver<GetSnapLinesRequest, GetSnapLinesResponse>((int)DesignerMessageId.GetSnapLines, HandleGetSnapLines));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<GetElementSnapDataRequest, GetElementSnapDataResponse>((int)DesignerMessageId.GetElementSnapData, HandleGetElementSnapData));

        _registrationIds.Add(protocolHandler.RegisterMessageObserver<ExecuteXamlActionsRequestInfo, ResponseWithError>((int)DesignerMessageId.ExecuteXamlActions, HandleExecuteActions));

        _registrationIds.Add(protocolHandler.RegisterMessageObserver<PropertiesRequestInfo, LiveObjectState>((int)DesignerMessageId.GetProperties, HandleGetProperties));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<DefaultValueRequestInfo, LiveValueResponse>((int)DesignerMessageId.GetDefaultValue, HandleGetDefaultValue));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<UnderlyingValueSourceRequest, UnderlyingValueSourceInformation>((int)DesignerMessageId.GetUnderlyingValue, HandleGetUnderlyingValue));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<ExecuteXamlActionsRequestInfo, UnderlyingValueSourceInformation>((int)DesignerMessageId.ExecuteLookupActions, HandleExecuteLookupActions));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<EvaluateStaticExtensionRequest, LiveValue>((int)DesignerMessageId.EvaluateStaticExtension, HandleEvaluateStaticExtension));

        _registrationIds.Add(protocolHandler.RegisterMessageObserver<GoToStateRequest, ResponseWithError>((int)DesignerMessageId.GoToState, HandleGoToState));
        _registrationIds.Add(protocolHandler.RegisterMessageObserver<StoryboardRequest, ResponseWithError>((int)DesignerMessageId.Storyboard, HandleStoryboardAction));

        _diagnosticsPropertySource.StartInitialization();
    }

    public void Dispose()
    {
        _actionObjects.Clear();
        _trackedActionObjects.Clear();

        foreach (int registrationId in _registrationIds)
        {
            _protocolHandler.UnregisterMessageObserver(registrationId);
        }

        foreach (DesignerSurface surface in _surfaces.Values)
        {
            surface.Dispose();
        }

        _surfaces.Clear();

        foreach (var dictionary in _resourceDocuments.Values)
        {
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }

        _resourceDocuments.Clear();
        _documentResources.Clear();
        _sharedResourceDocuments.Clear();

        if (_xamlControlsResources is not null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_xamlControlsResources);
            _xamlControlsResources = null;
        }

        _applicationResourceDocumentId = null;
        _designTimeResourceDocumentId = null;
        _hasXamlControlsResources = false;
        _appRequestedTheme = ElementTheme.Default;

        _publishedSurfaceBounds.Clear();
        _visualTreeService.Clear();
        _objectIdentity.Clear();
    }
}
