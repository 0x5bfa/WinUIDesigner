using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
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

    private Task<CreateSurfaceResponseInfo> HandleCreateSurfaceAsync(CreateSurfaceRequestInfo requestInfo)
    {
        Program.WriteDiagnosticTrace("CreateSurface (516) received; dispatching visual creation to the WinUI thread.");
        return InvokeOnDispatcher(() => CreateSurface(requestInfo), CreateFailure);
    }

    private CreateSurfaceResponseInfo CreateSurface(CreateSurfaceRequestInfo requestInfo)
    {
        try
        {
            int documentId = requestInfo.Document?.DocumentId ?? 0;
            if (surfaces.Remove(documentId, out DesignerSurface? oldSurface))
            {
                publishedSurfaceBounds.Remove(documentId);
                objectIdentity.RemoveObject(oldSurface.Content);
                oldSurface.Dispose();
            }

            FrameworkElement root = CreateRootVisual(requestInfo.Document, out string? preparedXaml);
            var surface = new DesignerSurface(root, DefaultSurfaceSize.Width, DefaultSurfaceSize.Height);
            surface.BoundsInvalidated += (_, _) => PublishSurfaceBoundsIfCurrent(documentId, surface);
            surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
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
                PublishVisualTreeMutation(root, VisualMutationType.Add);
                protocolHandler.PostMessage(OnApplicationEventMessage, new OnApplicationEventResponse { EventName = "OnIdle" });
            });

            return new CreateSurfaceResponseInfo { DispatcherHandle = dispatcherHandle, RootVisualHandle = rootVisualHandle };
        }
        catch (Exception ex)
        {
            Program.WriteDiagnosticTrace($"CreateSurface failed: {ex}");
            return CreateFailure(ex);
        }
    }

    private FrameworkElement CreateRootVisual(CreateDocumentInfo? document, out string? preparedXaml)
    {
        preparedXaml = null;
        string? xaml = TryReadPreparedXaml(document) ?? document?.InitialXamlContent;
        if (!string.IsNullOrWhiteSpace(xaml))
        {
            try
            {
                string sanitized = SanitizePreparedXaml(xaml);
                if (XamlReader.Load(sanitized) is FrameworkElement loaded)
                {
                    // SourceInfo coordinates must stay in the coordinate space of the
                    // XAML prepared by Visual Studio. SurfaceProcessDocument maps those
                    // coordinates back to the editor document with the cleaner mapping.
                    // SanitizePreparedXaml can change line/column positions, so only use
                    // the sanitized text for runtime loading.
                    preparedXaml = xaml;
                    Program.WriteDiagnosticTrace($"Loaded prepared document XAML with WinUI XamlReader for document {document!.DocumentId} ({loaded.GetType().FullName}).");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                Program.WriteDiagnosticTrace($"WinUI XamlReader load failed; using fallback content: {ex.Message}");
            }
        }

        var grid = new Grid();
        grid.Children.Add(new Button
        {
            Content = "WinUI 3 Designer",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
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
                if (!element.Name.LocalName.Contains('.', StringComparison.Ordinal))
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

    private static void AppendRuntimeElements(DependencyObject element, List<DependencyObject> elements)
    {
        elements.Add(element);

        // Walk the object graph produced from source XAML instead of the rendered
        // visual tree. The latter contains ControlTemplate implementation details
        // (Border/Grid/ContentPresenter, etc.) which have no source DocumentNode
        // and can otherwise steal same-type source entries from authored elements.
        foreach (string propertyName in new[] { "Children", "Content", "Child", "Items" })
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
                AppendRuntimeElements(child, elements);
                return;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                bool foundChild = false;
                foreach (object? item in enumerable)
                {
                    if (item is DependencyObject dependencyObject)
                    {
                        AppendRuntimeElements(dependencyObject, elements);
                        foundChild = true;
                    }
                }

                if (foundChild)
                {
                    return;
                }
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
                if (used.Contains(candidate) || candidate.Name.LocalName != runtimeTypeName)
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
            if (!used.Contains(candidate) && candidate.Name.LocalName == runtimeTypeName)
            {
                return candidate;
            }
        }

        return null;
    }

    internal void SetSurfaceContent(int documentId, object? content)
    {
        if (content is not FrameworkElement root)
        {
            throw new InvalidOperationException($"Surface content for document {documentId} must be a FrameworkElement, got {content?.GetType().FullName ?? "<null>"}.");
        }

        if (!surfaces.TryGetValue(documentId, out DesignerSurface? surface))
        {
            surface = new DesignerSurface(root, DefaultSurfaceSize.Width, DefaultSurfaceSize.Height);
            surface.BoundsInvalidated += (_, _) => PublishSurfaceBoundsIfCurrent(documentId, surface);
            surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
            surface.SetRequestedTheme(appRequestedTheme);
            surfaces[documentId] = surface;
        }
        else if (!ReferenceEquals(surface.Content, root))
        {
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
        foreach (DesignerSurface surface in surfaces.Values)
        {
            surface.RefreshLayout();
            PublishVisualTreeMutationIfChanged(surface.Content);
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

    private Task<ResponseWithError> HandleCloseDocumentAsync(CloseDocumentRequestInfo request)
    {
        return InvokeOnDispatcher(() =>
        {
            if (surfaces.Remove(request.DocumentId, out DesignerSurface? surface))
            {
                publishedSurfaceBounds.Remove(request.DocumentId);
                if (objectIdentity.TryGetHandle(surface.Content, out long rootHandle))
                {
                    PublishVisualTreeMutation(surface.Content, VisualMutationType.Remove);
                }
                RemoveVisualTreeIdentities(surface.Content);
                surface.Dispose();
            }

            Program.WriteDiagnosticTrace($"CloseDocument (517) completed for document {request.DocumentId}.");
            return Success;
        }, CreateResponseFailure);
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

    private Task<T> InvokeOnDispatcher<T>(Func<T> callback, Func<Exception, T> failureFactory)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                completion.TrySetResult(callback());
            }
            catch (Exception ex)
            {
                Program.WriteDiagnosticTrace($"Surface operation failed: {ex}");
                completion.TrySetResult(failureFactory(ex));
            }
        }))
        {
            completion.TrySetResult(failureFactory(new InvalidOperationException("Failed to enqueue operation on the WinUI DispatcherQueue.")));
        }
        return completion.Task;
    }

    private void InvokeOnDispatcher(Action callback)
    {
        Exception? exception = null;
        using var completion = new System.Threading.ManualResetEventSlim();
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completion.Set();
            }
        }))
        {
            throw new InvalidOperationException("Failed to enqueue operation on the WinUI DispatcherQueue.");
        }

        completion.Wait();
        if (exception is not null)
        {
            throw new InvalidOperationException("WinUI DispatcherQueue operation failed.", exception);
        }
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

        PublishVisualTreeMutation(root, VisualMutationType.Add);
    }

    private VisualTreeTopologyEntry[] CaptureVisualTreeTopology(DependencyObject root)
    {
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
    }
}
