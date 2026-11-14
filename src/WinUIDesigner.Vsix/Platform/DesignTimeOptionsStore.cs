// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WinUIDesigner.DesignTime;

namespace WinUIDesigner.Platform;

/// <summary>
/// Reads and writes the design-time options of a project in Visual Studio.
/// </summary>
/// <remarks>
/// <para>
/// The user's override is a project setting, which Visual Studio keeps per project in the solution's user options
/// file. Only a string is stored there: Visual Studio deserializes all designer settings of a solution together and
/// only accepts types it can load, so a type of this extension would not survive a session without the extension.
/// The defaults come from the MSBuild properties named by <see cref="DesignTimeOptionsResolver"/>.
/// </para>
/// <para>
/// Both are read from the application project when the project has one, the way Visual Studio scopes its own
/// designer settings, so an application and the libraries previewed in its surface process agree. Nothing here
/// throws: a failing call is logged and counts as "not set", which leaves the default options.
/// </para>
/// </remarks>
internal sealed class DesignTimeOptionsStore
{
    /// <summary>
    /// The name of the project setting that holds the user's override.
    /// </summary>
    public const string UserOverrideSettingName = "WinUIDesigner.DesignTimeOptions";

    private const string _Category = "Platform";

    private readonly IProjectContext _project;

    public DesignTimeOptionsStore(IProjectContext project)
    {
        _project = project;
    }

    /// <summary>
    /// Resolves the options in effect: the user's override, then the MSBuild properties, then the defaults.
    /// </summary>
    /// <param name="source">Describes where the options came from, for the log.</param>
    public DesignTimeOptions Load(out string source)
    {
        string? settingsProject = null;
        string? userOverride = null;
        string? targetPlatform = null;
        string? windowsVersion = null;

        Guard("reading the project", () => ThreadHelperBase.RunOnUIThread(() =>
        {
            IHostProject? project = FindSettingsProject();
            if (project is null)
            {
                return;
            }

            settingsProject = project.ProjectName;
            userOverride = TryRead("reading the stored override", () => project.GetProjectSetting(UserOverrideSettingName) as string);
            targetPlatform = TryReadBuildProperty(project, DesignTimeOptionsResolver.TargetPlatformPropertyName);
            windowsVersion = TryReadBuildProperty(project, DesignTimeOptionsResolver.WindowsVersionPropertyName);
        }));

        // A value nobody can read is ignored by the resolver; say so, because the user probably mistyped it.
        if (!IsUnset(userOverride) && !DesignTimeOptions.TryParse(userOverride, out _))
        {
            WinUIDesignerLogger.LogWarning(_Category, $"The stored design-time options '{userOverride}' of project '{settingsProject}' are not understood and are ignored.");
        }

        if (!IsUnset(targetPlatform) && !DesignTimeTargetPlatform.TryParse(targetPlatform, out _))
        {
            WinUIDesignerLogger.LogWarning(_Category, $"{DesignTimeOptionsResolver.TargetPlatformPropertyName} '{targetPlatform}' of project '{settingsProject}' is not a known target platform and is ignored.");
        }

        if (!IsUnset(windowsVersion) && !WindowsVersionCatalog.TryParse(windowsVersion, out _))
        {
            WinUIDesignerLogger.LogWarning(_Category, $"{DesignTimeOptionsResolver.WindowsVersionPropertyName} '{windowsVersion}' of project '{settingsProject}' is not a known Windows version and is ignored.");
        }

        source = settingsProject is null
            ? "no project to read them from"
            : $"user override {Describe(userOverride)}, {DesignTimeOptionsResolver.TargetPlatformPropertyName} {Describe(targetPlatform)}, "
                + $"{DesignTimeOptionsResolver.WindowsVersionPropertyName} {Describe(windowsVersion)}, read from project '{settingsProject}'";

        return DesignTimeOptionsResolver.Resolve(userOverride, targetPlatform, windowsVersion);
    }

