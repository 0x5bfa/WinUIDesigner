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
