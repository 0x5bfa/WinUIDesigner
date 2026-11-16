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
/// The conditions the user forces are part of that string. The defaults come from the MSBuild properties named by
/// <see cref="DesignTimeOptionsResolver"/>.
/// </para>
/// <para>
/// Both are read from the application project when the project has one, the way Visual Studio scopes its own
/// designer settings, so an application and the libraries previewed in its surface process agree. Nothing here
/// throws: a failing call is logged and counts as "not set", which leaves the default options. What is read and
/// written is decided by <see cref="DesignTimeOptionsSettings"/>; this class reaches the project on the UI thread.
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
        DesignTimeOptions? options = null;
        string? loadedSource = null;

        Guard("reading the project", () => ThreadHelperBase.RunOnUIThread(() =>
        {
            options = DesignTimeOptionsSettings.Load(FindSettingsProject(), Warn, out string projectSource);
            loadedSource = projectSource;
        }));

        if (options is null || loadedSource is null)
        {
            // The project could not be reached at all: the defaults apply.
            return DesignTimeOptionsSettings.Load(null, Warn, out source);
        }

        source = loadedSource;
        return options;
    }

    /// <summary>
    /// Stores <paramref name="options"/> as the user's choice. Only the options that differ from what the project
    /// file asks for, and the conditions the user forces, are stored; when nothing is left, a stored override is removed.
    /// </summary>
    /// <returns>Whether the choice was stored.</returns>
    public bool Save(DesignTimeOptions options)
    {
        bool saved = false;

        Guard("storing the override", () => ThreadHelperBase.RunOnUIThread(() =>
        {
            HostProjectSettings? project = FindSettingsProject();
            if (project is null)
            {
                WinUIDesignerLogger.LogWarning(_Category, $"The design-time options '{options}' cannot be stored: project '{_project.ProjectName}' is not available.");
                return;
            }

            saved = DesignTimeOptionsSettings.Save(project, options, Warn);
        }));

        return saved;
    }

    private HostProjectSettings? FindSettingsProject()
    {
        IHostProject? project = (_project as ProjectContextBase)?.HostProject;
        if (project is null || !project.IsValid)
        {
            return null;
        }

        return new HostProjectSettings(project.FindApplicationProject() ?? project);
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

    private static void Warn(string message, Exception? exception)
    {
        WinUIDesignerLogger.LogWarning(_Category, message, exception);
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

    /// <summary>
    /// The settings project as <see cref="DesignTimeOptionsSettings"/> sees it. Used on the UI thread only.
    /// </summary>
    private sealed class HostProjectSettings(IHostProject project) : IDesignTimeOptionsProject
    {
        public string ProjectName => project.ProjectName;

        public string? ReadUserOverride()
        {
            return project.GetProjectSetting(UserOverrideSettingName) as string;
        }

        public void WriteUserOverride(string? userOverride)
        {
            project.SetProjectSetting(UserOverrideSettingName, userOverride);
        }

        public string? ReadBuildProperty(string propertyName)
        {
            return DesignTimeOptionsStore.ReadBuildProperty(project, propertyName);
        }
    }
}
