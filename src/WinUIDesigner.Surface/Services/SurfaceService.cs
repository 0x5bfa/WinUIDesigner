// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

// Implements the Visual Studio designer's surface-side protocol and translates
// those requests into WinUI objects, mutations, bounds, and property operations.
internal sealed class SurfaceService : IDisposable
{
    private const int OnApplicationEventMessage = 521;
    private const int SurfaceLayoutUpdatedMessage = 543;
    private const int ConfigureAppResourcesMessage = 6001;
    private const int FreezeCompositionTimeoutMilliseconds = 200;
    private static readonly Size DefaultSurfaceSize = new(800, 600);
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string XSurfUwpNamespace = "using:XSurfUwp";

    private readonly ProtocolHandler protocolHandler;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly ObjectIdentityRegistry objectIdentity;
    private readonly Dictionary<int, DesignerSurface> surfaces = new();
    private readonly SemaphoreSlim constructionGate = new(1, 1);
    private readonly Dictionary<int, ResourceDictionary> resourceDocuments = new();
    private readonly Dictionary<int, HashSet<int>> documentResources = new();
    private readonly Dictionary<int, SurfaceBoundsSnapshot> publishedSurfaceBounds = new();
    private readonly Dictionary<long, VisualTreeTopologyEntry[]> publishedVisualTreeTopologies = new();
    private readonly List<int> registrationIds = new();
    private readonly HitTestService hitTestService;
    private readonly SnapLineService snapLineService;
    private readonly PropertyService propertyService;
    private readonly XamlActionService xamlActionService;
    private readonly AnimationService animationService;
    private bool hasXamlControlsResources;
    private bool appResourcesLoaded;
    private ElementTheme appRequestedTheme = ElementTheme.Default;

