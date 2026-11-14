// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace WinUIDesigner.DesignTime;

/// <summary>
/// A platform a document can be previewed as: Windows, or one of the Uno Platform targets.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the stable name of the target. It is what gets stored, sent to the surface process and
/// compared by code, and it equals the XAML prefix Uno Platform reserves for the target (<c>win</c>, <c>android</c>, ...).
/// <see cref="DisplayName"/> is meant for user interface only.
/// </remarks>
internal sealed class DesignTimeTargetPlatform
{
    public const string WindowsId = "win";
    public const string AndroidId = "android";
    public const string IosId = "ios";
    public const string WebAssemblyId = "wasm";
    public const string SkiaDesktopId = "skia";
    public const string AndroidSkiaId = "androidskia";
    public const string IosSkiaId = "iosskia";
    public const string WebAssemblySkiaId = "wasmskia";

    private readonly string _normalizedDisplayName;

    private DesignTimeTargetPlatform(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
        _normalizedDisplayName = Normalize(displayName);
    }

    /// <summary>
    /// Gets the target the designer previews unless told otherwise.
    /// </summary>
    public static DesignTimeTargetPlatform Windows { get; } = new(WindowsId, "Windows");

    public static DesignTimeTargetPlatform Android { get; } = new(AndroidId, "Android");

    public static DesignTimeTargetPlatform Ios { get; } = new(IosId, "iOS");

    public static DesignTimeTargetPlatform WebAssembly { get; } = new(WebAssemblyId, "WebAssembly");

    public static DesignTimeTargetPlatform SkiaDesktop { get; } = new(SkiaDesktopId, "Skia Desktop");

    public static DesignTimeTargetPlatform AndroidSkia { get; } = new(AndroidSkiaId, "Android (Skia)");

    public static DesignTimeTargetPlatform IosSkia { get; } = new(IosSkiaId, "iOS (Skia)");

    public static DesignTimeTargetPlatform WebAssemblySkia { get; } = new(WebAssemblySkiaId, "WebAssembly (Skia)");

    /// <summary>
    /// Gets every target, <see cref="Windows"/> first, in the order a user interface should list them.
    /// </summary>
    public static IReadOnlyList<DesignTimeTargetPlatform> All { get; } =
        new[] { Windows, Android, Ios, WebAssembly, SkiaDesktop, AndroidSkia, IosSkia, WebAssemblySkia };

    /// <summary>
    /// Gets the stable identifier of the target, for example <c>android</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the name of the target as shown to the user, for example <c>Android (Skia)</c>.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Returns the target with the identifier <paramref name="id"/>, or <see cref="Windows"/> when there is none.
    /// </summary>
    /// <param name="id">The identifier, in any letter case; a display name is accepted as well.</param>
    public static DesignTimeTargetPlatform FromId(string? id)
    {
        return TryParse(id, out DesignTimeTargetPlatform platform) ? platform : Windows;
    }

    /// <summary>
    /// Finds a target by its identifier or display name, ignoring letter case, white space and punctuation.
    /// </summary>
    /// <param name="text">For example <c>wasm</c>, <c>WebAssembly</c> or <c>Android (Skia)</c>.</param>
    /// <param name="platform">The target found; <see cref="Windows"/> when the text names no target.</param>
    /// <returns>Whether <paramref name="text"/> names a target.</returns>
    public static bool TryParse(string? text, out DesignTimeTargetPlatform platform)
    {
        platform = Windows;
        if (text is null)
        {
            return false;
        }

        string name = Normalize(text);
        if (name.Length == 0)
        {
            return false;
        }

        IReadOnlyList<DesignTimeTargetPlatform> all = All;
        for (int index = 0; index < all.Count; index++)
        {
            DesignTimeTargetPlatform candidate = all[index];
            if (string.Equals(candidate.Id, name, StringComparison.Ordinal)
                || string.Equals(candidate._normalizedDisplayName, name, StringComparison.Ordinal))
            {
                platform = candidate;
                return true;
            }
        }

        return false;
    }

    public override string ToString()
    {
        return Id;
    }

    private static string Normalize(string text)
    {
        var name = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsLetterOrDigit(character))
            {
                name.Append(char.ToLowerInvariant(character));
            }
        }

        return name.ToString();
    }
}
