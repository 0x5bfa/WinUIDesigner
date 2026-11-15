// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;

/// <summary>
/// Pins the shape of the members of Visual Studio's closed-source XAML designer that the extension overrides
/// or calls. The designer assemblies differ between Visual Studio versions, so every member has its own test:
/// a failure names exactly the member that differs in the Visual Studio the tests were built with.
/// </summary>
[TestClass]
public sealed class VsDesignerApiShapeTests
{
    private const string _DesignerInstanceManager = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.DesignerInstanceManager";
    private const string _SurfaceProcessContext = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.SurfaceProcessContext";
    private const string _UwpInstanceBuilderPlatform = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp.UwpInstanceBuilderPlatform";
    private const string _UwpDesignerInstanceBuilderPlatform = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation.UwpDesignerInstanceBuilderPlatform";
    private const string _SurfaceProcessMarkupProvider = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents.SurfaceProcessMarkupProvider";
    private const string _XamlCleaner = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents.XamlModify.XamlCleaner";
    private const string _XamlSceneView = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views.XamlSceneView";
    private const string _XmlNamespace = "Microsoft.VisualStudio.DesignTools.Markup.Metadata.XmlNamespace";
    private const string _XmlElementReference = "Microsoft.VisualStudio.DesignTools.Markup.Xaml.XmlElementReference";
    private const string _XmlNamespaceResolver = "Microsoft.VisualStudio.DesignTools.Markup.IXmlNamespaceResolver";
    private const string _CreateSurfaceRequestInfo = "Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM.CreateSurfaceRequestInfo";
    private const string _UwpInstanceBuilder = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp.UwpInstanceBuilder";
    private const string _LiveNodeProxy = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.LiveNodeProxy";
    private const string _InstanceBuilderContext = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.IInstanceBuilderContext";
    private const string _ProtocolHandler = "Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking.IProtocolHandler";
    private const string _SurfaceProcessMarkupProviderContract = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.ISurfaceProcessMarkupProvider";
    private const string _SurfaceProcessDocument = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.ISurfaceProcessDocument";
    private const string _MarkupDocument = "Microsoft.VisualStudio.DesignTools.Markup.IMarkupDocument";
    private const string _SceneView = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views.SceneView";
    private const string _UwpSceneView = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Views.UwpSceneView";
    private const string _SceneScrollViewer = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views.SceneScrollViewer";
    private const string _DeviceToolBar = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Views.Device.DeviceToolBar";
    private const string _DocumentRoot = "Microsoft.VisualStudio.DesignTools.Markup.DocumentModel.IDocumentRoot";
    private const string _MarkupTextBuffer = "Microsoft.VisualStudio.DesignTools.Utility.Text.IMarkupTextBuffer";
    private const string _MarkupTextReader = "Microsoft.VisualStudio.DesignTools.Utility.Text.IMarkupTextReader";

    private static readonly Lazy<DesignerAssemblies> _Assemblies = new(DesignerAssemblies.Load);

    [ClassCleanup]
    public static void ReleaseAssemblies()
    {
        if (_Assemblies.IsValueCreated)
        {
            _Assemblies.Value.Dispose();
        }
    }

