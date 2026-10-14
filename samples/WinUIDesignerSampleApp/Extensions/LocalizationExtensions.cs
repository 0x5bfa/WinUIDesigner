// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#nullable enable

namespace WinUIDesignerSampleApp.Extensions;

public static class LocalizationExtensions
{
    private static readonly global::Microsoft.Windows.ApplicationModel.Resources.ResourceMap? _Resources =
        new global::Microsoft.Windows.ApplicationModel.Resources.ResourceManager()
            .MainResourceMap
            .TryGetSubtree("Resources");
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, string>
        _LocalizedResources = new(global::System.StringComparer.Ordinal);

    public static string GetLocalized(this string resourceKey)
    {
        global::System.ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        if (_LocalizedResources.TryGetValue(resourceKey, out var value))
        {
            return value;
        }

        value = _Resources?.TryGetValue(resourceKey)?.ValueAsString ?? resourceKey;
        return _LocalizedResources.GetOrAdd(resourceKey, value);
    }

    public static void ClearLocalizedCache() => _LocalizedResources.Clear();
}
