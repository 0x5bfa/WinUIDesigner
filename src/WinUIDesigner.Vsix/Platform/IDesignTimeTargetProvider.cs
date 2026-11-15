// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.DesignTime;

namespace WinUIDesigner.Platform;

/// <summary>
/// Owns the design-time options of a surface process: the platform and the Windows version its documents are previewed for.
/// </summary>
/// <remarks>
/// The WinUI surface process context implements this interface, so the options belong to a surface process.
/// A library that shares the surface process of its application shares its options as well.
/// </remarks>
internal interface IDesignTimeTargetProvider
{
    /// <summary>
    /// Gets the options in effect. The value is kept in memory, so reading it is cheap on any thread.
    /// </summary>
    DesignTimeOptions DesignTimeOptions { get; }

    /// <summary>
    /// Occurs on the UI thread after <see cref="DesignTimeOptions"/> changed. When the change came from
    /// <see cref="SetDesignTimeOptions"/>, the design surface reloads right after the event.
    /// </summary>
    event EventHandler? DesignTimeOptionsChanged;

    /// <summary>
    /// Makes <paramref name="options"/> the user's choice for the project. When they differ from the options in
    /// effect, they are stored, <see cref="DesignTimeOptionsChanged"/> is raised and the design surface reloads.
    /// </summary>
    void SetDesignTimeOptions(DesignTimeOptions options);
}

/// <summary>
/// Gives designer code the design-time options of a project without knowing who owns them.
/// </summary>
internal static class DesignTimeTargetProvider
{
    /// <summary>
    /// Returns the owner of the options of <paramref name="project"/>, or <see langword="null"/> when the project
    /// has no WinUI surface process.
    /// </summary>
    public static IDesignTimeTargetProvider? Get(IProjectContext? project)
    {
        try
        {
            return project?.SurfaceProcessContext as IDesignTimeTargetProvider;
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Platform", "The design-time options of a project could not be reached; using the defaults.", exception);
            return null;
        }
    }

    /// <summary>
    /// Returns the options <paramref name="project"/> is previewed with; the defaults when it has no WinUI surface process.
    /// </summary>
    public static DesignTimeOptions GetDesignTimeOptions(IProjectContext? project)
    {
        return Read(Get(project));
    }

    /// <summary>
    /// Returns the options the documents of <paramref name="surfaceProcessContext"/> are previewed with.
    /// </summary>
    public static DesignTimeOptions GetDesignTimeOptions(ISurfaceProcessContext? surfaceProcessContext)
    {
        return Read(surfaceProcessContext as IDesignTimeTargetProvider);
    }

    private static DesignTimeOptions Read(IDesignTimeTargetProvider? provider)
    {
        try
        {
            return provider?.DesignTimeOptions ?? DesignTimeOptions.Default;
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Platform", "The design-time options could not be read; using the defaults.", exception);
            return DesignTimeOptions.Default;
        }
    }
}
