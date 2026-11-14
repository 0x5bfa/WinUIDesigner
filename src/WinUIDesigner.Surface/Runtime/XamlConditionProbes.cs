// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Xaml.Markup;
using Windows.Foundation.Metadata;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Answers, in the surface process, what conditional XAML asks about the running system.
/// </summary>
/// <remarks>
/// <para>
/// The XAML reader answers type and property conditions from Windows Runtime metadata: that of the operating
/// system for <c>Windows.*</c> names, and the <c>.winmd</c> files beside the executable for everything else.
/// The surface is a single-file executable without such files beside it, so there the reader finds no
/// <c>Microsoft.UI.Xaml</c> type at all. The probes look at the projection assemblies instead and apply the
/// rules of the metadata, measured against the reader in an application with Windows App SDK 2.5.1: a type is
/// present when it is a Windows Runtime type, and a property is present on the type that declares it, not on
/// derived types. A type that is only managed code, such as a class of the previewed project, is not present.
/// </para>
/// <para>
/// The methods are safe to call from any thread, except <see cref="EvaluateCustom"/>.
/// </para>
/// </remarks>
internal static class XamlConditionProbes
{
    private const string _ProjectionAssemblySuffix = ".Projection";

    /// <summary>
    /// Creates the probes of the surface process.
    /// </summary>
    public static XamlConditionProbeSet Create()
    {
        return new XamlConditionProbeSet
        {
            IsApiContractPresent = IsApiContractPresent,
            IsWindowsTypePresent = ApiInformation.IsTypePresent,
            IsWindowsPropertyPresent = ApiInformation.IsPropertyPresent,
            IsManagedTypePresent = IsManagedTypePresent,
            IsManagedPropertyPresent = IsManagedPropertyPresent,
            EvaluateCustom = EvaluateCustom,
        };
    }

    /// <summary>
    /// Returns whether the operating system implements an API contract in at least the given version.
    /// </summary>
    public static bool IsApiContractPresent(string contractName, int majorVersion, int? minorVersion)
    {
        return minorVersion.HasValue
            ? ApiInformation.IsApiContractPresent(contractName, (ushort)majorVersion, (ushort)minorVersion.Value)
            : ApiInformation.IsApiContractPresent(contractName, (ushort)majorVersion);
    }

    /// <summary>
    /// Returns whether a Windows Runtime type that is not part of the operating system is present.
    /// </summary>
    public static bool IsManagedTypePresent(string typeName)
    {
        // Metadata that can be read is the authority, as it is for the XAML reader.
        return ApiInformation.IsTypePresent(typeName) || FindProjectedType(typeName) is not null;
    }

    /// <summary>
    /// Returns whether a Windows Runtime type that is not part of the operating system declares a property.
    /// </summary>
    public static bool IsManagedPropertyPresent(string typeName, string propertyName)
    {
        if (ApiInformation.IsPropertyPresent(typeName, propertyName))
        {
            return true;
        }

        Type? type = FindProjectedType(typeName);
        return type is not null && DeclaresProperty(type, propertyName);
    }

    /// <summary>
    /// Creates a custom condition and evaluates it. Conditions are objects of the previewed application and
    /// usually derive from <c>DependencyObject</c>, so this has to run on the UI thread.
    /// </summary>
    /// <param name="typeName">The full name of a type that implements <see cref="IXamlCondition"/>.</param>
    /// <param name="argument">The text between the parentheses of the condition, exactly as written.</param>
    /// <exception cref="Exception">The type cannot be found, is not a condition, or fails.</exception>
    public static bool? EvaluateCustom(string typeName, string argument)
    {
        Type type = FindType(typeName, loadProjections: false)
            ?? throw new TypeLoadException($"The type '{typeName}' was not found. Build the project so that the designer can load its assembly.");
        if (!typeof(IXamlCondition).IsAssignableFrom(type))
        {
            throw new InvalidCastException($"The type '{typeName}' does not implement {typeof(IXamlCondition).FullName}.");
        }

        try
        {
            // The XAML reader creates one object per evaluation as well.
            var condition = (IXamlCondition)Activator.CreateInstance(type)!;
            return condition.Evaluate(argument);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Report what the constructor of the condition threw, not that it was called through reflection.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// Finds the projection of a Windows Runtime type. A type that exists as managed code only is not one.
    /// </summary>
    internal static Type? FindProjectedType(string typeName)
    {
        Type? type = FindType(typeName, loadProjections: true);
        return type is not null && type.IsDefined(typeof(WinRT.WindowsRuntimeTypeAttribute), inherit: false) ? type : null;
    }

    /// <summary>
    /// Returns whether <paramref name="type"/> itself declares a property, the way Windows Runtime metadata
    /// records it: inherited properties belong to the base type, events are no properties, and the members
    /// of a structure are fields.
    /// </summary>
    internal static bool DeclaresProperty(Type type, string propertyName)
    {
        if (type.IsValueType)
        {
            return false;
        }

        try
        {
            const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return type.GetProperty(propertyName, declared) is not null;
        }
        catch (AmbiguousMatchException)
        {
            return true;
        }
    }

    /// <summary>
    /// Finds a type by its full name: among the loaded assemblies, in the staged project, and optionally in
    /// projection assemblies of the Windows App SDK that are not loaded yet.
    /// </summary>
    /// <remarks>
    /// The name is taken as written. <c>XamlRuntimeUtilities.ResolveType</c> is of no use here: it turns
    /// <c>Windows.UI.Xaml</c> names into <c>Microsoft.UI.Xaml</c> names, which are different types to a condition.
    /// </remarks>
    private static Type? FindType(string typeName, bool loadProjections)
    {
        Type? type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null)
            {
                return type;
            }
        }

        try
        {
            type = ProjectRuntimeResolver.ResolveType(typeName);
            if (type is not null)
            {
                return type;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The staged assembly that defines the type cannot be loaded: the type is not available here.
        }

        if (!loadProjections)
        {
            return null;
        }

        foreach (string assemblyName in GetProjectionAssemblyNames(typeName))
        {
            try
            {
                type = Assembly.Load(new AssemblyName(assemblyName)).GetType(typeName, throwOnError: false, ignoreCase: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // No such assembly ships with the surface.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the names of the Windows App SDK assemblies that could project <paramref name="typeName"/>.
    /// </summary>
    private static IEnumerable<string> GetProjectionAssemblyNames(string typeName)
    {
        // XAML, composition, input and windowing types live in two assemblies that are not named after a namespace.
        if (typeName.StartsWith("Microsoft.UI.", StringComparison.Ordinal))
        {
            yield return "Microsoft.WinUI";
            yield return "Microsoft.InteractiveExperiences.Projection";
        }

        // The others are named after the namespace they project: Microsoft.Windows.Storage.Pickers.Projection.
        for (int separator = typeName.LastIndexOf('.'); separator > 0; separator = typeName.LastIndexOf('.', separator - 1))
        {
            string namespaceName = typeName.Substring(0, separator);
            if (namespaceName.IndexOf('.') < 0)
            {
                // A single name such as "Microsoft" is not the namespace of an assembly.
                yield break;
            }

            yield return namespaceName + _ProjectionAssemblySuffix;
        }
    }
}
