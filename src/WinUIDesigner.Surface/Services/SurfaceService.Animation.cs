// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    private Storyboard? _activeStoryboard;

    private ResponseWithError HandleGoToState(GoToStateRequest request)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => HandleGoToState(request), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new ResponseWithError
                {
                    HResult = ex.HResult,
                    Error = ex.ToString()
                };
            }
        }

        if (!_objectIdentity.TryGetObject(request.RootHandle, out object? rootObject) || rootObject is not FrameworkElement root)
        {
            return new ResponseWithError
            {
                HResult = -2147467259, // E_FAIL
                Error = $"Unable to find VisualState root handle {request.RootHandle}."
            };
        }

        foreach (string stateName in request.StateNames ?? [])
        {
            TryGoToState(root, stateName, request.UseTransitions);
        }

        return new ResponseWithError()
        {
            HResult = 0 // S_OK
        };

        static bool TryGoToState(FrameworkElement root, string stateName, bool useTransitions)
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
    }

    private ResponseWithError HandleStoryboardAction(StoryboardRequest request)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => HandleStoryboardAction(request), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new ResponseWithError { HResult = ex.HResult, Error = ex.ToString() };
            }
        }

        if (!_objectIdentity.TryGetObject(request.ObjectHandle, out object? storyboardObject) || storyboardObject is not Storyboard storyboard)
        {
            return new ResponseWithError
            {
                HResult = -2147467259, // E_FAIL
                Error = $"Unable to find Storyboard handle {request.ObjectHandle}."
            };
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
                        _activeStoryboard = storyboard;
                        break;
                }
            }


            return new ResponseWithError()
            {
                HResult = 0 // S_OK
            };
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", $"Storyboard ({(int)DesignerMessageId.Storyboard}) failed.", ex);

            return new ResponseWithError
            {
                HResult = ex.HResult == 0 ? -2147467259 : ex.HResult, // E_FAIL
                Error = ex.ToString()
            };
        }
    }
}
