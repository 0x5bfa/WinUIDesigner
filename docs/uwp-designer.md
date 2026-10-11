# UWP XAML Designer

This page follows the UWP application and display paths behind the [shared designer flow](architecture.md). The editor, document model, and editing tools run in Visual Studio. A separate designer application holds and renders the live `Windows.UI.Xaml` objects.

- [Starting the application and creating a document](#starting-the-application-and-creating-a-document)
- [Embedding the surface](#embedding-the-surface)
- [Error handling and lifetime management](#error-handling-and-lifetime-management)

> [!NOTE]
> Unless otherwise noted, shortened assembly names such as `DesignerHost.dll` refer to the full file name `Microsoft.VisualStudio.DesignTools.DesignerHost.dll`.

## Starting the application and creating a document

When a live preview is needed, the host stages a designer application and the project's content, establishes its package identity, and registers the package for the current user. The manifest, resource index, and access permissions must be ready before activation.

The host then creates an application view, transfers valid communication handles, and loads the diagnostics TAP. Once communication is ready, the editor associates the document with an application view and sends its construction request. The shared protocol and pipeline steps are described in the [main document](architecture.md#communicating-with-the-surface).

```mermaid
sequenceDiagram
    participant Host as UwpHostPlatform<br/>/ shadow-copy and package services
    participant Process as UwpSurfaceProcess
    participant Manager as DesignerAppManager<br/>(Windows.UI.Xaml.Designer)
    participant Document as UwpSurfaceProcessMarkupProvider<br/>/ UwpDesignerInstanceManager
    participant App as Designer UWP application<br/>/ TAP
    Note over Host,Document: Host-side objects run inside Visual Studio
    Host->>Host: CopySurfaceProcessPayload<br/>/ CopyProjectContentAsync
    Host->>Host: EnsurePackageName<br/>/ FixupResourcePriFileAsync
    Host->>Host: EnsurePackageRegisteredAsync<br/>RegisterPackageTimed when needed
    Host->>Process: ActivateSurface<br/>construct and initialize the process wrapper
    Process->>Manager: CreateNewViewAsync(Visible, Size(0, 0))
    Manager->>App: Activate the application and create a view
    Manager-->>Process: Initial DesignerAppView
    Process->>Process: initialView.TrySetResult<br/>/ CreateInitializationData
    Note over Process,Manager: DuplicateHandleIntoApp transfers<br/>pipe and VS process handles
    Process->>Manager: LoadObjectIntoAppAsync<br/>UwpTap.dll and initialization data
    Manager->>App: Load and initialize the TAP
    Process->>Process: WaitForTapToLoad
    App-->>Process: First protocol response over pipes
    Note over Process,Document: Continue through the shared local pipeline
    Document->>Document: PrepareApplicationDocumentsForLoading<br/>/ RegisterDocument / CreateRequestAsync
    Document->>Process: CreateViewAsync
    opt Initial view already used
        Process->>Manager: Through SafeCreateViewAsync:<br/>CreateNewViewAsync
        Manager-->>Process: Additional DesignerAppView
    end
    Process-->>Document: View associated with this document
    Document->>Document: Set document.Surface<br/>/ validate ApplicationViewId
    Document->>App: Through the shared pipeline:<br/>UwpCreateSurfaceRequestInfo
    App-->>Document: Document construction result
    Note over Document,App: Connect the completed document's view<br/>to the display in the next stage
```

The initial view becomes available before TAP initialization finishes, and can be used by the first document. Later requests can obtain additional views. The view identity selects the UWP display context; document and object identities connect editing operations to its runtime content.

Application resources can come from compiled App XBF or prepared application XAML. A previous loading or construction failure can cause the next request to include construction actions and prepared application XAML. After construction completes, the image host connects the selected view to the artboard.

## Embedding the surface

The UWP runtime supplies a shared composition visual for the document's application view. Visual Studio creates a display child window, opens the shared visual using its composition device, and attaches it to a local DirectComposition target.

The artboard and editing overlays remain in Visual Studio. The remote application supplies the rendered XAML content, while the local windows provide its display location and the layers used by the editing tools.

```mermaid
sequenceDiagram
    participant Display as UwpIsolatedImageHost<br/>/ UwpHwndHost / UwpVisualWrapper
    participant View as DesignerAppView<br/>/ IDesignerAppViewNative
    participant Composition as DirectComposition
    participant Runtime as UWP XAML visual producer
    Note over View,Runtime: OS shared-visual setup:<br/>intermediate internal calls are omitted
    Runtime->>Composition: Through CreateAndSendSharedVisual:<br/>DCompositionCreateSharedVisualHandle
    Composition-->>Runtime: Shared visual handle
    Runtime->>View: Through IDesignerSurfaceReceiveHandle:<br/>SetDesignerSharedVisualHandle
    Note over Display,View: Attach the completed document's view
    Display->>Display: CreateHwndHost
    Display->>Composition: Through CompositionDevice:<br/>D3D11CreateDevice / DCompositionCreateDevice
    Display->>Display: BuildWindowCore / InitializeSurfaceHwnd<br/>create the display child window
    Display->>Composition: CreateTargetForHwnd(display HWND, true)
    Display->>View: GetVisual(device)
    opt Visual has not already been opened
        View->>Composition: QueryInterface for<br/>IDCompositionDesktopDevicePartner
        View->>Composition: OpenSharedResource<br/>with the handle and IDCompositionVisual IID
        Composition-->>View: Opened composition visual
    end
    View-->>Display: IDCompositionVisual
    Display->>Composition: CreateVisual / AddVisual<br/>/ SetRoot / Commit
    Note over Display: Shared host supplies editing overlays
    Display->>Display: ResizeSurfaceHwnd / SetWindowPos
    Display->>View: On WM_SIZE:<br/>UpdateViewAsync(Visible, size)
```

The visual is shared through an OS handle, and an already-opened visual can be reused. This connects the remote rendering to Visual Studio's display target without moving the application's display window into the editor.

Resizing the local display window updates the application view's viewport. Overlapping resize work is suppressed, and the final window size is checked again after completion. Preview device size, orientation, pan, and zoom remain separate from this viewport synchronization.

## Error handling and lifetime management

UWP-specific failures can occur while checking the target runtime, preparing or registering the package, creating an application view, loading the TAP, or associating a document with a view. Application-exit notifications update the editor's process state and cancel unfinished view creation.

Display teardown, connection disposal, application shutdown, and package cleanup have separate lifetimes. Closing a display releases its local windows; disposing the connection releases its composition devices; ending application management requests application shutdown.

```mermaid
sequenceDiagram
    participant Owner as VS view / project lifetime
    participant Display as UwpHwndHost<br/>/ UwpVisualWrapper
    participant Context as UwpSurfaceProcessContext
    participant Process as UwpSurfaceProcess
    participant Manager as DesignerAppManager
    participant Host as UwpHostPlatform<br/>/ AppPackageHelper
    Note over Owner,Host: Display, connection, application, and package<br/>have separate lifetimes
    opt Display teardown
        Owner->>Display: DestroyWindowCore
        Display->>Display: Dispose visual wrapper
        Display->>Display: Base DestroyWindowCore<br/>releases overlays and holder HWND
    end
    opt Process-context disposal
        Owner->>Context: Dispose
        Context->>Context: Marshal.ReleaseComObject<br/>D3D11 and composition devices
        Context->>Context: Base Dispose<br/>releases connection state
    end
    opt Application disposal
        Owner->>Process: Dispose / StartDispose
        Process->>Process: Remove log and exit-event handlers<br/>UpdateStatus(Killed)
        Process->>Manager: Dispose asynchronously
        opt Caller waits for exit
            Owner->>Process: WaitForExit
            opt Disposal times out and the process remains
                Process->>Process: Kill / WaitForExit
            end
        end
    end
    opt Shadow-cache cleanup
        Owner->>Host: CleanShadowCache
        Host->>Host: UnregisterPackage
        Host->>Host: Base CleanShadowCache
    end
```

Application disposal is asynchronous. A caller waiting for exit can attempt to terminate a process that remains after the timeout. Package removal belongs to cache replacement and cleanup, including cleanup of older registrations during idle work.

Closing a document therefore does not necessarily remove its package registration. Which views and processes remain also depends on the shared project and document ownership rules.