    public SurfaceService(
        ProtocolHandler protocolHandler,
        DispatcherQueue dispatcherQueue,
        ObjectIdentityRegistry objectIdentity)
    {
        this.protocolHandler = protocolHandler;
        this.dispatcherQueue = dispatcherQueue;
        this.objectIdentity = objectIdentity;

        // These numeric message IDs are the shared VS designer wire contract; the
        // matching request/response types come from the private VS contract assembly.
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<CreateSurfaceRequestInfo, CreateSurfaceResponseInfo>(516, HandleCreateSurfaceAsync));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<CloseDocumentRequestInfo, ResponseWithError>(517, HandleCloseDocumentAsync));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetPanZoomTransformRequestInfo, ResponseWithError>(518, HandleSetPanZoomTransformAsync));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetArtboardColorsRequestInfo, ResponseWithError>(519, HandleSetArtboardColorsAsync));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetDeviceSizeRequestInfo, ResponseWithError>(520, HandleSetDeviceSizeAsync));
        registrationIds.Add(protocolHandler.RegisterMessageObserver<SetFreezeStateInfo>(527, HandleSetFreezeState));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<SetSurfacePositionRequestInfo, ResponseWithError>(548, HandleSetSurfacePositionAsync));
        registrationIds.Add(protocolHandler.RegisterAsyncMessageObserver<AppResourcesRequest, ResponseWithError>(ConfigureAppResourcesMessage, HandleConfigureAppResourcesAsync));

        hitTestService = new HitTestService(protocolHandler, dispatcherQueue, objectIdentity);
        snapLineService = new SnapLineService(protocolHandler, dispatcherQueue, objectIdentity);
        xamlActionService = new XamlActionService(protocolHandler, dispatcherQueue, objectIdentity, this);
        propertyService = new PropertyService(protocolHandler, dispatcherQueue, objectIdentity, xamlActionService);
        animationService = new AnimationService(protocolHandler, dispatcherQueue, objectIdentity);
    }

    private async Task<CreateSurfaceResponseInfo> HandleCreateSurfaceAsync(CreateSurfaceRequestInfo requestInfo)
    {
        try
        {
            if (!await constructionGate.WaitAsync(TimeSpan.FromSeconds(30), protocolHandler.CancellationToken).ConfigureAwait(false))
                throw new TimeoutException("Another document construction did not finish within 30 seconds.");
        }
        catch (Exception ex) { return CreateFailure(ex); }
        try { return await CreateSurfaceCoreAsync(requestInfo).ConfigureAwait(false); }
        finally { constructionGate.Release(); }
    }

    private async Task<CreateSurfaceResponseInfo> CreateSurfaceCoreAsync(CreateSurfaceRequestInfo requestInfo)
    {
        CreateDocumentInfo? document = requestInfo.Document;
        try
        {
            if (document is null || document.DocumentId == 0)
                throw new ArgumentException("A nonzero document ID is required.");
            await InvokeOnDispatcher(() => { CloseSurfaceDocument(document.DocumentId); return true; }, ex => throw ex).ConfigureAwait(false);
            foreach (CreateDocumentInfo? resourceDocument in new[] { requestInfo.Application, requestInfo.DesignTimeResources })
            {
                if (resourceDocument is null) continue;
                bool cached = await InvokeOnDispatcher(() => resourceDocuments.ContainsKey(resourceDocument.DocumentId), ex => throw ex).ConfigureAwait(false);
                if (cached)
                {
                    await InvokeOnDispatcher(() => { TrackResourceDocument(document.DocumentId, resourceDocument.DocumentId); return true; }, ex => throw ex).ConfigureAwait(false);
                    continue;
                }
                // A resource build can fail before its dictionary is installed.
                // Record ownership first so the failure path also releases the
                // partially constructed nonvisual objects and action handles.
                await InvokeOnDispatcher(() => { TrackResourceDocument(document.DocumentId, resourceDocument.DocumentId); return true; }, ex => throw ex).ConfigureAwait(false);
                var resources = await BuildDocumentAsync(resourceDocument, resources: true).ConfigureAwait(false);
                await InvokeOnDispatcher(() =>
                {
                    ResourceDictionary dictionary = resources.Root switch
                    {
                        Application application => application.Resources,
                        ResourceDictionary value => value,
                        _ => throw new InvalidOperationException("The resource document did not construct a ResourceDictionary."),
                    };
                    Application.Current.Resources.MergedDictionaries.Add(dictionary);
                    resourceDocuments.Add(resourceDocument.DocumentId, dictionary);
                    TrackResourceDocument(document.DocumentId, resourceDocument.DocumentId);
                    return true;
                }, ex => throw ex).ConfigureAwait(false);
            }
            var result = await BuildDocumentAsync(document, resources: false).ConfigureAwait(false);
            return await InvokeOnDispatcher(() => CreateSurface(requestInfo, result.Root, result.PreparedXaml), ex => throw ex).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"CreateSurface failed for document {document?.DocumentId}: {ex}");
            if (ex is DocumentConstructionException constructionError)
                protocolHandler.PostMessage(535, new InstanceBuildingErrors { DocumentId = document?.DocumentId ?? 0, Errors = constructionError.SerializedErrors });
            else
                protocolHandler.PostMessage(529, new UnhandledExceptionResponse
                {
                    DocumentId = document?.DocumentId ?? 0, Message = ex.Message, CallStack = ex.ToString(), IsArtboardException = true,
                });
            await InvokeOnDispatcher(() => { if (document is not null) CloseSurfaceDocument(document.DocumentId); return true; }, _ => false).ConfigureAwait(false);
            return CreateFailure(ex);
        }
    }

    private async Task<(object Root, string? PreparedXaml)> BuildDocumentAsync(CreateDocumentInfo document, bool resources)
    {
        Exception? parseFailure = null;
        if (!document.HasActions)
        {
            try
            {
                return await InvokeOnDispatcher(() =>
                {
                    using var scope = objectIdentity.EnterDocument(document.DocumentId);
                    string xaml = TryReadPreparedXaml(document) ?? document.InitialXamlContent;
                    if (string.IsNullOrWhiteSpace(xaml)) throw new InvalidOperationException("The prepared document has no XAML.");
                    string runtimeXaml = resources ? ExtractResourceXaml(xaml) : xaml;
                    object root = XamlReader.Load(SanitizePreparedXaml(runtimeXaml));
                    objectIdentity.Track(root);
                    return (root, (string?)xaml);
                }, ex => throw ex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                parseFailure = ex;
                Program.WriteDiagnosticTrace($"Prepared XAML parsing failed for document {document.DocumentId}; requesting construction actions: {ex}");
            }
        }

        CreateDocumentInfo actionsDocument = document;
        if (!document.HasActions)
        {
            actionsDocument = await protocolHandler.SendMessageAsync<CreateDocumentInfo>(534,
                new GetDocumentBuildingActionsRequest { DocumentId = document.DocumentId })
                .WaitAsync(TimeSpan.FromSeconds(30), protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        if (actionsDocument is null || actionsDocument.DocumentId != document.DocumentId || !actionsDocument.HasActions)
            throw new InvalidOperationException($"No construction actions were returned for document {document.DocumentId}.", parseFailure);
        return await InvokeOnDispatcher(() =>
        {
            if (!resources) return (xamlActionService.BuildDocument(actionsDocument), (string?)null);
            // Isolate action-built App resources from the designer's own dictionary.
            ResourceDictionary previous = Application.Current.Resources;
            Application.Current.Resources = new ResourceDictionary();
            try
            {
                object root = xamlActionService.BuildDocument(actionsDocument);
                return (root is Application app ? (object)app.Resources : root, (string?)null);
            }
            finally { Application.Current.Resources = previous; }
        }, ex => throw ex).ConfigureAwait(false);
    }

    private void TrackResourceDocument(int documentId, int resourceId)
    {
        if (!documentResources.TryGetValue(documentId, out var ids))
            documentResources[documentId] = ids = new HashSet<int>();
        ids.Add(resourceId);
    }

    private static string ExtractResourceXaml(string xaml)
    {
        XDocument document = XDocument.Parse(xaml);
        XElement root = document.Root ?? throw new InvalidOperationException("The resource document is empty.");
        if (root.Name.LocalName != "Application") return xaml;
        XElement? property = root.Elements().FirstOrDefault(value => value.Name.LocalName == "Application.Resources");
        XElement dictionary = property?.Elements().FirstOrDefault(value => value.Name.LocalName == "ResourceDictionary")
            ?? new XElement(root.Name.Namespace + "ResourceDictionary", property?.Elements() ?? []);
        foreach (XAttribute declaration in root.Attributes().Where(value => value.IsNamespaceDeclaration))
            if (dictionary.Attribute(declaration.Name) is null) dictionary.Add(new XAttribute(declaration));
        return dictionary.ToString();
    }

    private void CloseSurfaceDocument(int documentId)
    {
        // VS can close an application/resource document directly after an edit.
        // Keeping it in the cache would reuse its old dictionary on the next build.
        if (resourceDocuments.Remove(documentId, out var closedResource))
        {
            Application.Current.Resources.MergedDictionaries.Remove(closedResource);
            foreach (var ids in documentResources.Values) ids.Remove(documentId);
        }
        if (documentResources.Remove(documentId, out var resourceIds))
        {
            foreach (int resourceId in resourceIds)
            {
                if (documentResources.Values.Any(ids => ids.Contains(resourceId))) continue;
                if (resourceDocuments.Remove(resourceId, out var dictionary))
                    Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                xamlActionService.ReleaseDocument(resourceId);
            }
        }
        if (surfaces.Remove(documentId, out DesignerSurface? surface))
        {
            PublishVisualTreeMutation(surface.Content, VisualMutationType.Remove);
            surface.Dispose();
        }
        publishedSurfaceBounds.Remove(documentId);
        xamlActionService.ReleaseDocument(documentId);
    }

    private CreateSurfaceResponseInfo CreateSurface(CreateSurfaceRequestInfo requestInfo, object content, string? preparedXaml)
    {
        try
        {
            int documentId = requestInfo.Document?.DocumentId ?? 0;
            using var scope = objectIdentity.EnterDocument(documentId);
            FrameworkElement root = content as FrameworkElement
                ?? throw new InvalidOperationException("The document root must be a WinUI FrameworkElement.");
            XamlRuntimeUtilities.RegisterNameScope(root);
            var surface = new DesignerSurface(root, DefaultSurfaceSize.Width, DefaultSurfaceSize.Height);
            surface.BoundsInvalidated += (_, _) => PublishSurfaceBoundsIfCurrent(documentId, surface);
            surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
            surface.DpiChanged += dpi => protocolHandler.PostMessage(530, new SurfaceDpiChangedEvent { DocumentId = documentId, SurfaceDpi = dpi });
            surface.SetRequestedTheme(appRequestedTheme);
            surfaces[documentId] = surface;

            RegisterPreparedSourceInfo(requestInfo.Document, preparedXaml, root);

            long dispatcherHandle = objectIdentity.GetHandle(dispatcherQueue);
            long rootVisualHandle = objectIdentity.GetHandle(root);
            PublishVisualTreeMutation(root, VisualMutationType.Add);
            PublishSurfaceBounds(documentId, surface);
            Program.WriteDiagnosticTrace($"CreateSurface completed: DocumentId={documentId}, DispatcherHandle={dispatcherHandle}, RootVisualHandle={rootVisualHandle}, DesiredSize={root.DesiredSize.Width}x{root.DesiredSize.Height}, ActualSize={root.ActualWidth}x{root.ActualHeight}.");

            _ = dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!surfaces.TryGetValue(documentId, out var current) || !ReferenceEquals(current, surface)) return;
                using var scope = objectIdentity.EnterDocument(documentId);
                PublishVisualTreeMutation(root, VisualMutationType.Add);
                protocolHandler.PostMessage(OnApplicationEventMessage, new OnApplicationEventResponse { EventName = "OnIdle" });
            });

            return new CreateSurfaceResponseInfo { DispatcherHandle = dispatcherHandle, RootVisualHandle = rootVisualHandle };
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"CreateSurface failed: {ex}");
            // Let the construction failure path release resources and identities,
            // including a surface installed before a later initialization failure.
            throw;
        }
    }

    private void RegisterPreparedSourceInfo(CreateDocumentInfo? document, string? preparedXaml, FrameworkElement root)
    {
        if (document is null || string.IsNullOrWhiteSpace(document.DesignTimeSurfaceUri) || string.IsNullOrWhiteSpace(preparedXaml))
        {
            return;
        }

        try
        {
            XDocument xamlDocument = XDocument.Parse(preparedXaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            if (xamlDocument.Root is null)
            {
                return;
            }

            var markupElements = new List<XElement>();
            foreach (XElement element in xamlDocument.Root.DescendantsAndSelf())
            {
                if (!element.Name.LocalName.Contains('.', StringComparison.Ordinal)
                    && !element.Ancestors().Any(parent => parent.Name.LocalName is "ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate"))
                {
                    markupElements.Add(element);
                }
            }

            var runtimeElements = new List<DependencyObject>();
            AppendRuntimeElements(root, runtimeElements);
            var usedMarkup = new HashSet<XElement>();
            int mapped = 0;
            foreach (DependencyObject runtimeElement in runtimeElements)
            {
                XElement? markupElement = FindMarkupElement(runtimeElement, markupElements, usedMarkup);
                if (markupElement is null || markupElement is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
                {
                    continue;
                }

                usedMarkup.Add(markupElement);
                objectIdentity.RegisterSourceInfo(runtimeElement, new SourceInfo
                {
                    FileName = document.DesignTimeSurfaceUri,
                    LineNumber = (uint)lineInfo.LineNumber,
                    ColumnNumber = (uint)lineInfo.LinePosition,
                });
                mapped++;
            }

            Program.WriteDiagnosticTrace($"Prepared XAML source mapping registered: document={document.DocumentId}, mapped={mapped}/{runtimeElements.Count} visual(s).");
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"Prepared XAML source mapping failed for document {document.DocumentId}: {ex.Message}");
        }
    }

    private static void AppendRuntimeElements(DependencyObject element, List<DependencyObject> elements, HashSet<object>? visited = null)
    {
        visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(element)) return;
        elements.Add(element);

        // Walk the object graph produced from source XAML instead of the rendered
        // visual tree. The latter contains ControlTemplate implementation details
        // (Border/Grid/ContentPresenter, etc.) which have no source DocumentNode
        // and can otherwise steal same-type source entries from authored elements.
        foreach (string propertyName in new[] { "Resources", "MergedDictionaries", "Setters", "Value", "Children", "Content", "Child", "Items" })
        {
            System.Reflection.PropertyInfo? property = element.GetType().GetProperty(propertyName);
            if (property?.CanRead != true)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(element);
            }
            catch
            {
                continue;
            }

            if (value is DependencyObject child)
            {
                AppendRuntimeElements(child, elements, visited);
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                bool foundChild = false;
                foreach (object? item in enumerable)
                {
                    object? authoredValue = item is not null && item.GetType().IsGenericType && item.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                        ? item.GetType().GetProperty("Value")?.GetValue(item) : item;
                    if (authoredValue is DependencyObject dependencyObject)
                    {
                        AppendRuntimeElements(dependencyObject, elements, visited);
                        foundChild = true;
                    }
                }

                _ = foundChild;
            }
        }
    }

    private static XElement? FindMarkupElement(
        DependencyObject runtimeElement,
        IReadOnlyList<XElement> candidates,
        HashSet<XElement> used)
    {
        string runtimeTypeName = runtimeElement.GetType().Name;
        string? runtimeName = (runtimeElement as FrameworkElement)?.Name;
        XNamespace x = XamlNamespace;

        if (!string.IsNullOrEmpty(runtimeName))
        {
            foreach (XElement candidate in candidates)
            {
                if (used.Contains(candidate) || !MatchesMarkupType(candidate, runtimeElement.GetType()))
                {
                    continue;
                }

                string? candidateName = (string?)candidate.Attribute(x + "Name") ?? (string?)candidate.Attribute("Name");
                if (string.Equals(candidateName, runtimeName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        foreach (XElement candidate in candidates)
        {
            if (!used.Contains(candidate) && MatchesMarkupType(candidate, runtimeElement.GetType()))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool MatchesMarkupType(XElement element, Type runtimeType)
    {
        if (element.Name.NamespaceName.StartsWith("using:", StringComparison.Ordinal))
            return string.Equals(element.Name.NamespaceName[6..] + "." + element.Name.LocalName, runtimeType.FullName, StringComparison.Ordinal);
        if (element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal))
            return string.Equals(element.Name.NamespaceName[14..].Split(';')[0] + "." + element.Name.LocalName, runtimeType.FullName, StringComparison.Ordinal);
        return element.Name.LocalName == runtimeType.Name && runtimeType.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) == true;
    }

    internal void SetSurfaceContent(int documentId, object? content)
    {
        if (content is not FrameworkElement root)
        {
            throw new InvalidOperationException($"Surface content for document {documentId} must be a FrameworkElement, got {content?.GetType().FullName ?? "<null>"}.");
        }

        if (!surfaces.TryGetValue(documentId, out DesignerSurface? surface))
        {
            XamlRuntimeUtilities.RegisterNameScope(root);
            surface = new DesignerSurface(root, DefaultSurfaceSize.Width, DefaultSurfaceSize.Height);
            surface.BoundsInvalidated += (_, _) => PublishSurfaceBoundsIfCurrent(documentId, surface);
            surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
            surface.DpiChanged += dpi => protocolHandler.PostMessage(530, new SurfaceDpiChangedEvent { DocumentId = documentId, SurfaceDpi = dpi });
            surface.SetRequestedTheme(appRequestedTheme);
            surfaces[documentId] = surface;
        }
        else if (!ReferenceEquals(surface.Content, root))
        {
            XamlRuntimeUtilities.RegisterNameScope(root);
            FrameworkElement oldRoot = surface.Content;
            PublishVisualTreeMutation(oldRoot, VisualMutationType.Remove);
            surface.ReplaceContent(root);
            RemoveVisualTreeIdentities(oldRoot);
        }
        else
        {
            surface.RefreshLayout();
        }

        objectIdentity.GetHandle(root);
        PublishVisualTreeMutation(root, VisualMutationType.Add);
        PublishSurfaceBounds(documentId, surface);
        Program.WriteDiagnosticTrace($"Action surface content applied: document={documentId}, root={root.GetType().FullName}.");
    }

    internal void CompleteActionBatch()
    {
        foreach (var entry in surfaces)
        {
            using var scope = objectIdentity.EnterDocument(entry.Key);
            entry.Value.RefreshLayout();
            PublishVisualTreeMutationIfChanged(entry.Value.Content);
            PublishSurfaceBounds(entry.Key, entry.Value);
        }
    }

    private static string? TryReadPreparedXaml(CreateDocumentInfo? document)
    {
        if (string.IsNullOrWhiteSpace(document?.DesignTimeSurfaceUri))
        {
            return null;
        }

        string path = document.DesignTimeSurfaceUri;
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
            {
                path = uri.LocalPath;
            }
            else if (string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(
                    AppContext.BaseDirectory,
                    Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            }
        }

        if (!File.Exists(path))
        {
            Program.WriteDiagnosticTrace($"Prepared XAML path was not found for document {document.DocumentId}: '{path}'.");
            return null;
        }

        Program.WriteDiagnosticTrace($"Reading prepared XAML for document {document.DocumentId}: '{path}'.");
        return File.ReadAllText(path);
    }

    private static string SanitizePreparedXaml(string xaml)
    {
        XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
        XNamespace x = XamlNamespace;

        document.Root?.Attribute(x + "Class")?.Remove();

        if (document.Root is null)
        {
            return xaml;
        }

        foreach (XElement element in document.Root.DescendantsAndSelf())
        {
            // The UWP cleaner can suppress project code and emit its own stand-in.
            // Keep that decision, but use a WinUI control instead of UwpSurface.
            if (element.Name.NamespaceName == "using:XSurfUwp.Fallback"
                && XamlRuntimeUtilities.IsFallbackControlType("XSurfUwp.Fallback." + element.Name.LocalName))
            {
                element.Name = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation") + "ContentControl";
                if (element.Attribute("Content") is null && element.Attribute("Tag") is { } originalType)
                    element.SetAttributeValue("Content", originalType.Value);
            }
            foreach (XAttribute attribute in new List<XAttribute>(element.Attributes()))
            {
                if (!attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName == XSurfUwpNamespace)
                {
                    if (attribute.Name.LocalName is not (
                        "DT.RootWidth" or "DT.DesignWidth" or "DT.RuntimeWidth" or
                        "DT.RootHeight" or "DT.DesignHeight" or "DT.RuntimeHeight"))
                    {
                        attribute.Remove();
                    }

                    continue;
                }

                if (attribute.IsNamespaceDeclaration && attribute.Value == XSurfUwpNamespace)
                {
                    if (attribute.Name.LocalName == "d")
                    {
                        attribute.Value = BlendDesignNamespace;
                    }
                }
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private async Task<ResponseWithError> HandleCloseDocumentAsync(CloseDocumentRequestInfo request)
    {
        try
        {
            if (!await constructionGate.WaitAsync(TimeSpan.FromSeconds(30), protocolHandler.CancellationToken).ConfigureAwait(false))
                throw new TimeoutException("The document construction did not finish before close.");
        }
        catch (Exception ex) { return CreateResponseFailure(ex); }
        try
        {
            return await InvokeOnDispatcher(() =>
            {
                CloseSurfaceDocument(request.DocumentId);
                Program.WriteDiagnosticTrace($"CloseDocument (517) completed for document {request.DocumentId}.");
                return Success;
            }, CreateResponseFailure).ConfigureAwait(false);
        }
        finally { constructionGate.Release(); }
    }

    private Task<ResponseWithError> HandleSetPanZoomTransformAsync(SetPanZoomTransformRequestInfo request)
        => InvokeSurfaceAsync(request.DocumentId, surface => surface.SetPanZoomTransform(request.OffsetX, request.OffsetY, request.Scale), "SetPanZoomTransform (518)");

    private Task<ResponseWithError> HandleSetArtboardColorsAsync(SetArtboardColorsRequestInfo request)
        => InvokeSurfaceAsync(request.DocumentId, surface =>
        {
            if (TryParseColor(request.CheckerboardColor1, out Windows.UI.Color color1)
                && TryParseColor(request.CheckerboardColor2, out Windows.UI.Color color2))
            {
                surface.SetCheckerboardColors(color1, color2);
            }
        }, "SetArtboardColors (519)");

    private Task<ResponseWithError> HandleConfigureAppResourcesAsync(AppResourcesRequest request)
        => InvokeOnDispatcher(() =>
        {
            hasXamlControlsResources = request.HasXamlControlsResources;
            appRequestedTheme = Enum.TryParse(request.RequestedTheme, true, out ElementTheme theme)
                ? theme : ElementTheme.Default;
            LoadAppResourcesIfNeeded();
            foreach (DesignerSurface surface in surfaces.Values)
            {
                surface.SetRequestedTheme(appRequestedTheme);
            }
            return Success;
        }, CreateResponseFailure);

    private Task<ResponseWithError> HandleSetDeviceSizeAsync(SetDeviceSizeRequestInfo request)
        => InvokeSurfaceAsync(request.DocumentId, surface =>
        {
            surface.SetDeviceSize(request.Width, request.Height);
            PublishSurfaceBounds(request.DocumentId, surface);
        }, "SetDeviceSize (520)");

    private void HandleSetFreezeState(SetFreezeStateInfo request)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            ApplyFreezeState(request.DocumentIdToFreeze);
            Program.WriteDiagnosticTrace($"SetFreezeState (527): document={request.DocumentIdToFreeze}, handled directly on UI thread.");
            return;
        }

        DesignerSurface? targetSurface = null;
        bool waitForPreviousUnfreeze = false;
        InvokeOnDispatcher(() =>
        {
            if (request.DocumentIdToFreeze != 0
                && surfaces.TryGetValue(request.DocumentIdToFreeze, out DesignerSurface? surface))
            {
                targetSurface = surface;
                waitForPreviousUnfreeze = !surface.IsFrozen;
            }
        });

        if (waitForPreviousUnfreeze && targetSurface is not null)
        {
            bool completed = targetSurface.WaitForUnfreezeComposition(FreezeCompositionTimeoutMilliseconds);
            Program.WriteDiagnosticTrace(
                $"SetFreezeState (527) composition barrier: document={request.DocumentIdToFreeze}, completed={completed}.");
        }

        InvokeOnDispatcher(() => ApplyFreezeState(request.DocumentIdToFreeze));
        Program.WriteDiagnosticTrace($"SetFreezeState (527): document={request.DocumentIdToFreeze}.");
    }

    private void ApplyFreezeState(int documentIdToFreeze)
    {
        foreach ((int id, DesignerSurface surface) in surfaces)
        {
            if (id != documentIdToFreeze && surface.IsFrozen)
            {
                surface.Unfreeze();
            }
        }

        // Rendering suspension belongs to the UI thread, so resume the old
        // document before freezing the next one, regardless of dictionary order.
        if (documentIdToFreeze != 0
            && surfaces.TryGetValue(documentIdToFreeze, out DesignerSurface? targetSurface)
            && !targetSurface.IsFrozen)
        {
            targetSurface.Freeze();
        }
    }

    private Task<ResponseWithError> HandleSetSurfacePositionAsync(SetSurfacePositionRequestInfo request)
        => InvokeSurfaceAsync(request.DocumentId, surface =>
        {
            surface.SetSurfacePosition(new IntPtr(request.ParentWindow), request.Width, request.Height);
            LoadAppResourcesIfNeeded();
            long rootHandle = objectIdentity.GetHandle(surface.Content);
            PublishVisualTreeMutation(surface.Content, VisualMutationType.Add);
            PublishSurfaceBounds(request.DocumentId, surface);
        }, "SetSurfacePosition (548)");

    private void LoadAppResourcesIfNeeded()
    {
        if (!hasXamlControlsResources || appResourcesLoaded)
        {
            return;
        }

        try
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("ms-appx:///DesignerResources.xaml"),
            };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            appResourcesLoaded = true;
            Program.WriteDiagnosticTrace("XamlControlsResources loaded into Application resources.");
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"XamlControlsResources could not be loaded: {ex}");
            throw;
        }
    }

    private Task<ResponseWithError> InvokeSurfaceAsync(int documentId, Action<DesignerSurface> action, string operation)
    {
        return InvokeOnDispatcher(() =>
        {
            if (!surfaces.TryGetValue(documentId, out DesignerSurface? surface))
            {
                return new ResponseWithError { HResult = unchecked((int)0x80070057) };
            }

            action(surface);
            Program.WriteDiagnosticTrace($"{operation} completed for document {documentId}.");
            return Success;
        }, CreateResponseFailure);
    }

    private async Task<T> InvokeOnDispatcher<T>(Func<T> callback, Func<Exception, T> failureFactory)
    {
        try { return await DispatcherOperation.InvokeAsync(dispatcherQueue, callback, protocolHandler.CancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { Program.WriteDiagnosticTrace($"Surface operation failed: {ex}"); return failureFactory(ex); }
    }

    private void InvokeOnDispatcher(Action callback)
    {
        DispatcherOperation.Invoke(dispatcherQueue, () => { callback(); return true; }, protocolHandler.CancellationToken);
    }

    private static bool TryParseColor(string? value, out Windows.UI.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim().TrimStart('#');
        if (text.Length == 6)
        {
            text = "FF" + text;
        }
        if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return false;
        }

        color = Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return true;
    }

    private void PublishSurfaceBoundsIfCurrent(int documentId, DesignerSurface surface)
    {
        if (surfaces.TryGetValue(documentId, out DesignerSurface? currentSurface)
            && ReferenceEquals(currentSurface, surface))
        {
            PublishSurfaceBounds(documentId, surface);
        }
    }

    private void PublishSurfaceLayoutUpdatedIfCurrent(int documentId, DesignerSurface surface)
    {
        if (!surfaces.TryGetValue(documentId, out DesignerSurface? currentSurface)
            || !ReferenceEquals(currentSurface, surface))
        {
            return;
        }

        protocolHandler.PostMessage(SurfaceLayoutUpdatedMessage, new SurfaceLayoutUpdatedEvent
        {
            DocumentId = documentId,
        });
    }

    private void PublishSurfaceBounds(int documentId, DesignerSurface surface)
    {
        Rect contentBounds = surface.GetContentBounds();
        Rect documentBounds = surface.GetDocumentBounds();
        var currentBounds = new SurfaceBoundsSnapshot(contentBounds, documentBounds);
        if (publishedSurfaceBounds.TryGetValue(documentId, out SurfaceBoundsSnapshot publishedBounds)
            && publishedBounds.Equals(currentBounds))
        {
            Program.WriteDiagnosticTrace($"SurfaceBoundsChanged (531) skipped: bounds unchanged for document={documentId}.");
            return;
        }

        publishedSurfaceBounds[documentId] = currentBounds;
        protocolHandler.PostMessage(531, new SurfaceBoundsChangedEventContract
        {
            DocumentId = documentId,
            ContentBounds = RectContract.FromRect(contentBounds),
            DocumentBounds = RectContract.FromRect(documentBounds),
        });
        Program.WriteDiagnosticTrace(
            $"SurfaceBoundsChanged (531) posted: document={documentId}, " +
            $"content={contentBounds.X},{contentBounds.Y},{contentBounds.Width},{contentBounds.Height}, " +
            $"document={documentBounds.X},{documentBounds.Y},{documentBounds.Width},{documentBounds.Height}.");
    }

    [DataContract]
    private sealed class SurfaceBoundsChangedEventContract
    {
        [DataMember]
        public RectContract ContentBounds { get; set; } = new();

        [DataMember]
        public RectContract DocumentBounds { get; set; } = new();

        [DataMember]
        public int DocumentId { get; set; }
    }

    [DataContract]
    private sealed class RectContract
    {
        [DataMember(Name = "_x")]
        public double X { get; set; }

        [DataMember(Name = "_y")]
        public double Y { get; set; }

        [DataMember(Name = "_width")]
        public double Width { get; set; }

        [DataMember(Name = "_height")]
        public double Height { get; set; }

        public static RectContract FromRect(Rect rect)
            => new() { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };
    }

    private readonly record struct SurfaceBoundsSnapshot(Rect ContentBounds, Rect DocumentBounds);

    [DataContract]
    private sealed class AppResourcesRequest
    {
        [DataMember]
        public bool HasXamlControlsResources { get; set; }

        [DataMember]
        public string? RequestedTheme { get; set; }
    }

    private void PublishVisualTreeMutation(FrameworkElement root, VisualMutationType mutationType)
    {
        long rootHandle = objectIdentity.GetHandle(root);
        if (mutationType == VisualMutationType.Remove)
        {
            protocolHandler.PostMessage(9, new MutationList
            {
                Mutations =
                [
                    CreateVisualMutation(root, rootHandle, 0, 0, isRoot: true, mutationType),
                ],
            });
            publishedVisualTreeTopologies.Remove(rootHandle);
            Program.WriteDiagnosticTrace($"Visual tree mutation (9) posted: {mutationType} root handle={rootHandle}, type={root.GetType().FullName}.");
            return;
        }

        var mutations = new List<VisualTreeMutationEvent>();
        AppendVisualMutations(root, rootHandle, 0, 0, isRoot: true, mutations);

        protocolHandler.PostMessage(9, new MutationList
        {
            Mutations = mutations,
        });

        publishedVisualTreeTopologies[rootHandle] = CaptureVisualTreeTopology(root);
        Program.WriteDiagnosticTrace($"Visual tree mutation (9) posted: Add {mutations.Count} visual(s), root handle={rootHandle}, type={root.GetType().FullName}.");
    }

    private void PublishVisualTreeMutationIfChanged(FrameworkElement root)
    {
        long rootHandle = objectIdentity.GetHandle(root);
        VisualTreeTopologyEntry[] currentTopology = CaptureVisualTreeTopology(root);

        if (publishedVisualTreeTopologies.TryGetValue(rootHandle, out VisualTreeTopologyEntry[]? publishedTopology)
            && publishedTopology.AsSpan().SequenceEqual(currentTopology))
        {
            Program.WriteDiagnosticTrace($"Visual tree mutation (9) skipped: topology unchanged for root handle={rootHandle}.");
            return;
        }

        if (publishedTopology is not null)
        {
            var retained = currentTopology.Select(entry => entry.Handle).ToHashSet();
            // A full Add snapshot relocates surviving children, but does not remove
            // deleted nodes in LiveNodeTreeBase. Explicitly remove the vanished roots.
            var removed = publishedTopology.Where(entry => !retained.Contains(entry.Handle)).ToArray();
            var removedHandles = removed.Select(entry => entry.Handle).ToHashSet();
            var mutations = removed.Where(entry => !removedHandles.Contains(entry.ParentHandle))
                .Select(entry => new VisualTreeMutationEvent
                {
                    Element = new VisualElement { Handle = entry.Handle },
                    Relation = new ParentChildRelation { Parent = entry.ParentHandle, Child = entry.Handle, ChildIndex = entry.ChildIndex },
                    VisualMutationType = VisualMutationType.Remove,
                }).ToList();
            if (mutations.Count != 0) protocolHandler.PostMessage(9, new MutationList { Mutations = mutations });
            // Keep detached object handles alive until document close: Undo can
            // reconnect an existing proxy. Visual-tree deletion is not object release.
        }

        PublishVisualTreeMutation(root, VisualMutationType.Add);
    }

    private VisualTreeTopologyEntry[] CaptureVisualTreeTopology(DependencyObject root)
    {
        // Compare parent/child order and
        // stable handles first to avoid resending an unchanged tree on every idle.
        var topology = new List<VisualTreeTopologyEntry>();
        AppendVisualTreeTopology(root, parentHandle: 0, childIndex: 0, topology);
        return topology.ToArray();
    }

    private void AppendVisualTreeTopology(
        DependencyObject element,
        long parentHandle,
        uint childIndex,
        List<VisualTreeTopologyEntry> topology)
    {
        long handle = objectIdentity.GetHandle(element);
        topology.Add(new VisualTreeTopologyEntry(handle, parentHandle, childIndex));

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            AppendVisualTreeTopology(
                VisualTreeHelper.GetChild(element, index),
                handle,
                (uint)index,
                topology);
        }
    }

    private void AppendVisualMutations(
        DependencyObject element,
        long handle,
        long parentHandle,
        uint childIndex,
        bool isRoot,
        List<VisualTreeMutationEvent> mutations)
    {
        // The designer needs both each element and its ordered parent relation to
        // rebuild its own selectable tree from this out-of-process visual tree.
        mutations.Add(CreateVisualMutation(element, handle, parentHandle, childIndex, isRoot, VisualMutationType.Add));

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, index);
            long childHandle = objectIdentity.GetHandle(child);
            AppendVisualMutations(child, childHandle, handle, (uint)index, isRoot: false, mutations);
        }
    }

    private VisualTreeMutationEvent CreateVisualMutation(
        DependencyObject element,
        long handle,
        long parentHandle,
        uint childIndex,
        bool isRoot,
        VisualMutationType mutationType)
    {
        return new VisualTreeMutationEvent
        {
            Element = new VisualElement
            {
                Handle = handle,
                Type = GetDesignerTypeName(element.GetType()),
                Name = (element as FrameworkElement)?.Name,
                SourceInfo = objectIdentity.GetSourceInfo(element),
                IsRoot = isRoot,
            },
            Relation = new ParentChildRelation
            {
                Parent = parentHandle,
                Child = handle,
                ChildIndex = childIndex,
            },
            VisualMutationType = mutationType,
        };
    }

    private readonly record struct VisualTreeTopologyEntry(long Handle, long ParentHandle, uint ChildIndex);

    private void RemoveVisualTreeIdentities(DependencyObject element)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            RemoveVisualTreeIdentities(VisualTreeHelper.GetChild(element, index));
        }

        objectIdentity.RemoveObject(element);
    }

    private static string? GetDesignerTypeName(Type type)
        => type.FullName;

    private static ResponseWithError Success => new() { HResult = 0 };

    private static ResponseWithError CreateResponseFailure(Exception exception)
        => new() { HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception), Error = exception.ToString() };

    private static CreateSurfaceResponseInfo CreateFailure(Exception exception)
        => new() { HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception), Error = exception.ToString() };

    public void Dispose()
    {
        animationService.Dispose();
        xamlActionService.Dispose();
        hitTestService.Dispose();
        snapLineService.Dispose();
        propertyService.Dispose();
        foreach (int registrationId in registrationIds)
        {
            protocolHandler.UnregisterMessageObserver(registrationId);
        }
        foreach (DesignerSurface surface in surfaces.Values)
        {
            surface.Dispose();
        }
        surfaces.Clear();
        foreach (var dictionary in resourceDocuments.Values)
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        resourceDocuments.Clear(); documentResources.Clear();
        publishedSurfaceBounds.Clear(); publishedVisualTreeTopologies.Clear();
        objectIdentity.Clear();
    }
}