    [TestMethod]
    public void InstanceManagerLetsDerivedClassesIgnoreProperties()
    {
        MethodDefinition method = RequireMethod(
            _DesignerInstanceManager,
            "ShouldIgnoreProperty",
            "IInstanceBuilderContext", "DocumentCompositeNode", "DocumentNode", "IProperty", "Boolean&");

        AssertOverridable(method);
        Assert.IsTrue(method.Parameters[4].IsOut, $"The last parameter must be an out parameter: {Describe(method)}");
        Assert.AreEqual("Boolean", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
    }

    [TestMethod]
    public void InstanceBuilderPlatformLetsDerivedClassesChooseTheBuilder()
    {
        MethodDefinition method = RequireMethod(
            _UwpInstanceBuilderPlatform,
            "GetInstanceBuilder",
            "IInstanceBuilderContext", "DocumentNode");

        AssertOverridable(method);
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformLetsDerivedClassesChooseTheFallbackType()
    {
        MethodDefinition method = RequireMethod(
            _UwpDesignerInstanceBuilderPlatform,
            "GetFallbackTypeInfo",
            "IType", "Boolean");

        AssertOverridable(method);
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformCanBeSubclassed()
    {
        TypeDefinition type = RequireType(_UwpDesignerInstanceBuilderPlatform);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");

        MethodDefinition? constructor = type.Methods.FirstOrDefault(method =>
            method.IsConstructor && !method.IsStatic && HasParameters(method, "IProjectContext"));
        Assert.IsNotNull(
            constructor,
            $"{type.FullName} needs a constructor (IProjectContext). Constructors found: {DescribeAll(type.Methods.Where(method => method.IsConstructor && !method.IsStatic))}");
        Assert.IsTrue(IsVisibleToDerivedClasses(constructor), $"The constructor must be public or protected: {Describe(constructor)}");
    }

    [TestMethod]
    public void MarkupProviderLetsDerivedClassesSeeTheCleaningResult()
    {
        MethodDefinition method = RequireMethod(
            _SurfaceProcessMarkupProvider,
            "SetCleaningResult",
            "SurfaceProcessDocument", "XamlResult");

        AssertOverridable(method);
        Assert.IsTrue(method.IsFamily || method.IsFamilyOrAssembly, $"Expected a protected method: {Describe(method)}");
    }

    [TestMethod]
    public void MarkupProviderExposesItsCleanerContext()
    {
        MethodDefinition method = RequireMethod(
            _SurfaceProcessMarkupProvider,
            "GetXamlCleanerContext",
            "IProjectContext");

        Assert.IsTrue(method.IsPublic, $"Expected a public method: {Describe(method)}");
        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
    }

    [TestMethod]
    public void XamlCleanerCanBeCalledDirectly()
    {
        MethodDefinition method = RequireMethod(
            _XamlCleaner,
            "Clean",
            "String", "IXamlCleanerContext", "IDocumentContext");

        Assert.IsTrue(method.IsPublic && method.IsStatic, $"Expected a public static method: {Describe(method)}");
        Assert.IsTrue(RequireType(_XamlCleaner).IsPublic, $"{_XamlCleaner} must be public.");
    }

    [TestMethod]
    public void XmlNamespaceExposesItsCondition()
    {
        PropertyDefinition property = RequireProperty(_XmlNamespace, "Condition");

        Assert.AreEqual("String", property.PropertyType.Name, $"Unexpected type of {_XmlNamespace}.Condition.");
        Assert.IsTrue(property.GetMethod is { IsPublic: true, IsStatic: false }, $"{_XmlNamespace}.Condition needs a public instance getter.");
    }

    [TestMethod]
    public void ElementReferenceResolvesPrefixes()
    {
        // XmlElementReference implements GetXmlNamespace publicly in Visual Studio 18 and explicitly in
        // Visual Studio 17.14, so the extension calls it through the interface that both versions have.
        MethodDefinition method = RequireMethod(_XmlNamespaceResolver, "GetXmlNamespace", "XmlnsPrefix");

        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
        Assert.AreEqual("XmlNamespace", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
        Assert.IsTrue(
            RequireType(_XmlElementReference).Interfaces.Any(@interface => @interface.InterfaceType.FullName == _XmlNamespaceResolver),
            $"{_XmlElementReference} must implement {_XmlNamespaceResolver}.");

        MethodDefinition isIgnorable = RequireMethod(_XmlElementReference, "IsIgnorable", "XmlNamespace");
        Assert.IsTrue(isIgnorable.IsPublic && !isIgnorable.IsStatic, $"Expected a public instance method: {Describe(isIgnorable)}");
    }

    [TestMethod]
    public void SurfaceProcessContextLetsDerivedClassesRestartTheSurface()
    {
        MethodDefinition method = RequireMethod(_SurfaceProcessContext, "RestartAfter", "TimeSpan", "Boolean");

        Assert.IsTrue(IsVisibleToDerivedClasses(method), $"Expected a protected or public method: {Describe(method)}");
        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
    }

    [TestMethod]
    public void SceneViewLetsDerivedClassesCreateTheScrollViewer()
    {
        MethodDefinition method = RequireMethod(_XamlSceneView, "CreateScrollViewer", "Artboard");

        AssertOverridable(method);
    }

    [TestMethod]
    public void SceneScrollViewerExposesItsDeviceToolBar()
    {
        // The conditional XAML switches go into the grid of the device toolbar, the child of a Border.
        PropertyDefinition property = RequireProperty(_SceneScrollViewer, "DeviceToolBar");

        Assert.IsTrue(property.GetMethod is { IsPublic: true, IsStatic: false }, $"{_SceneScrollViewer}.DeviceToolBar needs a public instance getter.");
        Assert.AreEqual("DeviceToolBar", property.PropertyType.Name, $"Unexpected type of {_SceneScrollViewer}.DeviceToolBar.");
        Assert.AreEqual("System.Windows.Controls.Border", RequireType(_DeviceToolBar).BaseType?.FullName, $"{_DeviceToolBar} must derive from Border.");
    }

    [TestMethod]
    public void SceneScrollViewerHoldsItsContentLikeAUserControl()
    {
        // Without a device toolbar, the switches go into a strip above the content of the scroll viewer.
        TypeDefinition type = RequireType(_SceneScrollViewer);

        Assert.AreEqual("System.Windows.Controls.UserControl", type.BaseType?.FullName, $"{_SceneScrollViewer} must derive from UserControl.");
    }

    [TestMethod]
    public void SceneViewLetsDerivedClassesReleaseTheirState()
    {
        MethodDefinition method = RequireMethod(_UwpSceneView, "DisposeInternal", "Boolean");

        AssertOverridable(method);
        Assert.IsTrue(method.IsFamily || method.IsFamilyOrAssembly, $"Expected a protected method: {Describe(method)}");
    }

    [TestMethod]
    public void SceneViewExposesItsDocumentAndProject()
    {
        PropertyDefinition document = RequireProperty(_SceneView, "DocumentRoot");
        PropertyDefinition project = RequireProperty(_SceneView, "ProjectContext");

        Assert.IsTrue(document.GetMethod is { IsPublic: true, IsStatic: false }, $"{_SceneView}.DocumentRoot needs a public instance getter.");
        Assert.AreEqual("IDocumentRoot", document.PropertyType.Name, $"Unexpected type of {_SceneView}.DocumentRoot.");
        Assert.IsTrue(project.GetMethod is { IsPublic: true, IsStatic: false }, $"{_SceneView}.ProjectContext needs a public instance getter.");
        Assert.AreEqual("IProjectContext", project.PropertyType.Name, $"Unexpected type of {_SceneView}.ProjectContext.");
    }

    [TestMethod]
    public void DocumentRootExposesItsText()
    {
        // The switches list the conditions of the document's current text and follow its edits.
        PropertyDefinition buffer = RequireProperty(_DocumentRoot, "TextBuffer");
        Assert.AreEqual("IMarkupTextBuffer", buffer.PropertyType.Name, $"Unexpected type of {_DocumentRoot}.TextBuffer.");

        TypeDefinition bufferType = RequireType(_MarkupTextBuffer);
        EventDefinition? textChanged = bufferType.Events.FirstOrDefault(candidate => candidate.Name == "TextChanged");
        Assert.IsNotNull(textChanged, $"{_MarkupTextBuffer}.TextChanged was not found.");
        Assert.AreEqual("EventHandler`1", textChanged.EventType.Name, $"Unexpected type of {_MarkupTextBuffer}.TextChanged.");
        Assert.AreEqual(
            "TextChangedEventArgs",
            (textChanged.EventType as GenericInstanceType)?.GenericArguments.Single().Name,
            $"Unexpected arguments of {_MarkupTextBuffer}.TextChanged.");
        Assert.IsTrue(
            bufferType.Interfaces.Any(implemented => implemented.InterfaceType.FullName == _MarkupTextReader),
            $"{_MarkupTextBuffer} must be a {_MarkupTextReader}.");

        Assert.AreEqual("Int32", RequireProperty(_MarkupTextReader, "Version").PropertyType.Name, $"Unexpected type of {_MarkupTextReader}.Version.");
        Assert.AreEqual("Int32", RequireProperty(_MarkupTextReader, "Length").PropertyType.Name, $"Unexpected type of {_MarkupTextReader}.Length.");
        MethodDefinition getText = RequireMethod(_MarkupTextReader, "GetText", "Int32", "Int32");
        Assert.AreEqual("String", getText.ReturnType.Name, $"Unexpected return type: {Describe(getText)}");
    }

    [TestMethod]
    public void SceneViewExposesTheReloadCommand()
    {
        PropertyDefinition property = RequireProperty(_XamlSceneView, "ReloadDesignerCommand");

        Assert.IsTrue(property.GetMethod is { IsPublic: true, IsStatic: false }, $"{_XamlSceneView}.ReloadDesignerCommand needs a public instance getter.");
        Assert.AreEqual("ICommand", property.PropertyType.Name, $"Unexpected type of {_XamlSceneView}.ReloadDesignerCommand.");
    }

    [TestMethod]
    public void CreateSurfaceRequestCanBeSubclassed()
    {
        TypeDefinition type = RequireType(_CreateSurfaceRequestInfo);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");
        Assert.IsTrue(
            type.Methods.Any(method => method.IsConstructor && !method.IsStatic && !method.HasParameters && IsVisibleToDerivedClasses(method)),
            $"{type.FullName} needs an accessible parameterless constructor.");
    }

    [TestMethod]
    public void InstanceBuilderCanBeSubclassedWithoutArguments()
    {
        // The builder for nodes a namespace policy drops derives from it and passes nothing to its constructor.
        TypeDefinition type = RequireType(_UwpInstanceBuilder);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");
        Assert.IsTrue(
            type.Methods.Any(method => method.IsConstructor && !method.IsStatic && IsVisibleToDerivedClasses(method) && method.Parameters.All(parameter => parameter.HasDefault)),
            $"{type.FullName} needs an accessible constructor that can be called without arguments. Constructors found: {DescribeAll(type.Methods.Where(method => method.IsConstructor && !method.IsStatic))}");
    }

    [TestMethod]
    [DataRow("CanInstantiate", "IInstanceBuilderContext,DocumentNode")]
    [DataRow("Instantiate", "IInstanceBuilderContext,LiveNodeProxy")]
    [DataRow("Initialize", "IInstanceBuilderContext,LiveNodeProxy,BuildOrder")]
    [DataRow("ReferencePropertyOrChild", "IInstanceBuilderContext,DocumentNode")]
    [DataRow("UpdateProperty", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,IProperty")]
    [DataRow("UpdateEvent", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,IEvent")]
    [DataRow("UpdateChild", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,Int32")]
    public void InstanceBuilderLetsDerivedClassesReplaceWhatItBuilds(string methodName, string parameterTypes)
    {
        MethodDefinition method = RequireMethod(_UwpInstanceBuilder, methodName, parameterTypes.Split(','));

        AssertOverridable(method);
    }

    [TestMethod]
    public void LiveNodeProxyCanBeInvalidated()
    {
        // An invalid proxy is how a builder tells its caller that no object was created.
        MethodDefinition method = RequireMethod(_LiveNodeProxy, "Invalidate");

        Assert.IsTrue(method.IsPublic && !method.IsStatic, $"Expected a public instance method: {Describe(method)}");
    }

    [TestMethod]
    public void InstanceBuilderContextHandsOutTheProxyOfANode()
    {
        // Later parameters, if any, have defaults: the extension passes the node alone.
        TypeDefinition type = RequireType(_InstanceBuilderContext);
        MethodDefinition[] candidates = type.Methods.Where(method => method.Name == "GetLiveNodeProxy").ToArray();

        Assert.IsTrue(
            candidates.Any(method =>
                method.Parameters.Count >= 1
                && method.Parameters[0].ParameterType.Name == "DocumentNode"
                && method.Parameters.Skip(1).All(parameter => parameter.HasDefault)
                && method.ReturnType.Name == "LiveNodeProxy"),
            $"{_InstanceBuilderContext}.GetLiveNodeProxy(DocumentNode) was not found. Methods named GetLiveNodeProxy: {DescribeAll(candidates)}");
    }

    [TestMethod]
    public void ProtocolHandlerSendsARequestOfAnyMessageNumber()
    {
        // The extension sends a message of its own to the surface and awaits the reply.
        MethodDefinition method = RequireMethod(_ProtocolHandler, "SendMessageAsync", "Int32", "Object");

        Assert.AreEqual(1, method.GenericParameters.Count, $"Expected one type parameter for the reply: {Describe(method)}");
        Assert.AreEqual("Task`1", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
    }

    [TestMethod]
    public void ProtocolHandlerTellsWhenTheConnectionCloses()
    {
        PropertyDefinition property = RequireProperty(_ProtocolHandler, "CancellationToken");

        Assert.AreEqual("CancellationToken", property.PropertyType.Name, $"Unexpected type of {_ProtocolHandler}.CancellationToken.");
    }

    [TestMethod]
    public void InstanceManagerExposesItsProtocolHandlerAndMarkupProvider()
    {
        PropertyDefinition protocolHandler = RequireProperty(_DesignerInstanceManager, "ProtocolHandler");
        PropertyDefinition markupProvider = RequireProperty(_DesignerInstanceManager, "MarkupProvider");

        Assert.AreEqual("IProtocolHandler", protocolHandler.PropertyType.Name, $"Unexpected type of {_DesignerInstanceManager}.ProtocolHandler.");
        Assert.IsTrue(protocolHandler.GetMethod is { IsStatic: false } getProtocolHandler && IsVisibleToDerivedClasses(getProtocolHandler), $"{_DesignerInstanceManager}.ProtocolHandler needs a getter derived classes can call.");
        Assert.AreEqual("ISurfaceProcessMarkupProvider", markupProvider.PropertyType.Name, $"Unexpected type of {_DesignerInstanceManager}.MarkupProvider.");
        Assert.IsTrue(markupProvider.GetMethod is { IsStatic: false } getMarkupProvider && IsVisibleToDerivedClasses(getMarkupProvider), $"{_DesignerInstanceManager}.MarkupProvider needs a getter derived classes can call.");
    }

    [TestMethod]
    public void MarkupProviderListsTheDocumentsItKnows()
    {
        // Their text is scanned for conditional namespaces before a document is sent to the surface.
        PropertyDefinition registered = RequireProperty(_SurfaceProcessMarkupProviderContract, "RegisteredDocuments");
        PropertyDefinition application = RequireProperty(_SurfaceProcessMarkupProviderContract, "AppXaml");
        PropertyDefinition document = RequireProperty(_SurfaceProcessDocument, "Document");

        StringAssert.StartsWith(registered.PropertyType.Name, "IEnumerable", $"Unexpected type of {_SurfaceProcessMarkupProviderContract}.RegisteredDocuments.");
        Assert.AreEqual("ISurfaceProcessDocument", application.PropertyType.Name, $"Unexpected type of {_SurfaceProcessMarkupProviderContract}.AppXaml.");
        Assert.AreEqual("IMarkupDocument", document.PropertyType.Name, $"Unexpected type of {_SurfaceProcessDocument}.Document.");
        Assert.IsTrue(
            RequireType(_MarkupDocument).Interfaces.Any(implemented => implemented.InterfaceType.Name == "IDocumentRoot"),
            $"{_MarkupDocument} must be a document root.");
    }

    private static TypeDefinition RequireType(string fullName)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        TypeDefinition? type = assemblies.FindType(fullName);
        Assert.IsNotNull(type, $"Type {fullName} was not found in the designer assemblies of {assemblies.InstallRoot}.");
        return type;
    }

    /// <summary>
    /// Finds a method by name and by the simple names of its parameter types, on the type or on a base type:
    /// a member the extension overrides may be declared anywhere up the hierarchy.
    /// </summary>
    private static MethodDefinition RequireMethod(string typeName, string methodName, params string[] parameterTypes)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        var candidates = new List<MethodDefinition>();
        for (TypeDefinition? type = RequireType(typeName); type is not null; type = assemblies.FindBaseType(type))
        {
            foreach (MethodDefinition method in type.Methods.Where(method => method.Name == methodName))
            {
                if (HasParameters(method, parameterTypes))
                {
                    return method;
                }

                candidates.Add(method);
            }
        }

        Assert.Fail(
            $"{typeName}.{methodName}({string.Join(", ", parameterTypes)}) was not found in the designer assemblies of {assemblies.InstallRoot}. " +
            $"Methods named {methodName}: {DescribeAll(candidates)}");
        return null!;
    }

    private static PropertyDefinition RequireProperty(string typeName, string propertyName)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        for (TypeDefinition? type = RequireType(typeName); type is not null; type = assemblies.FindBaseType(type))
        {
            PropertyDefinition? property = type.Properties.FirstOrDefault(candidate => candidate.Name == propertyName);
            if (property is not null)
            {
                return property;
            }
        }

        Assert.Fail($"Property {typeName}.{propertyName} was not found in the designer assemblies of {assemblies.InstallRoot}.");
        return null!;
    }

    private static void AssertOverridable(MethodDefinition method)
    {
        Assert.IsTrue(method.IsVirtual && !method.IsFinal, $"Expected a virtual method that is not sealed: {Describe(method)}");
        Assert.IsTrue(IsVisibleToDerivedClasses(method), $"Expected a public or protected method: {Describe(method)}");
    }

    private static bool IsVisibleToDerivedClasses(MethodDefinition method)
    {
        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    private static bool HasParameters(MethodDefinition method, params string[] parameterTypes)
    {
        return method.Parameters.Count == parameterTypes.Length
            && method.Parameters.Select(parameter => parameter.ParameterType.Name).SequenceEqual(parameterTypes, StringComparer.Ordinal);
    }

    private static string DescribeAll(IEnumerable<MethodDefinition> methods)
    {
        string description = string.Join("; ", methods.Select(Describe));
        return description.Length == 0 ? "none" : description;
    }

    private static string Describe(MethodDefinition method)
    {
        string access = method.IsPublic ? "public"
            : method.IsFamily ? "protected"
            : method.IsFamilyOrAssembly ? "protected internal"
            : method.IsAssembly ? "internal"
            : method.IsFamilyAndAssembly ? "private protected"
            : "private";
        string kind = method.IsStatic ? " static"
            : method.IsAbstract ? " abstract"
            : method.IsVirtual && method.IsFinal ? " sealed"
            : method.IsVirtual ? " virtual"
            : string.Empty;
        string parameters = string.Join(", ", method.Parameters.Select(parameter => parameter.ParameterType.Name));
        return $"{access}{kind} {method.ReturnType.Name} {method.DeclaringType.FullName}.{method.Name}({parameters})";
    }

    /// <summary>
    /// The designer assemblies of one Visual Studio installation, opened for metadata inspection only.
    /// </summary>
    private sealed class DesignerAssemblies : IDisposable
    {
        private const string _InstallRootVariable = "WINUIDESIGNER_VS_INSTALL_ROOT";
        private const string _InstallRootMetadata = "VsInstallRoot";

        private readonly List<ModuleDefinition> _modules = [];

        private DesignerAssemblies(string installRoot)
        {
            InstallRoot = installRoot;
        }

        public string InstallRoot { get; }

        public static DesignerAssemblies Load()
        {
            string installRoot = FindInstallRoot();
            var assemblies = new DesignerAssemblies(installRoot);
            foreach (string folder in new[] { "PrivateAssemblies", "PublicAssemblies" })
            {
                string directory = Path.Combine(installRoot, "Common7", "IDE", folder);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string path in Directory.EnumerateFiles(directory, "Microsoft.VisualStudio.DesignTools.*.dll"))
                {
                    try
                    {
                        assemblies._modules.Add(ModuleDefinition.ReadModule(path, new ReaderParameters { ReadingMode = ReadingMode.Deferred }));
                    }
                    catch (BadImageFormatException)
                    {
                        // Not a managed assembly; nothing the extension derives from lives there.
                    }
                }
            }

            if (assemblies._modules.Count == 0)
            {
                throw new FileNotFoundException(
                    $"No designer assemblies (Microsoft.VisualStudio.DesignTools.*.dll) were found under '{installRoot}'. " +
                    $"Set {_InstallRootVariable} to the root of a Visual Studio installation.");
            }

            return assemblies;
        }

        public TypeDefinition? FindType(string fullName)
        {
            foreach (ModuleDefinition module in _modules)
            {
                TypeDefinition? type = module.GetType(fullName);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the base type when it is defined in a designer assembly; the walk ends at framework types.
        /// </summary>
        public TypeDefinition? FindBaseType(TypeDefinition type)
        {
            return type.BaseType is null ? null : FindType(type.BaseType.GetElementType().FullName);
        }

        public void Dispose()
        {
            foreach (ModuleDefinition module in _modules)
            {
                module.Dispose();
            }

            _modules.Clear();
        }

        private static string FindInstallRoot()
        {
            string? configured = Environment.GetEnvironmentVariable(_InstallRootVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured!;
            }

            // Visual Studio's MSBuild defines VsInstallRoot; the test project embeds it at build time.
            string? embedded = typeof(VsDesignerApiShapeTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == _InstallRootMetadata)?.Value;
            if (!string.IsNullOrWhiteSpace(embedded))
            {
                return embedded!;
            }

            throw new DirectoryNotFoundException(
                $"The Visual Studio installation is unknown: the tests were built without VsInstallRoot. " +
                $"Build them with Visual Studio's MSBuild or set {_InstallRootVariable}.");
        }
    }
}
