// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinUIDesigner.DesignTime;

/// <summary>
/// Lists the Windows releases a document can be previewed for, and reduces a Windows version written by a person
/// to the version of <c>Windows.Foundation.UniversalApiContract</c> that release ships.
/// </summary>
/// <remarks>
/// A conditional XAML namespace such as <c>...?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)</c> tests
/// a contract version, not a Windows build, so the contract version is all the designer needs to know.
/// </remarks>
internal static class WindowsVersionCatalog
{
    /// <summary>
    /// The contract conditional XAML namespaces are evaluated against.
    /// </summary>
    public const string ContractName = "Windows.Foundation.UniversalApiContract";

    /// <summary>
    /// The largest plain number read as a contract version; larger ones are release names or build numbers.
    /// </summary>
    public const int MaximumContractVersion = 99;

    private const int _MaximumBuild = 99999;

    /// <summary>
    /// Gets the known releases, oldest first.
    /// </summary>
    /// <remarks>
    /// The contract versions come from <c>Platforms\UAP\&lt;version&gt;\Platform.xml</c> and <c>PreviousPlatforms.xml</c>
    /// of the Windows SDK. Releases marked "servicing" shipped without an SDK of their own: they are enablement
    /// packages on the release before them and keep its contract.
    /// </remarks>
    public static IReadOnlyList<WindowsRelease> Releases { get; } = new[]
    {
        new WindowsRelease(10240, 1, "1507", "Windows 10 1507"),
        new WindowsRelease(10586, 2, "1511", "Windows 10 1511"),
        new WindowsRelease(14393, 3, "1607", "Windows 10 1607"),
        new WindowsRelease(15063, 4, "1703", "Windows 10 1703"),
        new WindowsRelease(16299, 5, "1709", "Windows 10 1709"),
        new WindowsRelease(17134, 6, "1803", "Windows 10 1803"),
        new WindowsRelease(17763, 7, "1809", "Windows 10 1809"),
        new WindowsRelease(18362, 8, "1903", "Windows 10 1903"),
        new WindowsRelease(18363, 8, "1909", "Windows 10 1909"), // servicing
        new WindowsRelease(19041, 10, "2004", "Windows 10 2004"),
        new WindowsRelease(19042, 10, "20H2", "Windows 10 20H2"), // servicing
        new WindowsRelease(19043, 10, "21H1", "Windows 10 21H1"), // servicing
        new WindowsRelease(19044, 10, "21H2", "Windows 10 21H2"), // servicing
        new WindowsRelease(19045, 10, "22H2", "Windows 10 22H2"), // servicing
        new WindowsRelease(20348, 12, "2104", "Windows Server 2022"), // the SDK calls it "Windows 10, version 2104"
        new WindowsRelease(22000, 14, "21H2", "Windows 11 21H2"),
        new WindowsRelease(22621, 15, "22H2", "Windows 11 22H2"),
        new WindowsRelease(22631, 15, "23H2", "Windows 11 23H2"), // servicing
        new WindowsRelease(26100, 19, "24H2", "Windows 11 24H2"),
        new WindowsRelease(26200, 19, "25H2", "Windows 11 25H2"), // servicing
        new WindowsRelease(28000, 20, "26H1", "Windows 11 26H1"),
    };

    /// <summary>
    /// Reads a Windows version in any of the accepted forms and returns its contract version.
    /// </summary>
    /// <param name="text">
    /// A contract version (<c>7</c>), a build number (<c>17763</c>), a full version (<c>10.0.17763.0</c>), a release name
    /// (<c>1809</c>, <c>24H2</c>) or a display name (<c>Windows 11 22H2</c>). A build between two known releases
    /// counts as the earlier one. <c>21H2</c> and <c>22H2</c> name a Windows 10 and a Windows 11 release with
    /// different contracts and are only accepted with the product name.
    /// </param>
    /// <param name="contractVersion">The major version of <see cref="ContractName"/>; zero when the text is not understood.</param>
    /// <returns>Whether <paramref name="text"/> was understood.</returns>
    public static bool TryParse(string? text, out int contractVersion)
    {
        contractVersion = 0;
        if (text is null)
        {
            return false;
        }

        string value = text.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (IsNumber(value))
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return false;
            }

