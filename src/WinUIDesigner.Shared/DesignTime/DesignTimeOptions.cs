// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using System.Text;

namespace WinUIDesigner.DesignTime;

/// <summary>
/// The options that decide which members of a document the designer instantiates: the platform the document is
/// previewed as and the Windows version conditional XAML namespaces are evaluated against.
/// </summary>
/// <remarks>
/// The value is immutable and compares by content. Its text form is <c>v=1;target=android;contract=7</c>: fields are
/// separated by semicolons, <c>v</c> is the format version, <c>target</c> a <see cref="DesignTimeTargetPlatform.Id"/> and
/// <c>contract</c> a contract version. The <c>contract</c> field is left out when the installed Windows decides.
/// Fields a reader does not know are ignored, so a later format can add fields without breaking this one.
/// </remarks>
internal sealed class DesignTimeOptions : IEquatable<DesignTimeOptions>
{
    /// <summary>
    /// The name of the data member that carries the text form in the CreateSurface (516) request.
    /// </summary>
    public const string WireMemberName = "WinUIDesignTimeOptions";

    /// <summary>
    /// The version written into the <c>v</c> field.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// The value of the <c>contract</c> field that names the installed Windows. Only an override needs it,
    /// to say that it replaces a contract version with none; see <see cref="DesignTimeOptionsResolver"/>.
    /// </summary>
    internal const string InstalledWindowsContract = "os";

    private const string _VersionField = "v";
    private const string _TargetField = "target";
    private const string _ContractField = "contract";

    private static readonly char[] _FieldSeparators = { ';' };

    public DesignTimeOptions(DesignTimeTargetPlatform? targetPlatform, int? contractVersion)
    {
        TargetPlatform = targetPlatform ?? DesignTimeTargetPlatform.Windows;
        ContractVersion = contractVersion > 0 ? contractVersion : null;
    }

    /// <summary>
    /// Gets the options the designer uses unless told otherwise: the Windows target on the installed Windows.
    /// </summary>
    public static DesignTimeOptions Default { get; } = new(DesignTimeTargetPlatform.Windows, null);

    /// <summary>
    /// Gets the platform the document is previewed as.
    /// </summary>
    public DesignTimeTargetPlatform TargetPlatform { get; }

    /// <summary>
    /// Gets the identifier of <see cref="TargetPlatform"/>, for example <c>android</c>.
    /// </summary>
    public string TargetPlatformId => TargetPlatform.Id;

    /// <summary>
    /// Gets the major version of <c>Windows.Foundation.UniversalApiContract</c> that conditional XAML namespaces are
    /// evaluated against, or <see langword="null"/> when the installed Windows decides.
    /// </summary>
    public int? ContractVersion { get; }

    /// <summary>
    /// Gets whether these are the <see cref="Default"/> options.
    /// </summary>
    public bool IsDefault => Equals(Default);

    public static bool operator ==(DesignTimeOptions? left, DesignTimeOptions? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(DesignTimeOptions? left, DesignTimeOptions? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Reads the text form written by <see cref="Serialize"/>.
    /// </summary>
    /// <param name="text">The text form; fields it does not know are ignored.</param>
    /// <param name="options">The options read; <see cref="Default"/> when the text is not in the format.</param>
    /// <returns>
    /// Whether <paramref name="text"/> is in the format, which means it has a valid <c>v</c> field. A target this
    /// version does not know reads as Windows, and an unreadable contract version as none.
    /// </returns>
    public static bool TryParse(string? text, out DesignTimeOptions options)
    {
        if (!TryReadFields(text, out string? targetPlatformId, out string? contractVersion))
        {
            options = Default;
            return false;
        }

        options = new DesignTimeOptions(
            DesignTimeTargetPlatform.FromId(targetPlatformId),
            TryParseContractVersion(contractVersion, out int version) ? version : null);
        return true;
    }

    /// <summary>
    /// Returns a copy of these options that previews <paramref name="targetPlatform"/>.
    /// </summary>
    public DesignTimeOptions WithTargetPlatform(DesignTimeTargetPlatform? targetPlatform)
    {
        return new DesignTimeOptions(targetPlatform, ContractVersion);
    }

    /// <summary>
    /// Returns a copy of these options that evaluates conditional XAML against <paramref name="contractVersion"/>.
    /// </summary>
    /// <param name="contractVersion">The contract version, or <see langword="null"/> for the installed Windows.</param>
    public DesignTimeOptions WithContractVersion(int? contractVersion)
    {
        return new DesignTimeOptions(TargetPlatform, contractVersion);
    }

    /// <summary>
    /// Returns the text form, for example <c>v=1;target=android;contract=7</c>.
    /// </summary>
    public string Serialize()
    {
        return Format(TargetPlatformId, ContractVersion?.ToString(CultureInfo.InvariantCulture));
    }

    public bool Equals(DesignTimeOptions? other)
    {
        return other is not null
            && string.Equals(TargetPlatformId, other.TargetPlatformId, StringComparison.Ordinal)
            && ContractVersion == other.ContractVersion;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as DesignTimeOptions);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(TargetPlatformId) * 397) ^ ContractVersion.GetValueOrDefault();
        }
    }

    public override string ToString()
    {
        return Serialize();
    }

    /// <summary>
    /// Writes the text form from raw field values; a <see langword="null"/> field is left out.
    /// </summary>
    internal static string Format(string? targetPlatformId, string? contractVersion)
    {
        var text = new StringBuilder();
        text.Append(_VersionField).Append('=').Append(FormatVersion.ToString(CultureInfo.InvariantCulture));
        if (targetPlatformId is not null)
        {
            text.Append(_FieldSeparators[0]).Append(_TargetField).Append('=').Append(targetPlatformId);
        }

        if (contractVersion is not null)
        {
            text.Append(_FieldSeparators[0]).Append(_ContractField).Append('=').Append(contractVersion);
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads the raw field values of the text form without interpreting them.
    /// </summary>
    /// <param name="text">The text form.</param>
    /// <param name="targetPlatformId">The <c>target</c> field, or <see langword="null"/> when the text has none.</param>
    /// <param name="contractVersion">The <c>contract</c> field, or <see langword="null"/> when the text has none.</param>
    /// <returns>Whether <paramref name="text"/> has a valid <c>v</c> field.</returns>
    internal static bool TryReadFields(string? text, out string? targetPlatformId, out string? contractVersion)
    {
        targetPlatformId = null;
        contractVersion = null;
        if (text is null)
        {
            return false;
        }

        bool hasVersion = false;
        string? target = null;
        string? contract = null;
        foreach (string field in text.Split(_FieldSeparators))
        {
            int separator = field.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string name = field.Substring(0, separator).Trim();
            string value = field.Substring(separator + 1).Trim();
            if (name.Equals(_VersionField, StringComparison.OrdinalIgnoreCase))
            {
                // Any later version is read as well; it can only have added fields.
                hasVersion = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int version) && version >= 1;
            }
            else if (name.Equals(_TargetField, StringComparison.OrdinalIgnoreCase))
            {
                target = value;
            }
            else if (name.Equals(_ContractField, StringComparison.OrdinalIgnoreCase))
            {
                contract = value;
            }
        }

        if (!hasVersion)
        {
            return false;
        }

        targetPlatformId = target;
        contractVersion = contract;
        return true;
    }

    /// <summary>
    /// Reads the value of a <c>contract</c> field.
    /// </summary>
    internal static bool TryParseContractVersion(string? text, out int contractVersion)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out contractVersion) && contractVersion > 0;
    }
}
