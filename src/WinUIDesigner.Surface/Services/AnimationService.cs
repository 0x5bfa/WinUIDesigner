// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies visual-state and storyboard operations to live WinUI objects.
/// Implements the shared Designer's VisualState and Storyboard messages directly
/// against WinUI runtime objects in the surface process.
/// </summary>
internal sealed partial class AnimationService : IDisposable
{
    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly int visualStateRegistrationId;
    private readonly int storyboardRegistrationId;
    private Storyboard? activeStoryboard;

    public AnimationService(
        ProtocolHandler protocolHandler,
        DispatcherQueue dispatcherQueue,
        ObjectIdentityRegistry objectIdentity)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;
        visualStateRegistrationId = protocolHandler.RegisterMessageObserver<GoToStateRequest, ResponseWithError>(524, HandleGoToState);
        storyboardRegistrationId = protocolHandler.RegisterMessageObserver<StoryboardRequest, ResponseWithError>(525, HandleStoryboardAction);
    }

    private ResponseWithError HandleGoToState(GoToStateRequest request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleGoToState(request));
        }

        if (!objectIdentity.TryGetObject(request.RootHandle, out object? rootObject) || rootObject is not FrameworkElement root)
        {
            return Failure($"Unable to find VisualState root handle {request.RootHandle}.");
        }

        bool changed = false;
        foreach (string stateName in request.StateNames ?? [])
        {
            changed |= TryGoToState(root, stateName, request.UseTransitions);
        }

        WinUIDesignerLogger.LogTrace("Surface", $"GoToState (524): root={request.RootHandle}, states={request.StateNames?.Count ?? 0}, changed={changed}.");
        return Success;
    }

    private static bool TryGoToState(FrameworkElement root, string stateName, bool useTransitions)
    {
        // States belong to their templated Control, not necessarily the supplied
        // root, so search the realized subtree until a control accepts the state.
        if (root is Control control && VisualStateManager.GoToState(control, stateName, useTransitions))
        {
            return true;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is FrameworkElement child && TryGoToState(child, stateName, useTransitions))
            {
                return true;
            }
        }

        return false;
    }

    private ResponseWithError HandleStoryboardAction(StoryboardRequest request)
    {
        if (!dispatcherQueue.HasThreadAccess)
        {
            return InvokeOnDispatcher(() => HandleStoryboardAction(request));
        }

        if (!objectIdentity.TryGetObject(request.ObjectHandle, out object? storyboardObject) || storyboardObject is not Storyboard storyboard)
        {
            return Failure($"Unable to find Storyboard handle {request.ObjectHandle}.");
        }

        try
        {
            foreach (int action in request.StoryboardActions ?? [])
            {
                switch (action)
                {
                    case 0:
                        storyboard.Begin();
                        break;
                    case 1:
                        storyboard.Pause();
                        break;
                    case 2:
                        storyboard.Resume();
                        break;
                    case 3:
                        storyboard.Stop();
                        break;
                    case 4:
                        storyboard.Seek(TimeSpan.FromSeconds(request.SeekTime));
                        if (request.IsPaused)
                        {
                            storyboard.Pause();
                        }
                        break;
                    case 5:
                        activeStoryboard = storyboard;
                        break;
                }
            }

            WinUIDesignerLogger.LogTrace("Surface", $"Storyboard (525): handle={request.ObjectHandle}, actions={request.StoryboardActions?.Count ?? 0}.");

            return Success;
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Surface", $"Storyboard (525) failed: {ex}");

            return new ResponseWithError { HResult = ex.HResult == 0 ? -2147467259 : ex.HResult, Error = ex.ToString() };
        }
    }

    private ResponseWithError InvokeOnDispatcher(Func<ResponseWithError> callback)
    {
        try
        {
            return DispatcherOperation.Invoke(dispatcherQueue, callback, protocolHandler.CancellationToken);
        }
        catch (Exception ex)
        {
            return new ResponseWithError { HResult = ex.HResult, Error = ex.ToString() };
        }
    }

    private static ResponseWithError Success => new() { HResult = 0 };

    private static ResponseWithError Failure(string message)
    {
        return new() { HResult = -2147467259, Error = message };
    }

    public void Dispose()
    {
        protocolHandler.UnregisterMessageObserver(visualStateRegistrationId);
        protocolHandler.UnregisterMessageObserver(storyboardRegistrationId);
    }
}
