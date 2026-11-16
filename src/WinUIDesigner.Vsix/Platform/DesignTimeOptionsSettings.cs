// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// The project the design-time options of a surface process are kept in: Visual Studio's project settings hold the
/// user's override, the MSBuild properties the defaults.
/// </summary>
/// <remarks>
/// Every member may throw; <see cref="DesignTimeOptionsSettings"/> treats a failing call as "not set".
/// </remarks>
internal interface IDesignTimeOptionsProject
{
    /// <summary>
    /// Gets the name of the project, for the log.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// Returns the stored override, or <see langword="null"/> when there is none.
    /// </summary>
    string? ReadUserOverride();

    /// <summary>
    /// Stores the override; <see langword="null"/> removes it.
    /// </summary>
    void WriteUserOverride(string? userOverride);

    /// <summary>
    /// Returns the evaluated value of an MSBuild property, or <see langword="null"/> when it is not set.
    /// </summary>
    string? ReadBuildProperty(string propertyName);
}

/// <summary>
/// Reads and writes the design-time options of a project: what the user chose, kept per project in the solution's
/// user options file, on top of what the project file says.
/// </summary>
/// <remarks>
/// This is the logic of <see cref="DesignTimeOptionsStore"/> without Visual Studio, so that it can be tested.
/// Nothing here throws: a value that cannot be read counts as absent and is reported, which leaves the defaults.
/// </remarks>
internal static class DesignTimeOptionsSettings
{
    /// <summary>
    /// Resolves the options in effect: the user's override, then the MSBuild properties, then the defaults.
    /// </summary>
    /// <param name="project">The project the options are kept in, or <see langword="null"/> when there is none.</param>
    /// <param name="warn">Receives what could not be read, with the exception behind it, if any.</param>
    /// <param name="source">Describes where the options came from, for the log.</param>
    public static DesignTimeOptions Load(IDesignTimeOptionsProject? project, Action<string, Exception?> warn, out string source)
    {
        if (project is null)
        {
            source = "no project to read them from";
            return DesignTimeOptionsResolver.Resolve(null, null, null);
        }

        string settingsProject = TryRead(warn, "reading the project name", () => project.ProjectName) ?? "?";
        string? userOverride = TryRead(warn, "reading the stored override", project.ReadUserOverride);
        string? targetPlatform = TryReadBuildProperty(project, DesignTimeOptionsResolver.TargetPlatformPropertyName, warn);
        string? windowsVersion = TryReadBuildProperty(project, DesignTimeOptionsResolver.WindowsVersionPropertyName, warn);

        // A value nobody can read is ignored by the resolver; say so, because the user probably mistyped it.
        if (!IsUnset(userOverride))
        {
            if (!DesignTimeOptions.TryReadFields(userOverride, out _, out _, out string? conditionOverrides))
            {
                warn($"The stored design-time options '{userOverride}' of project '{settingsProject}' are not understood and are ignored.", null);
            }
            else if (!XamlConditionOverrides.TryParse(conditionOverrides, out _))
            {
                warn($"Some of the conditional XAML conditions forced in the stored design-time options '{userOverride}' of project '{settingsProject}' cannot be read and are ignored.", null);
            }
        }

        if (!IsUnset(targetPlatform) && !DesignTimeTargetPlatform.TryParse(targetPlatform, out _))
        {
            warn($"{DesignTimeOptionsResolver.TargetPlatformPropertyName} '{targetPlatform}' of project '{settingsProject}' is not a known target platform and is ignored.", null);
        }

        if (!IsUnset(windowsVersion) && !WindowsVersionCatalog.TryParse(windowsVersion, out _))
        {
            warn($"{DesignTimeOptionsResolver.WindowsVersionPropertyName} '{windowsVersion}' of project '{settingsProject}' is not a known Windows version and is ignored.", null);
        }

        source = $"user override {Describe(userOverride)}, {DesignTimeOptionsResolver.TargetPlatformPropertyName} {Describe(targetPlatform)}, "
            + $"{DesignTimeOptionsResolver.WindowsVersionPropertyName} {Describe(windowsVersion)}, read from project '{settingsProject}'";

        return DesignTimeOptionsResolver.Resolve(userOverride, targetPlatform, windowsVersion);
    }

    /// <summary>
    /// Stores <paramref name="options"/> as the user's choice. Only the options that differ from what the project
    /// file asks for, and the conditions the user forces, are stored; when nothing is left, a stored override is removed.
    /// </summary>
    /// <param name="project">The project the options are kept in.</param>
    /// <param name="options">The options the user wants.</param>
    /// <param name="warn">Receives what went wrong, with the exception behind it, if any.</param>
    /// <returns>Whether the choice was stored.</returns>
    public static bool Save(IDesignTimeOptionsProject project, DesignTimeOptions options, Action<string, Exception?> warn)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        string? targetPlatform = TryReadBuildProperty(project, DesignTimeOptionsResolver.TargetPlatformPropertyName, warn);
        string? windowsVersion = TryReadBuildProperty(project, DesignTimeOptionsResolver.WindowsVersionPropertyName, warn);
        string? userOverride = DesignTimeOptionsResolver.CreateUserOverride(options, targetPlatform, windowsVersion);

        try
        {
            // A null value removes the setting. Visual Studio drops a value without telling when it has
            // nowhere to keep it, so read the setting back.
            project.WriteUserOverride(userOverride);
            if (string.Equals(project.ReadUserOverride(), userOverride, StringComparison.Ordinal))
            {
                return true;
            }

            warn($"Visual Studio did not keep the design-time options '{userOverride}' for project '{project.ProjectName}'.", null);
        }
        catch (Exception exception)
        {
            warn($"Design-time options: storing the override '{userOverride}' failed; the affected options keep their defaults.", exception);
        }

        return false;
    }

    private static string? TryReadBuildProperty(IDesignTimeOptionsProject project, string propertyName, Action<string, Exception?> warn)
    {
        return TryRead(warn, "reading " + propertyName, () => project.ReadBuildProperty(propertyName));
    }

    private static string? TryRead(Action<string, Exception?> warn, string action, Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            warn($"Design-time options: {action} failed; the affected options keep their defaults.", exception);
            return null;
        }
    }

    private static bool IsUnset(string? value)
    {
        return value is null || value.Trim().Length == 0;
    }

    private static string Describe(string? value)
    {
        return IsUnset(value) ? "not set" : "'" + value + "'";
    }
}