            // A plain number is told apart by its size: 7 is a contract version, 1809 a release name, 17763 a build.
            if (number >= 1 && number <= MaximumContractVersion)
            {
                contractVersion = number;
                return true;
            }

            return number >= Releases[0].Build
                ? TryGetContractOfBuild(number, out contractVersion)
                : TryGetContractByName(value, out contractVersion);
        }

        if (value.IndexOf('.') >= 0)
        {
            // The form of TargetPlatformVersion. Windows 11 reports the major version 10 as well.
            return Version.TryParse(value, out Version? version)
                && version is not null
                && version.Major == 10
                && version.Minor == 0
                && TryGetContractOfBuild(version.Build, out contractVersion);
        }

        return TryGetContractByName(value, out contractVersion);
    }

    /// <summary>
    /// Returns the first release that ships <paramref name="contractVersion"/>, or <see langword="null"/> when no
    /// known release does. Contract versions that only appeared in preview builds have no release.
    /// </summary>
    public static WindowsRelease? FindByContract(int contractVersion)
    {
        IReadOnlyList<WindowsRelease> releases = Releases;
        for (int index = 0; index < releases.Count; index++)
        {
            if (releases[index].ContractVersion == contractVersion)
            {
                return releases[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Describes a contract version for a log entry or a tooltip, for example <c>Windows 10 1809 (contract 7)</c>.
    /// </summary>
    /// <param name="contractVersion">The contract version, or <see langword="null"/> for the installed Windows.</param>
    public static string Describe(int? contractVersion)
    {
        if (contractVersion is not int version)
        {
            return "installed Windows version";
        }

        string contract = "contract " + version.ToString(CultureInfo.InvariantCulture);
        return FindByContract(version) is { } release
            ? release.DisplayName + " (" + contract + ")"
            : contract;
    }

    private static bool TryGetContractOfBuild(int build, out int contractVersion)
    {
        contractVersion = 0;
        if (build > _MaximumBuild)
        {
            return false;
        }

        IReadOnlyList<WindowsRelease> releases = Releases;
        for (int index = releases.Count - 1; index >= 0; index--)
        {
            if (releases[index].Build <= build)
            {
                contractVersion = releases[index].ContractVersion;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetContractByName(string text, out int contractVersion)
    {
        contractVersion = 0;
        string name = NormalizeName(text);
        if (name.Length == 0)
        {
            return false;
        }

        // A release name without the product can name several releases; that is fine as long as they agree.
        IReadOnlyList<WindowsRelease> releases = Releases;
        bool found = false;
        for (int index = 0; index < releases.Count; index++)
        {
            WindowsRelease release = releases[index];
            if (!string.Equals(NormalizeName(release.ReleaseName), name, StringComparison.Ordinal))
            {
                continue;
            }

            if (found && contractVersion != release.ContractVersion)
            {
                contractVersion = 0;
                return false;
            }

            contractVersion = release.ContractVersion;
            found = true;
        }

        if (found)
        {
            return true;
        }

        for (int index = 0; index < releases.Count; index++)
        {
            if (string.Equals(NormalizeName(releases[index].DisplayName), name, StringComparison.Ordinal))
            {
                contractVersion = releases[index].ContractVersion;
                return true;
            }
        }

        return false;
    }

    private static bool IsNumber(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] < '0' || text[index] > '9')
            {
                return false;
            }
        }

        return true;
    }

    // "Windows 10, version 1809", "Windows 10 1809" and "win10 1809" all name the same release.
    private static string NormalizeName(string text)
    {
        var name = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsLetterOrDigit(character))
            {
                name.Append(char.ToUpperInvariant(character));
            }
        }

        return name.Replace("VERSION", string.Empty).Replace("WINDOWS", "WIN").ToString();
    }
}
