// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using WinUIDesigner.Markup;

namespace WinUIDesigner.DesignTime;

/// <summary>
/// Decides the design-time options of a project from what the user chose and what the project file says.
/// </summary>
/// <remarks>
/// Each of the two options is decided on its own, in this order: the user's override, the MSBuild property, the
/// built-in default (the Windows target, the installed Windows version). An override therefore holds only the
/// options the user changed, in the text form of <see cref="DesignTimeOptions"/> with the other field left out;
/// an option the user did not touch keeps following the project file. The conditions the user forces exist only
/// in the override: the project file cannot set them.
/// </remarks>
internal static class DesignTimeOptionsResolver
{
    /// <summary>
    /// The MSBuild property that sets the default target platform, for example <c>android</c> or <c>Skia Desktop</c>.
    /// </summary>
    public const string TargetPlatformPropertyName = "WinUIDesignerTargetPlatform";

    /// <summary>
    /// The MSBuild property that sets the default Windows version, for example <c>1809</c> or <c>10.0.22621.0</c>.
    /// </summary>
    public const string WindowsVersionPropertyName = "WinUIDesignerWindowsVersion";

    /// <summary>
    /// Returns the options in effect.
    /// </summary>
    /// <param name="userOverride">The override written by <see cref="CreateUserOverride"/>, if the user has one.</param>
    /// <param name="targetPlatformProperty">The value of <see cref="TargetPlatformPropertyName"/>, if set.</param>
    /// <param name="windowsVersionProperty">The value of <see cref="WindowsVersionPropertyName"/>, if set.</param>
    /// <remarks>
    /// A value that cannot be read counts as absent, so the next source decides; nothing here throws.
    /// </remarks>
    public static DesignTimeOptions Resolve(string? userOverride, string? targetPlatformProperty, string? windowsVersionProperty)
    {
        DesignTimeOptions defaults = ResolveDefaults(targetPlatformProperty, windowsVersionProperty);
        if (!DesignTimeOptions.TryReadFields(userOverride, out string? targetPlatformId, out string? contractVersion, out string? conditionOverrides))
        {
            return defaults;
        }

        DesignTimeTargetPlatform targetPlatform = DesignTimeTargetPlatform.TryParse(targetPlatformId, out DesignTimeTargetPlatform chosen)
            ? chosen
            : defaults.TargetPlatform;

        int? contract = defaults.ContractVersion;
        if (IsInstalledWindows(contractVersion))
        {
            contract = null;
        }
        else if (DesignTimeOptions.TryParseContractVersion(contractVersion, out int version))
        {
            contract = version;
        }

        // An entry that cannot be read is left out; the others still apply.
        XamlConditionOverrides.TryParse(conditionOverrides, out XamlConditionOverrides overrides);
        return new DesignTimeOptions(targetPlatform, contract, overrides);
    }

    /// <summary>
    /// Returns the options the project file asks for, which apply where the user has no override.
    /// </summary>
    /// <param name="targetPlatformProperty">The value of <see cref="TargetPlatformPropertyName"/>, if set.</param>
    /// <param name="windowsVersionProperty">The value of <see cref="WindowsVersionPropertyName"/>, if set.</param>
    public static DesignTimeOptions ResolveDefaults(string? targetPlatformProperty, string? windowsVersionProperty)
    {
        DesignTimeTargetPlatform.TryParse(targetPlatformProperty, out DesignTimeTargetPlatform targetPlatform);
        return new DesignTimeOptions(
            targetPlatform,
            WindowsVersionCatalog.TryParse(windowsVersionProperty, out int contractVersion) ? contractVersion : null);
    }

    /// <summary>
    /// Returns the override that makes <see cref="Resolve"/> yield <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The options the user wants.</param>
    /// <param name="targetPlatformProperty">The value of <see cref="TargetPlatformPropertyName"/>, if set.</param>
    /// <param name="windowsVersionProperty">The value of <see cref="WindowsVersionPropertyName"/>, if set.</param>
    /// <returns>
    /// The override to store, holding only the options that differ from what the project file asks for and the
    /// conditions the user forces, or <see langword="null"/> when there is nothing to keep and a stored override
    /// should be removed.
    /// </returns>
    public static string? CreateUserOverride(DesignTimeOptions options, string? targetPlatformProperty, string? windowsVersionProperty)
    {
        DesignTimeOptions defaults = ResolveDefaults(targetPlatformProperty, windowsVersionProperty);
        bool overridesTargetPlatform = !string.Equals(options.TargetPlatformId, defaults.TargetPlatformId, StringComparison.Ordinal);
        bool overridesContractVersion = options.ContractVersion != defaults.ContractVersion;
        bool overridesConditions = !options.ConditionOverrides.IsEmpty;
        if (!overridesTargetPlatform && !overridesContractVersion && !overridesConditions)
        {
            return null;
        }

        string? contractVersion = null;
        if (overridesContractVersion)
        {
            // Leaving the field out would mean "as the project file says", so the installed Windows needs a name.
            contractVersion = options.ContractVersion?.ToString(CultureInfo.InvariantCulture) ?? DesignTimeOptions.InstalledWindowsContract;
        }

        return DesignTimeOptions.Format(
            overridesTargetPlatform ? options.TargetPlatformId : null,
            contractVersion,
            overridesConditions ? options.ConditionOverrides.Format() : null);
    }

    private static bool IsInstalledWindows(string? contractVersion)
    {
        return string.Equals(contractVersion, DesignTimeOptions.InstalledWindowsContract, StringComparison.OrdinalIgnoreCase);
    }
}
