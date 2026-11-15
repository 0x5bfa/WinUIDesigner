// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Globalization;
using System.IO;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Interprets the value Visual Studio gives a resource dictionary in place of its <c>ResourceDictionary.Source</c>.
/// </summary>
/// <remarks>
/// Visual Studio's XAML cleaner (<c>UwpResourceDictionaryCleanerHandler</c>) and its construction actions
/// (<c>DesignerInstanceBuilderUtility.SetDesignTimeResourceDictionarySource</c>) both register the referenced
/// dictionary as a related document of the designer, write a prepared copy of it into the designer's shadow
/// cache and pass on the design-time URI of that copy, such as
/// <c>ms-appx:///Ecierge.Console.UI/~vs]~359530~Resources.xaml</c>. A dictionary Visual Studio does not find in
/// the solution, such as one compiled into a package, keeps its source as written or made absolute.
/// </remarks>
internal static class ResourceDictionarySourceUri
{
    /// <summary>
    /// The marker of a prepared copy: Visual Studio names it <c>~vs]~{project item id}~{file name}</c>
    /// (<c>SurfaceProcessMarkupProvider.CreateDesignTimeFileName</c>).
    /// </summary>
    public const string DesignTimeFileMarker = "~vs]~";

    /// <summary>
    /// The name Visual Studio serializes the design-time property under on the construction-action path,
    /// before the separator and the declaring type <c>XSurfUwp.DT</c>.
    /// </summary>
    public const string PropertyName = "ResourceDictionarySource";

    /// <summary>
    /// The declaring type of the design-time property, as Visual Studio's UWP instance builders serialize it.
    /// </summary>
    public const string PropertyOwnerTypeName = "XSurfUwp.DT";

    private const string _AppxScheme = "ms-appx";
    private const string _AppxRoot = "ms-appx:///";

    /// <summary>
    /// Returns whether <paramref name="value"/> names a prepared copy that Visual Studio wrote for the designer.
    /// </summary>
    public static bool IsDesignTimeUri(string? value)
    {
        return TryGetDesignTimeDocumentId(value, out _);
    }

    /// <summary>
    /// Reads the id of Visual Studio's document from a design-time URI, the way Visual Studio itself does:
    /// the digits between the last marker and the next <c>~</c>.
    /// </summary>
    public static bool TryGetDesignTimeDocumentId(string? value, out int documentId)
    {
        documentId = 0;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int start = value.LastIndexOf(DesignTimeFileMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        start += DesignTimeFileMarker.Length;
        int end = value.IndexOf('~', start);
        if (end <= start)
        {
            return false;
        }

        for (int index = start; index < end; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(value.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out documentId);
    }

    /// <summary>
    /// Returns the name a log message uses for a dictionary: the file name it has in the project.
    /// </summary>
    public static string GetDisplayName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string trimmed = value.Trim().TrimEnd('/');
        int slash = trimmed.LastIndexOfAny(['/', '\\']);
        string fileName = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;

        // "~vs]~{id}~{file name}" is how Visual Studio names a prepared copy.
        if (fileName.StartsWith(DesignTimeFileMarker, StringComparison.OrdinalIgnoreCase))
        {
            int end = fileName.IndexOf('~', DesignTimeFileMarker.Length);
            if (end >= 0 && end + 1 < fileName.Length)
            {
                fileName = fileName[(end + 1)..];
            }
        }

        return fileName.Length == 0 ? value : Uri.UnescapeDataString(fileName);
    }

    /// <summary>
    /// Makes a source absolute the way the WinUI runtime reads it.
    /// </summary>
    /// <param name="value">The value of the source as Visual Studio passed it on.</param>
    /// <param name="baseUri">
    /// The design-time URI of the document that merges the dictionary, or <see langword="null"/> when it is unknown.
    /// </param>
    /// <returns>
    /// An absolute URI, or <see langword="null"/> for an empty value and for a value that is no URI. An absolute URI
    /// is returned as written; <c>/Folder/File.xaml</c> is relative to the root of the package; anything else is
    /// relative to the folder of the document that merges the dictionary, or to the root when that is unknown.
    /// </returns>
    /// <remarks>
    /// Visual Studio makes every source absolute before it sends a construction action. A relative value only
    /// arrives in prepared XAML, and only for a dictionary Visual Studio did not find in the solution.
    /// </remarks>
    public static string? Resolve(string? value, string? baseUri)
    {
        string? text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] != '/' && Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute))
        {
            return absolute.OriginalString;
        }

        if (!Uri.TryCreate(text, UriKind.Relative, out Uri? relative))
        {
            return null;
        }

        Uri root = new(_AppxRoot);
        Uri reference = !string.IsNullOrEmpty(baseUri)
            && Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? owner)
            && string.Equals(owner.Scheme, _AppxScheme, StringComparison.OrdinalIgnoreCase)
                ? owner
                : root;

        // A rooted value is relative to the root of the package, whatever the document is.
        return Uri.TryCreate(text[0] == '/' ? root : reference, relative, out Uri? combined)
            ? combined.AbsoluteUri
            : null;
    }

    /// <summary>
    /// Returns the local file that holds the XAML an absolute URI addresses in the surface process.
    /// </summary>
    /// <param name="uri">An absolute URI.</param>
    /// <param name="contentDirectory">The folder that <c>ms-appx:///</c> stands for: Visual Studio's shadow cache.</param>
    /// <returns>
    /// The path below <paramref name="contentDirectory"/> for an <c>ms-appx</c> URI, where Visual Studio writes its
    /// prepared copies, or the path of a <c>file</c> URI; <see langword="null"/> for any other URI and for a path
    /// that would leave the content directory.
    /// </returns>
    public static string? TryGetLocalPath(string uri, string contentDirectory)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(contentDirectory);

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return null;
        }

        if (parsed.IsFile)
        {
            return parsed.LocalPath;
        }

        if (!string.Equals(parsed.Scheme, _AppxScheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The host of "ms-appx://name/..." names a package; the designer has one, as the surface reads documents.
        string relativePath = Uri.UnescapeDataString(parsed.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (relativePath.Length == 0)
        {
            return null;
        }

        try
        {
            string root = Path.GetFullPath(contentDirectory);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns whether a property name of a construction action names the design-time source of a dictionary,
    /// <c>ResourceDictionarySource:XSurfUwp.DT</c>.
    /// </summary>
    public static bool IsSourceProperty(string? fullPropertyName)
    {
        if (string.IsNullOrEmpty(fullPropertyName))
        {
            return false;
        }

        int separator = fullPropertyName.IndexOf(':');
        if (separator <= 0 || !fullPropertyName.AsSpan(0, separator).Trim().Equals(PropertyName, StringComparison.Ordinal))
        {
            return false;
        }

        // The declaring type may carry an assembly name after a comma.
        ReadOnlySpan<char> owner = fullPropertyName.AsSpan(separator + 1);
        int comma = owner.IndexOf(',');
        return (comma < 0 ? owner : owner[..comma]).Trim().Equals(PropertyOwnerTypeName, StringComparison.Ordinal);
    }
}
