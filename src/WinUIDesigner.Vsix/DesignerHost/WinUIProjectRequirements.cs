// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.DesignTools.Utility;

namespace WinUIDesigner.DesignerHost;

/// <summary>
/// Describes what a project must provide for the bundled WinUI surface to preview it.
/// </summary>
/// <remarks>
/// The designer pane asks before it creates the designer and shows the answer in place of the design surface;
/// the shadow-copy worker factory asks again when the surface process is prepared, because that process
/// can belong to another project than the one that owns the open document.
/// </remarks>
internal static class WinUIProjectRequirements
{
    // Microsoft.WindowsAppSDK 2.5.1 resolves to this Microsoft.WinUI.dll version.
    // The assembly is bundled inside WinUISurface.exe for VSIX single-file deployment.
    public const string VerifiedWinUIRuntimeFileVersion = "3.0.0.2609";

    public const string VerifiedWindowsAppSdkVersion = "2.5.1";

    private const string _WinUIRuntimeFileName = "Microsoft.WinUI.dll";

    private static readonly ConcurrentDictionary<string, KeyValuePair<long, string>> _FileVersions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explains why the project cannot be previewed, or returns <see langword="null"/> when it can.
    /// </summary>
    /// <param name="platformIdentifier">The platform identity of the project.</param>
    /// <param name="referencePaths">The resolved reference paths of the project.</param>
    public static string? GetUnsupportedReason(PlatformIdentifier? platformIdentifier, IEnumerable<string?>? referencePaths)
    {
        return GetUnsupportedReason(platformIdentifier, GetWinUIRuntimeFileVersion(referencePaths));
    }

    /// <summary>
    /// Explains why the project cannot be previewed, or returns <see langword="null"/> when it can.
    /// </summary>
    /// <param name="platformIdentifier">The platform identity of the project.</param>
    /// <param name="winUIRuntimeFileVersion">
    /// The file version of the Microsoft.WinUI.dll the project references, an empty string when the file
    /// carries no version, or <see langword="null"/> when the project has no such reference to verify.
    /// </param>
    public static string? GetUnsupportedReason(PlatformIdentifier? platformIdentifier, string? winUIRuntimeFileVersion)
    {
        if (!string.Equals(platformIdentifier?.TargetFrameworkIdentifier, ".NETCoreApp", StringComparison.Ordinal))
        {
            return "WinUI Designer currently supports .NETCoreApp projects only.";
        }

        if (!string.Equals(platformIdentifier?.TargetRuntime, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            return $"WinUI Designer requires a managed project. Runtime: {platformIdentifier?.TargetRuntime}.";
        }

        if (winUIRuntimeFileVersion is not null
            && !string.Equals(VerifiedWinUIRuntimeFileVersion, winUIRuntimeFileVersion, StringComparison.Ordinal))
        {
            string actual = winUIRuntimeFileVersion.Length == 0 ? "an unknown version" : winUIRuntimeFileVersion;

            return $"WinUI Designer requires Windows App SDK {VerifiedWindowsAppSdkVersion} (Microsoft.WinUI.dll {VerifiedWinUIRuntimeFileVersion}). The project references Microsoft.WinUI.dll {actual}.";
        }

        return null;
    }

    /// <summary>
    /// Finds the Microsoft.WinUI.dll reference among the project's reference paths.
    /// </summary>
    public static string? FindWinUIRuntimePath(IEnumerable<string?>? referencePaths)
    {
        if (referencePaths is null)
        {
            return null;
        }

        foreach (string? referencePath in referencePaths)
        {
            if (!string.IsNullOrEmpty(referencePath)
                && string.Equals(Path.GetFileName(referencePath), _WinUIRuntimeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return referencePath;
            }
        }

        return null;
    }

    private static string? GetWinUIRuntimeFileVersion(IEnumerable<string?>? referencePaths)
    {
        string? path = FindWinUIRuntimePath(referencePaths);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        // The designer pane repeats this question while the document is open; avoid re-reading an unchanged file.
        long stamp = File.GetLastWriteTimeUtc(path).Ticks;
        if (_FileVersions.TryGetValue(path, out KeyValuePair<long, string> cached) && cached.Key == stamp)
        {
            return cached.Value;
        }

        string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty;
        _FileVersions[path] = new KeyValuePair<long, string>(stamp, version);

        return version;
    }
}
