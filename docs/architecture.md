# Architecture

- [Platform registration](#platform-registration)
- [Opening a XAML file](#opening-a-xaml-file)
- [Creating the designer view](#creating-the-designer-view)
- [Connecting the project and host platforms](#connecting-the-project-and-host-platforms)
- [Starting the isolated process](#starting-the-isolated-process)
- [Communicating with the surface](#communicating-with-the-surface)
- [Loading the XAML document](#loading-the-xaml-document)
- [Embedding the surface](#embedding-the-surface)
- [Handling designer operations](#handling-designer-operations)
- [Synchronizing XAML and design changes](#synchronizing-xaml-and-design-changes)
- [Toolbox integration](#toolbox-integration)
- [Error handling and lifetime management](#error-handling-and-lifetime-management)

> [!NOTE]
> Unless otherwise noted, shortened assembly names such as `DesignerHost.dll` refer to the full file name `Microsoft.VisualStudio.DesignTools.DesignerHost.dll`.

## Platform registration

Platform configuration supplies the implementation details used to create the designer backend. It is separate from the runtime-to-editor mapping that enables the design tab.

`PlatformConfigurationService` in `DesignerHost.dll` registers configurations for these platforms and targets:

- WPF on .NET Framework
- WPF on .NET Core 3.0 or later
- UWP (`UAP` 0.8 or later)
- Legacy `WindowsXaml80`
- WinUI for desktop .NET (`.NETCoreApp` 5.0 or later)
- WinUI for UAP

These are configuration matching conditions, not a statement of current product support.

For example, the configurations of .NET Framework WPF and WinUI for desktop .NET are registered as follows:

```csharp
RegisterPlatformConfiguration(WpfSpecification, new Dictionary<string, string>
{
    { "PlatformCreatorAssembly", "Microsoft.VisualStudio.DesignTools.WpfSurfaceDesigner" },
    { "PlatformCreatorType", "Microsoft.VisualStudio.DesignTools.WpfSurfaceDesigner.WpfFrameworkPlatformCreator" },
    { "HostPlatformAssembly", "Microsoft.VisualStudio.DesignTools.XamlDesignerHost.dll" },
    { "HostPlatformType", "Microsoft.VisualStudio.DesignTools.WpfDesignerHost.WpfHostPlatform" },
    { "ReferenceAssemblyMode", "TargetFramework" },
    { "PlatformSurfaceIsolatedGuid", "{ABC03A4E-B0A1-41B7-97BA-BB178F354CCD}" },
    { "SupportsToolboxAutoPopulation", "true" },
    { "ToolboxPage", "{7C9BECBA-12B9-4480-9DBF-2345AA3C4F9A}" },
    { "DesignerTechnology", "Microsoft:System.Windows:WPF" },
    { "ClipboardFormat", "CF_WINFX_TOOL" },
    { "ProjectFlavor", "{60dc8134-eba5-43b8-bcc9-bb4bc16c2548}" }
});
```

```c#
RegisterPlatformConfiguration(winuiSpecification, new Dictionary<string, string>
{
    { "DefaultTargetFramework", ".NETCoreApp, Version=5.0" },
    { "SupportsToolboxAutoPopulation", "true" },
    { "SupportsExtensionSdks", "false" },
    { "ToolboxPage", "{8A63BDE2-AEB9-4AF9-A00D-DBC9BD7D509C}" },
    { "DesignerTechnology", "Microsoft:Microsoft.UI.Xaml" },
    { "ClipboardFormat", "CF_WINDOWSUIXAML_TOOL" },
    { "AppPackageType", "WindowsXaml" }
});
```

Each configuration associates a target specification with properties describing the designer's editing platform, process host, metadata, and Toolbox behavior. The assembly and type entries identify two implementations: an editing-platform creator and a process host. They are instantiated when requested and cached independently.

The editing service copies the configuration properties when it is constructed. Changes to the configuration table therefore do not automatically update an editing service that already exists. The host service reads the matched configuration when a host is requested and reuses an existing host for the same platform identity.

The built-in WinUI configurations omit both assembly/type pairs. They contain other properties used by designer tools, but do not supply an editing platform or process host. The runtime mapping can still enable a design tab; this project's [platform registration](../src/WinUIDesigner.Vsix/WinUIDesignerPackage.cs) also supplies the missing implementation bindings.

The extension updates the existing desktop WinUI configuration and installs a creator hook that returns its WinUI creator, cached per editing service. This also covers services whose configuration snapshot predates the update. Calls for other XAML runtimes continue through Visual Studio's original implementation. Both the configuration changes and hook follow the extension package's lifetime.

```mermaid
sequenceDiagram
    participant Manager as ProjectContextManager
    participant Config as PlatformConfigurationService
    participant Platforms as PlatformService
    participant Creator as IPlatformCreator
    participant Hosts as HostPlatformService
    participant Host as IHostPlatform
    Config->>Config: Register target specifications and properties
    Platforms->>Hosts: GetConfigurations at service construction
    Hosts->>Config: GetConfigurations
    Config-->>Hosts: Configuration entries
    Hosts-->>Platforms: Configuration entries
    Platforms->>Platforms: Copy properties into the editing service
    Note over Manager,Host: Later, resolve a project's implementations<br/>using its PlatformIdentifier
    Manager->>Platforms: GetPlatformCreator(platformId)
    Platforms->>Creator: Create or reuse the creator<br/>using its assembly/type binding
    Note over Platforms,Creator: For WinUI, the extension supplies the creator<br/>through its runtime hook
    Creator-->>Platforms: Editing-platform creator
    Platforms-->>Manager: IPlatformCreator
    Manager->>Hosts: GetPlatform(platformId)
    Hosts->>Config: GetProperty for the matched<br/>host assembly/type binding
    Config-->>Hosts: Host binding
    Hosts->>Host: Create or reuse the process host
    Host-->>Hosts: Process host
    Hosts-->>Manager: IHostPlatform
    Note over Manager: Project-context creation continues in<br/>Connecting the project and host platforms
```

## Opening a XAML file

When you open Visual Studio’s **Open With** dialog for a XAML file, it lists two designer-related entries: **XAML Designer** and **XAML Designer with Encoding**. The second entry selects the encoding-aware editor factory.

![image1](image.png)

These entries are registered by `MS.Internal.Package.XamlDesignerPackage` in `XamlDesignerHost.dll` as `TabbedViewEditorFactory` and `TabbedViewCodePageEditorFactory`, respectively. Both implement the Visual Studio SDK interface `IVsEditorFactory`. The package also registers Toolbox providers, which we’ll cover later.

After you select an entry, the shell asks its editor factory to open the document. The factory reuses the document's existing text buffer or creates one, then builds a view around it. The project, target platform, and editor settings determine whether that view includes the designer. Source and design views share the same buffer.

WPF and UWP use the normal designer eligibility path. WinUI starts with the design view disabled, but a runtime-specific editor mapping under `XamlDesigner\XamlRuntimes\WinUI` can enable it. The mapping must contain a valid editor-factory GUID, and the built-in designer-tab factory must be registered. Document-specific exclusions can still disable the designer. This project's [runtime registration attribute](../src/WinUIDesigner.Vsix/ProvideXamlRuntimeDesignerAttribute.cs) adds the WinUI mapping.

## Creating the designer view

The returned outer pane is a container for the design and source views. The shell first attaches it to a window frame; later display notifications cause it to populate the tabs. Each tab records which editor factory should supply its contents. Creating the container and loading the designer's contents are separate stages.

When the design tab is displayed, it asks the shell to open an inner editor using the selected GUID. The built-in designer-tab factory supplies a `DesignerPane`, which hosts the designer's UI and checks whether the project is supported.

After the compatibility checks pass, the inner pane obtains a `HostDesignerView` and places its UI element in the tab. This wrapper records the source item and text editor. The underlying designer is loaded when the view is needed, including when it becomes visible. The host integration service then obtains the shared designer implementation from `SurfaceDesigner.dll` and asks it to open the document.

Before returning the scene view, `DesignerService` resolves the project's editing and host platforms, opens the XAML document, and connects the view to the text editor. Project-context resolution is expanded in [Connecting the project and host platforms](#connecting-the-project-and-host-platforms).

```mermaid
sequenceDiagram
    participant Shell as Visual Studio shell
    participant Editor as XAML editor factory
    participant Tabs as XamlTabbedEditorPane
    participant Designer as DesignerTabEditorFactory<br/>/ DesignerPane
    participant Host as HostDesignerService<br/>/ HostDesignerView
    participant Shared as DesignerService
    Note over Shell,Shared: All participants run inside Visual Studio
    Shell->>Editor: IVsEditorFactory.<br/>CreateEditorInstance
    Editor->>Editor: Reuse or create<br/>the shared text buffer
    Editor->>Editor: DoCreateView<br/>selects the runtime's designer editor
    Note over Editor: Check project eligibility, settings,<br/>and runtime-to-editor GUID mapping
    alt Design view enabled
        Editor->>Tabs: Create outer pane<br/>with design and source tabs
        Editor-->>Shell: Outer pane and shared text buffer
        Shell->>Tabs: Attach window frame and show pane
        Tabs->>Shell: Open design tab's inner editor by GUID
        Shell->>Designer: CreateEditorInstance
        Designer->>Designer: CreateView supplies DesignerPane
        Shell-->>Tabs: Inner editor frame
        Tabs->>Designer: Initialize the supported designer
        Designer->>Host: CreateHostDesignerView<br/>(source item, text editor)
        Host-->>Designer: Wrapper UI element
        Note over Host: Load the underlying view<br/>when needed
        Host->>Shared: CreateDesigner<br/>(source item, text editor)
        Shared->>Shared: Resolve the project context
        Note over Shared: See Connecting the project<br/>and host platforms
        Shared->>Shared: Open the document<br/>and create its scene view
        Shared-->>Host: SceneView
        Host->>Host: Display the returned view<br/>in the wrapper
    else Design view disabled
        Editor-->>Shell: Source-only view<br/>using the shared text buffer
    end
```

## Connecting the project and host platforms

`DesignerService` asks the project context manager to resolve the source file's editing context and its project context. An existing project context is reused. For a new context, the project's target framework, SDK, and reference assemblies determine the editing platform to create or reuse. That platform supplies the project context, and the manager attaches the corresponding process host to it.

The context retains both references: the editing platform supplies document and scene services, while the host prepares and manages the display process. For UWP, these objects are `UwpProjectContext`, `UwpPlatform`, and `UwpHostPlatform`. WPF uses `WpfProjectContext`, `WpfPlatform`, and `WpfHostPlatform` through the same shared sequence.

Once the context is available, the shared XAML buffer is opened as an editing document and parsed into its XAML model. The editing platform supplies a scene view for that document. The view is connected to the text editor and returned to the wrapper that the design tab already hosts.

```mermaid
sequenceDiagram
    participant Shared as DesignerService
    participant Documents as DocumentViewContext
    participant Manager as ProjectContextManager
    participant Creator as IPlatformCreator
    participant Platform as IPlatform
    participant Hosts as HostPlatformService
    participant Context as ProjectContextBase
    Shared->>Manager: GetSourceItemContext(item)<br/>GetProjectContext(project)
    opt No existing project context
        Manager->>Creator: CreatePlatform(project references)
        Creator-->>Manager: Editing platform
        Manager->>Platform: Initialize(designerContext)
        Manager->>Platform: CreateProjectContext()
        Platform->>Context: Create context with<br/>a reference to this platform
        Platform-->>Manager: Project context
        Manager->>Hosts: GetPlatform(platformId)
        Hosts-->>Manager: Process host
        Manager->>Context: Initialize(project, host)
        Note over Context: Retain editing-platform<br/>and process-host references
    end
    Manager-->>Shared: Project context
    Shared->>Documents: OpenDocument(source item)
    Documents-->>Shared: SceneDocument
    Shared->>Shared: EnsureParsed<br/>parse the shared XAML buffer
    Shared->>Documents: OpenView(source item)
    opt No existing scene view
        Documents->>Platform: Through SceneDocument:<br/>CreateSceneView(document)
        Platform-->>Documents: SceneView
        Note over Documents: Initialize the new view
    end
    Documents-->>Shared: SceneView
    Shared->>Shared: Connect the view<br/>to the text editor
    Note over Shared: Return the view to the host wrapper
```

## Starting the isolated process

The scene view belongs to Visual Studio, but the live XAML objects belong to an isolated surface process. This keeps the project's runtime and control instances outside the editor process. When a live display connection is needed, the project context coordinates process startup and the designer's local pipeline.

When the returned scene view is attached to the host wrapper, deferred initialization and artboard loading start asynchronous surface loading through the view's image host. The image host uses the project's process context to ensure the isolated process and local pipeline are available before constructing the document. Surface loading can finish after the scene view has been returned.

The host selects the target architecture and runtime, then prepares a shadow cache containing the surface executable, project assemblies, resources, and diagnostics files. The editing platform supplies the markup provider and instance-building components. These components remain inside Visual Studio and connect its editing model to the remote objects.

The launch path depends on the platform:

| Platform | Process preparation and activation |
| --- | --- |
| WPF | Stage the desktop surface and launch it with pipe initialization data. |
| UWP | Prepare and register a designer app package, activate its app view, and initialize its TAP. |
| This WinUI extension | Reuse the WPF .NET host and shadow-copy worker, then launch the extension's WinUI surface. Reuse UWP editing services with WinUI-specific document and display adapters. |

```mermaid
sequenceDiagram
    participant Wrapper as HostDesignerView
    participant View as SceneView<br/>/ XamlSceneView
    participant Image as IsolatedSurfaceImageHost
    participant Connection as SurfaceProcessContext
    participant Host as IHostPlatform
    participant Surface as Isolated surface process
    Note over Wrapper,View: Continue with the returned SceneView
    Wrapper->>View: LoadSurfaceView:<br/>attach Element and call UIAttached
    Note over View: Deferred UI work and artboard loading<br/>activate the view
    View->>View: RefreshIsolatedSurfaceView<br/>starts LoadSurfaceAsync
    View->>Image: CreateSurfaceAsync
    Image->>Connection: Through Project.SurfaceProcessContext:<br/>EnsureSurfaceProcessAsync
    opt Surface process needs initialization
        Connection->>Host: Through the project context:<br/>prepare and activate the surface
        Host->>Surface: Launch the platform's surface
        Host-->>Connection: Process connection
        Connection->>Connection: Initialize protocol and local pipeline<br/>using editing-platform factories
    end
    Connection-->>Image: Process connection<br/>and pipeline available
    Note over Image: Continue with document construction<br/>and display connection
```

For this extension, the project architecture selects `WinUISurface.x64.exe` or `WinUISurface.arm64.exe`; the selected executable is staged as `WinUISurface.exe`. Project media and library PRI files are also staged for resource lookup. Startup passes the Visual Studio process ID, diagnostics TAP path, pipe initialization data, and DPI context. The surface resolves private designer contracts from the Visual Studio installation that launched it, initializes the WinUI application and dispatcher, registers its services, and starts receiving messages.

Implementation: [scene-view activation](../src/WinUIDesigner.Vsix/Platform/WinUISceneView.cs), [project context](../src/WinUIDesigner.Vsix/Platform/WinUIProjectContext.cs), [process host and staging](../src/WinUIDesigner.Vsix/DesignerHost), [local process context](../src/WinUIDesigner.Vsix/Platform/WinUISurfaceProcessContext.cs), and [surface startup](../src/WinUIDesigner.Surface/Program.cs).

## Communicating with the surface

Two anonymous pipes carry traffic in opposite directions. Their handles must be duplicated or inherited into the receiving process; passing a numeric handle as text alone does not establish a connection. Each side has a protocol handler that sends requests, matches replies to pending requests, and receives notifications.

The wire format is shared with Visual Studio's designer services:

| Part | Format |
| --- | --- |
| Header | Four 32-bit integers: remaining byte length, message type, message ID, and request ID. |
| Request/reply correlation | A reply identifies the original request; common replies use message type `1`. |
| Payload | Data-contract JSON encoded as UTF-16LE, or a contract-specific binary format. |
| Runtime identities | Serialized document IDs and object handles, resolved by the receiving service. |

```mermaid
sequenceDiagram
    participant Image as IsolatedSurfaceImageHost
    participant Pipeline as InstanceBuilderPipeline<br/>/ DesignerInstanceManager
    participant HostProtocol as ProtocolHandler<br/>(Visual Studio)
    participant SurfaceProtocol as ProtocolHandler<br/>(surface process)
    participant UI as Surface services<br/>/ WinUI dispatcher
    Note over Image,Pipeline: Continue with the process connection<br/>and pipeline available
    Image->>Pipeline: CreateSurfaceAsync(surface document)
    Pipeline->>HostProtocol: SendMessageAsync<br/>(516 CreateSurface)
    HostProtocol->>SurfaceProtocol: Framed request over pipes<br/>with document/object identities
    SurfaceProtocol->>UI: Dispatch runtime operation
    UI-->>SurfaceProtocol: Result or construction error
    SurfaceProtocol-->>HostProtocol: Framed reply over pipes
    HostProtocol-->>Pipeline: Match request ID<br/>and complete the pending request
    Pipeline-->>Image: Document construction result
    UI->>SurfaceProtocol: Tree or layout change
    SurfaceProtocol->>HostProtocol: Notification over pipes
    HostProtocol->>Pipeline: Update the live model
```

The surface uses the existing private protocol types and adds a pipe bridge compatible with the host's initialization data. Its frame reader assembles complete messages even when reads return partial data. WinUI operations are marshalled to the UI dispatcher; the receive thread does not directly mutate XAML objects. Service observers are registered before protocol startup so an early request can be handled.

Implementation: [pipe bridge](../src/WinUIDesigner.Surface/SurfacePipeDataBridge.cs), [frame reader](../src/WinUIDesigner.Surface/MessageFrameReader.cs), and [service initialization](../src/WinUIDesigner.Surface/App.xaml.cs).

## Loading the XAML document

Visual Studio prepares a design-time representation of the editing document. It can send XAML to load or construction actions describing objects, properties, and collection contents. Application resources and design-time resources are separate documents included with the edited document.

The surface first prepares those resources, then constructs the edited document on its UI thread. When XAML loading fails and actions were not supplied, it asks Visual Studio for construction actions. This alternative also lets the designer build objects individually and associate failures with the editing model.

```mermaid
sequenceDiagram
    participant Pipeline as InstanceBuilderPipeline<br/>/ DesignerInstanceManager
    participant Protocol as ProtocolHandler<br/>(surface process)
    participant Service as SurfaceService
    participant Runtime as WinUI runtime<br/>/ XAML action service
    Note over Protocol,Service: Expand the 516 CreateSurface request<br/>from the previous diagram
    Pipeline->>Protocol: 516 CreateSurface over pipes<br/>document and resource documents
    Protocol->>Service: HandleCreateSurfaceAsync
    opt Resource documents supplied
        Service->>Runtime: Build application and design-time resources<br/>on the UI dispatcher
        Runtime-->>Service: Resource dictionaries
    end
    alt Construction actions supplied
        Service->>Runtime: Execute construction actions
    else Prepared XAML supplied
        Service->>Runtime: XamlReader.Load on the UI dispatcher
        opt XAML loading fails
            Runtime-->>Service: Parse failure
            Service->>Pipeline: 534 Request construction actions<br/>over the same protocol connection
            Pipeline-->>Service: Construction actions
            Service->>Runtime: Execute construction actions
        end
    end
    Runtime-->>Service: Document root
    Service->>Service: CreateSurface:<br/>register document and runtime identities
    Service-->>Protocol: RootVisualHandle and DispatcherHandle
    Protocol-->>Pipeline: Construction reply over pipes
    Note over Pipeline: Successful result returns to the image host,<br/>then display connection follows
```

In this extension, the markup provider prepares App.xaml and design-time resources without using the application's compiled App XBF. The instance manager also sends preview configuration for `XamlControlsResources` and the requested theme. The surface loads prepared XAML with WinUI's XAML reader or executes construction actions through its action service. Staged project assemblies, XAML metadata providers, and PRI files supply custom types and resources.

The surface records stable runtime identities and source information, and reports visual-tree changes to Visual Studio. The returned root handle identifies an object; it is separate from the window used to display it. A successful construction reply therefore completes document creation, while embedding happens in the next stage.

Implementation: [document preparation](../src/WinUIDesigner.Vsix/Platform/WinUIDesignerInstanceManager.cs), [construction and tree notifications](../src/WinUIDesigner.Surface/Services/SurfaceService.cs), and [project type/resource resolution](../src/WinUIDesigner.Surface/ProjectRuntimeResolver.cs).

## Embedding the surface

Visual Studio supplies the artboard, selection adorners, and editing tools. The isolated process supplies the rendered XAML content. The scene view must connect that content to the artboard and keep the display coordinates aligned with the editing tools.

| Platform | Display connection |
| --- | --- |
| WPF | Attach the remote surface's child HWND to the designer's holder window. |
| UWP | Obtain the app view's visual and connect it to a DirectComposition target in Visual Studio. |
| This WinUI extension | Host the document in `DesktopWindowXamlSource` and attach its window beneath the designer's overlay. |

```mermaid
sequenceDiagram
    participant Image as IsolatedSurfaceImageHost
    participant Holder as WinUIHwndHost<br/>/ VS artboard overlay
    participant Pipeline as InstanceBuilderPipeline<br/>/ ProtocolHandler
    participant Service as SurfaceService<br/>(surface process)
    participant Island as DesignerSurface<br/>/ DesktopWindowXamlSource
    Note over Image,Service: Continue after successful document construction<br/>with the document ID and live-object handles
    Image->>Holder: CreateHwndHost
    Holder->>Holder: BuildWindowCore:<br/>create the holder and input overlay
    Holder->>Pipeline: PostMessage(548):<br/>holder HWND and viewport dimensions
    Pipeline->>Service: 548 SetSurfacePosition over pipes
    Service->>Island: SetSurfacePosition on the UI dispatcher
    Island->>Island: Create the XAML island,<br/>then reparent and resize its window below the overlay
    Island-->>Service: Attached document viewport
    Service->>Pipeline: Visual-tree and bounds notifications
    Note over Holder,Pipeline: Update the frontend's live tree and geometry
    Holder->>Holder: Position the overlay over the preview<br/>and forward input to the VS artboard
    Note over Holder: Continue with input and editing<br/>on the embedded document
```

The WinUI scene adapter replaces UWP's image host while retaining the shared artboard and tools. It sends the holder window and viewport dimensions after the document exists. The surface creates its XAML island, reparents the window, and keeps it below the overlay. The adapter forwards overlay input to the designer's tools.

Viewport size, preview device size, and pan/zoom are separate values. The viewport clips the visible area, device size controls the document's layout, and pan/zoom transforms its content. Keeping these separate allows the overlay, runtime bounds, and hit-test coordinates to agree as the user scrolls or zooms. Layout, bounds, and DPI notifications update the frontend's live view of the surface.

Implementation: [scene and window adapter](../src/WinUIDesigner.Vsix/Platform/WinUISceneView.cs), [overlay placement](../src/WinUIDesigner.Vsix/Platform/WinUIOverlayWindow.cs), and [WinUI island](../src/WinUIDesigner.Surface/Services/DesignerSurface.cs).

## Handling designer operations

The designer's tools run inside Visual Studio. They turn pointer input into selection, insertion, movement, and resizing of editing-model nodes. The surface answers questions about the live objects and applies the resulting runtime changes. Object handles and source information connect the editing nodes to those objects. The live visual tree also contains template-generated children beyond the nodes explicitly authored in XAML.

```mermaid
sequenceDiagram
    participant Artboard as VS scene view<br/>/ artboard overlay
    participant Tools as VS editing tools<br/>/ Properties
    participant Model as SceneViewModel<br/>/ edit transaction
    participant Pipeline as InstanceBuilderPipeline<br/>/ live-object services
    participant Surface as Surface services<br/>(WinUI dispatcher)
    Note over Artboard,Surface: Continue with the embedded document<br/>and its live-object identities
    Artboard->>Tools: Pointer input from the overlay
    Note over Tools: Property-editor input also starts here
    opt Live data needed
        Tools->>Pipeline: Query through live-object services
        Pipeline->>Surface: Properties, hit testing, or snap data<br/>over the protocol connection
        Surface-->>Pipeline: Identities, values, and geometry
        Pipeline-->>Tools: Live data for the operation
    end
    Tools->>Model: Update selection or apply an edit
    opt Edit changes the document
        Model->>Pipeline: Process pending model changes
        Pipeline->>Surface: 507 ExecuteXamlActions over pipes
        Surface->>Surface: Apply the ordered changes<br/>to WinUI runtime objects
        Surface-->>Pipeline: Operation result and tree/layout notifications
        Pipeline->>Pipeline: Invalidate live values<br/>and process visual-tree mutations
    end
    Tools->>Artboard: Refresh selection and adorners<br/>using live geometry
    Note over Model: Design edits also feed the XAML synchronization<br/>described in the next section
```

The surface implements these operation groups:

| Operation | Runtime work |
| --- | --- |
| Property queries | Return live values, base/current values, value sources, and layout information. |
| Hit testing | Find live elements at a point or within a rectangle and return their identities. |
| Snapping | Return container-relative edges, centers, and supported text baselines. |
| Model changes | Create objects, set or clear properties, edit collections and dictionaries, and update names and resource relationships. |
| State and animation preview | Apply visual states and control storyboards on the UI thread. |

Property origins matter to the editing model: a default, local value, or style value can require a different edit. The native diagnostics helper supplies value-source information unavailable through the public WinUI property API. When diagnostics are unavailable, the property service falls back to inspecting local values and explicit style setters. Preview-only dimensions are kept separately from authored dimensions so the preview does not write its device size into XAML.

During a drag, the shared designer updates temporary edits and commits or cancels the final transaction. Its pipeline invalidates cached live values after processing changes and uses runtime geometry to refresh adorners. Rendering can also be suspended while related changes are applied; that suspension is separate from the editing transaction and cache invalidation.

Implementation: [surface services](../src/WinUIDesigner.Surface/Services), [object identities](../src/WinUIDesigner.Surface/Services/ObjectIdentityRegistry.cs), and [native diagnostics](../src/WinUIDesigner.DiagnosticsTap/DiagnosticsTap.cpp). The action service reports unsupported actions as errors; this implementation does not imply complete parity with every built-in designer operation.

## Synchronizing XAML and design changes

The shared text buffer and Visual Studio's XAML document model are the editing state. The surface holds the live preview of that model. Text edits are parsed into document changes, which the designer pipeline applies to the runtime tree. Design edits change the same document model through editing transactions, and the language service writes those changes back into the shared buffer.

```mermaid
sequenceDiagram
    participant Text as Shared XAML buffer<br/>/ text editor
    participant Model as SceneViewModel<br/>/ edit transaction
    participant Markup as MarkupDocument
    participant Language as XAML language service
    participant Pipeline as InstanceBuilderPipeline
    participant Surface as Surface services<br/>/ live WinUI objects
    Note over Model,Pipeline: Continue from the designer's editing model<br/>and the runtime updates described above
    alt Design edit
        Model->>Markup: Record changes to document nodes
        Markup->>Markup: SynchronizeText
        Markup->>Language: IncrementalSerialize
        Language-->>Markup: Serialization result
        opt Incremental serialization cannot handle the changes
            Markup->>Language: SerializeDocument
        end
        Markup->>Text: FormatSerializedText and CommitTextEdits<br/>through the editor integration
    else XAML text edit
        Text->>Markup: Changed text in the shared buffer
        Markup->>Language: ParseRegion or ParseDocument
        Language-->>Markup: Parsed document changes
        Markup->>Model: Apply changes and refresh the editing model
    end
    Model->>Pipeline: Schedule document construction or updates
    Pipeline->>Surface: Build or update the preview over pipes
    Surface-->>Pipeline: Values, identities, and layout feedback
    Note over Markup,Surface: Text synchronization and preview updates<br/>can be scheduled independently
```

For design edits, the language service first attempts incremental serialization. Changes that cannot be serialized incrementally cause the document to be serialized as a whole. The serializer handles namespace declarations, markup extensions, property elements, and collection content; text edits are formatted and committed through the shared editor integration.

Parsing and serialization have guards against feeding their own changes back into another edit. Hidden design-time properties are also excluded from normal serialization. Undo and redo use the shared designer and editor history, with corresponding runtime changes processed by the instance pipeline. Runtime notifications refresh the live model; they do not independently save the XAML file.

This extension reuses that frontend synchronization. Its responsibility is to preserve the document/object correspondence while applying action batches in order, then report the surviving visual tree and layout. Removing a visual does not immediately invalidate every object handle: an undo operation may reconnect an existing object before the document is closed.

Implementation: [ordered action execution](../src/WinUIDesigner.Surface/Services/XamlActionService.cs), [document ownership](../src/WinUIDesigner.Surface/Services/ObjectIdentityRegistry.cs), and [tree/layout publication](../src/WinUIDesigner.Surface/Services/SurfaceService.cs). Text parsing and serialization themselves are provided by Visual Studio's `Markup.dll` and XAML language service.

## Toolbox integration

Toolbox integration has three entry points. Static registration supplies standard controls, discovery supplies selectable types from assemblies, and automatic population watches project or package references. The configuration flag enabling automatic population does not itself discover controls.

| Entry point | This extension's implementation |
| --- | --- |
| Standard controls | Register a WinUI 3 group and provide encoded item content through the package's Toolbox provider. |
| Choose Items | Register WinUI discovery, item creation, and an isolated metadata AppDomain. Accept eligible public, non-abstract controls with a public parameterless constructor. |
| Automatic population | Reuse the available VS infrastructure; complete WinUI project/NuGet discovery is not supplied by the above registrations. |

The inspected VS automatic project source recognizes WPF and UWP base types, but does not list `Microsoft.UI.Xaml.FrameworkElement`. Consequently, the WinUI configuration's automatic-population flag cannot be taken as proof that every project control will appear. The extension supplies standard controls explicitly and a separate WinUI Choose Items discovery path.

```mermaid
sequenceDiagram
    participant Toolbox as VS Toolbox / shell
    participant Provider as WinUI Toolbox providers<br/>/ discovery / item creator
    participant Tools as AssetDropToolBehavior<br/>/ DefaultTypeInstantiator
    participant Model as SceneViewModel<br/>/ edit transaction
    participant Text as Shared XAML buffer
    participant Surface as InstanceBuilderPipeline<br/>/ surface services
    Note over Tools,Model: Toolbox insertion uses the editing and<br/>synchronization paths described above
    alt Registered standard control
        Toolbox->>Provider: GetItemContent
        Provider-->>Toolbox: Encoded type, assembly, and capability data
    else Choose Items discovery
        Toolbox->>Provider: GetItemInfo for a candidate type
        Provider->>Provider: Validate the type and CreateToolboxItem
        Provider-->>Toolbox: Encoded ToolDataObject
    end
    Toolbox->>Tools: Insert the selected item
    Tools->>Tools: Decode the asset and resolve required references
    Tools->>Tools: Choose the insertion point,<br/>snapping, placement, and defaults
    Tools->>Model: CreateInstance and insert document nodes
    Model->>Model: Commit the editing transaction
    par XAML synchronization
        Model->>Text: Serialize the inserted control<br/>through the shared language service
    and Preview update
        Model->>Surface: Apply construction/update actions over pipes
        Surface-->>Model: Live control identities and geometry
    end
```

A Toolbox item describes a type to create; it does not carry an already-created WinUI control into Visual Studio. The extension encodes the shared XAML Toolbox format and adds WinUI capability information. On insertion, the shared tools resolve required references, select a valid destination, and apply placement and default values through the document model. The ordinary text/runtime synchronization then handles the new control.

The preferred Choose Items page identifies the registered discovery provider. It is separate from the Toolbox group containing the controls. The metadata AppDomain used for discovery is also separate from the process that renders the document.

Implementation: [Toolbox providers and registration](../src/WinUIDesigner.Vsix/Toolbox) and [package registration](../src/WinUIDesigner.Vsix/WinUIDesignerPackage.cs).

## Error handling and lifetime management

Failures can occur before the surface starts, while the connection initializes, during document construction, or after the preview is displayed. A created tab, running executable, or successful construction reply proves only that particular stage completed.

| Stage | Failure handling in this extension |
| --- | --- |
| Platform and staging | Reject missing private contracts, unsupported project/runtime architectures, missing payloads, and detected WinUI runtime version mismatches. |
| Startup and communication | Log startup exceptions and child-process exits; bound frame sizes and fail incomplete messages. |
| Document construction | Attempt the action-based loading alternative, then return construction failure information and publish error notifications. |
| Runtime operations | Return contract-specific failure results or notifications; dispatcher work observes cancellation and timeouts. |
| Shutdown | Cancel pending protocol work, release document services and native windows, and dispose the connection. |

```mermaid
sequenceDiagram
    participant Views as VS document/view services
    participant Pipeline as InstanceBuilderPipeline<br/>/ ProtocolHandler
    participant App as Surface application<br/>/ ProtocolHandler
    participant Service as Surface services
    participant State as DesignerSurface<br/>/ resources and object identities
    Note over Views,State: Continue with the document, connection,<br/>and runtime objects established above
    opt Construction or runtime failure
        Service->>Pipeline: Failure response or error notification over pipes
        Pipeline->>Views: Report the designer error
    end
    alt Close a document
        Views->>Pipeline: CloseDocument(documentId)
        Pipeline->>Service: 517 CloseDocument over pipes
        Service->>State: Release the document window, actions,<br/>and unshared resource/object ownership
        State-->>Service: Document state released
        Service-->>Pipeline: Visual-tree removals and close result
        Note over App,State: The process can continue serving other documents
    else Connection shutdown or Visual Studio exits
        App->>App: WatchHostProcess observes shutdown or host exit
        App->>App: Exit the application and begin disposal
        App->>App: ProtocolHandler.Shutdown<br/>cancels pending protocol/dispatcher work
        App->>Service: Dispose
        Service->>State: Release remaining documents and native windows
        App->>App: Dispose the protocol handler and pipes
    end
```

Closing a document releases its surface and document-owned state. Resources shared with another document are retained until their remaining owners are released. Document closure and process shutdown are separate lifetimes; the connection can serve more than one document.

The surface watches the Visual Studio process and exits when its host disappears or the protocol shuts down. On disposal, it shuts down protocol work before releasing its services and pipes. The extension package also restores the platform configuration entries and removes its creator hook when disposed.