    /// <summary>
    /// Stores <paramref name="options"/> as the user's choice. Only the options that differ from what the project
    /// file asks for are stored; when none differs, a stored override is removed.
    /// </summary>
    /// <returns>Whether the choice was stored.</returns>
    public bool Save(DesignTimeOptions options)
    {
        bool saved = false;

        Guard("storing the override", () => ThreadHelperBase.RunOnUIThread(() =>
        {
            IHostProject? project = FindSettingsProject();
            if (project is null)
            {
                WinUIDesignerLogger.LogWarning(_Category, $"The design-time options '{options}' cannot be stored: project '{_project.ProjectName}' is not available.");
                return;
            }

            string? targetPlatform = TryReadBuildProperty(project, DesignTimeOptionsResolver.TargetPlatformPropertyName);
            string? windowsVersion = TryReadBuildProperty(project, DesignTimeOptionsResolver.WindowsVersionPropertyName);
            string? userOverride = DesignTimeOptionsResolver.CreateUserOverride(options, targetPlatform, windowsVersion);

            // A null value removes the setting. Visual Studio drops a value without telling when it has
            // nowhere to keep it, so read the setting back.
            project.SetProjectSetting(UserOverrideSettingName, userOverride);
            saved = string.Equals(project.GetProjectSetting(UserOverrideSettingName) as string, userOverride, StringComparison.Ordinal);
            if (!saved)
            {
                WinUIDesignerLogger.LogWarning(_Category, $"Visual Studio did not keep the design-time options '{userOverride}' for project '{project.ProjectName}'.");
            }
        }));

        return saved;
    }

    private IHostProject? FindSettingsProject()
    {
        IHostProject? project = (_project as ProjectContextBase)?.HostProject;
        if (project is null || !project.IsValid)
        {
            return null;
        }

        return project.FindApplicationProject() ?? project;
    }

    private static string? TryReadBuildProperty(IHostProject project, string propertyName)
    {
        return TryRead("reading " + propertyName, () => ReadBuildProperty(project, propertyName));
    }

    // HostProject.GetProperty answers from a cache that outlives an edit of the project file. This is the read
    // behind it (PropertyHelper.GetProperty in DesignerHost.dll) without the cache: the evaluated value of the
    // property in the active configuration. It has to run on the UI thread.
    private static string? ReadBuildProperty(IHostProject project, string propertyName)
    {
        Guid projectGuid = project.ProjectGuid;
        if (Package.GetGlobalService(typeof(SVsSolution)) is not IVsSolution solution
            || solution.GetProjectOfGuid(ref projectGuid, out IVsHierarchy hierarchy) < 0
            || hierarchy is not IVsBuildPropertyStorage storage)
        {
            return null;
        }

        string? configuration = null;
        if (Package.GetGlobalService(typeof(SVsSolutionBuildManager)) is IVsSolutionBuildManager buildManager)
        {
            var active = new IVsProjectCfg[1];
            if (buildManager.FindActiveProjectCfg(IntPtr.Zero, IntPtr.Zero, hierarchy, active) >= 0
                && active[0] is { } activeConfiguration
                && activeConfiguration.get_CanonicalName(out string name) >= 0)
            {
                configuration = name;
            }
        }

        return storage.GetPropertyValue(propertyName, configuration, (uint)_PersistStorageType.PST_PROJECT_FILE, out string value) >= 0
            ? value
            : null;
    }

    private static bool IsUnset(string? value)
    {
        return value is null || value.Trim().Length == 0;
    }

    private static string Describe(string? value)
    {
        return IsUnset(value) ? "not set" : "'" + value + "'";
    }

    private static void Guard(string action, Action call)
    {
        try
        {
            call();
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning(_Category, $"Design-time options: {action} failed; the affected options keep their defaults.", exception);
        }
    }

    private static string? TryRead(string action, Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning(_Category, $"Design-time options: {action} failed; the affected options keep their defaults.", exception);
            return null;
        }
    }
}
