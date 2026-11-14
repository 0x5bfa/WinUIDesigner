// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Globalization;

namespace WinUIDesigner.DesignTime;

/// <summary>
/// A Windows release and the version of <c>Windows.Foundation.UniversalApiContract</c> it ships.
/// </summary>
internal sealed class WindowsRelease
{
    public WindowsRelease(int build, int contractVersion, string releaseName, string displayName)
    {
        Build = build;
        ContractVersion = contractVersion;
        ReleaseName = releaseName;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the build number, for example <c>17763</c>.
    /// </summary>
    public int Build { get; }

    /// <summary>
    /// Gets the major version of <c>Windows.Foundation.UniversalApiContract</c> in this release, for example <c>7</c>.
    /// </summary>
    public int ContractVersion { get; }

    /// <summary>
    /// Gets the release name without the product, for example <c>1809</c> or <c>22H2</c>.
    /// Windows 10 and Windows 11 share some names, so it does not always identify a release.
    /// </summary>
    public string ReleaseName { get; }

    /// <summary>
    /// Gets the name shown to the user, for example <c>Windows 10 1809</c>. It is unique in the catalog.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the version in the form MSBuild uses for <c>TargetPlatformVersion</c>, for example <c>10.0.17763.0</c>.
    /// </summary>
    public string Version => "10.0." + Build.ToString(CultureInfo.InvariantCulture) + ".0";

    public override string ToString()
    {
        return DisplayName;
    }
}
