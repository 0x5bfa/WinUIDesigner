// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies edits of styles in use, and of visual-state setters, through XAML Diagnostics.
/// </summary>
/// <remarks>
/// WinUI seals a Style, its Setters collection and its Setters as soon as an element uses the style, including a
/// detached element and a style that Visual Studio has only just created: its instance builders assign a new style
/// before they add the setters. The public API then rejects insertions (E_FAIL), Setter.Value and Setter.Property
/// changes (E_ACCESSDENIED) and BasedOn changes, and accepts removals without updating the elements.
/// <para>
/// Visual Studio's UWP designer meets the same sealing and does not avoid it: its StyleBuilder and SetterBuilder send
/// AddChild and RemoveChild for the setter collection of a style in use, and a changed setter is replaced by a new one.
/// Its TAP applies them through the framework's XAML Diagnostics, which unseals the objects for the edit, rebuilds
/// the merged setters of styles BasedOn the edited one, re-applies implicit styles and refreshes every element that
/// uses the style. Dictionary edits through XAML Diagnostics also re-resolve the <c>{StaticResource}</c> references
/// that the framework recorded for the key. VisualState setter collections are not sealed, but only XAML Diagnostics
/// applies an edit to the state that is active. The surface sends these edits through its own TAP; objects that are
/// not sealed keep the public API.
/// </para>
/// <para>
/// The framework records the elements that use a style only while it stores source information, so Program enables
/// that before the first style is applied; otherwise these calls succeed without updating anything. XAML Diagnostics
/// also keeps every object it is given alive until the process exits, so only edits that need it take this path.
/// </para>
/// </remarks>
internal sealed class StyleEditService(Action requestDiagnosticsConnection)
{
    private const string StyleSettersPropertyName = "Microsoft.UI.Xaml.Style.Setters";
    private const string VisualStateSettersPropertyName = "Microsoft.UI.Xaml.VisualState.Setters";
    private const string RefreshAdvice = "Refresh the designer to see the change.";
    private const string VisualStateFallback = "the setters of a visual state change in the preview when the state is entered again or the designer is refreshed.";
    private const string DictionaryFallback = "references to a rebuilt or moved style keep the old style until the designer is refreshed, except the Style of elements, which moves to a rebuilt style.";

    private readonly ConditionalWeakTable<SetterBaseCollection, DependencyObject> _setterCollectionOwners = new();
    private readonly ReplacedValueTracker _replacedValues = new();
    private readonly HashSet<object> _keysRemovedThroughDiagnostics = [];
    private readonly HashSet<string> _loggedProblems = [];
    private bool _nativeUnavailable;

    /// <summary>
    /// Remembers the Style or VisualState that owns a setter collection.
    /// </summary>
    /// <remarks>
    /// Visual Studio reaches a setter collection only through a GetProperty action on its owner. XAML Diagnostics
    /// lists the items of a collection only when it reaches the collection through that owner too.
    /// </remarks>
    public void TrackPropertyValue(object owner, object? value)
    {
        if (value is SetterBaseCollection collection && owner is Style or VisualState)
        {
            _setterCollectionOwners.AddOrUpdate(collection, (DependencyObject)owner);
        }
    }

    /// <summary>
    /// Inserts a complete setter. Returns <see langword="false"/> when the public API applies the edit.
    /// </summary>
    public bool TryInsertSetter(SetterBaseCollection collection, int index, SetterBase setter)
    {
        if (!TryGetDiagnosticsRoute(collection, out DependencyObject? owner, out string? collectionPropertyName))
        {
            return false;
        }

        int count = collection.Count;
        uint position = (uint)(index < 0 || index > count ? count : index);
        return Invoke("add a setter", collection.IsSealed ? null : VisualStateFallback, pointers => NativeMethods.WinUIDesignerDiagnostics_AddChild(pointers[0], collectionPropertyName, pointers[1], position), owner, setter);
    }

    /// <summary>
    /// Removes a setter. Returns <see langword="false"/> when the public API applies the edit.
    /// </summary>
    public bool TryRemoveSetter(SetterBaseCollection collection, int index)
    {
        if (index < 0
            || index >= collection.Count
            || !TryGetDiagnosticsRoute(collection, out DependencyObject? owner, out string? collectionPropertyName))
        {
            return false;
        }

        return Invoke("remove a setter", collection.IsSealed ? null : VisualStateFallback, pointers => NativeMethods.WinUIDesignerDiagnostics_RemoveChild(pointers[0], collectionPropertyName, (uint)index), owner);
    }

    /// <summary>
    /// Removes every setter of a setter collection. Returns <see langword="false"/> when the public API applies the edit.
    /// </summary>
    public bool TryClearSetters(object owner, object? collection)
    {
        if (collection is not SetterBaseCollection setters)
        {
            return false;
        }

        TrackPropertyValue(owner, setters);
        if (!TryGetDiagnosticsRoute(setters, out DependencyObject? knownOwner, out string? collectionPropertyName))
        {
            return false;
        }

        return Invoke("remove the setters", setters.IsSealed ? null : VisualStateFallback, pointers => NativeMethods.WinUIDesignerDiagnostics_ClearChildren(pointers[0], collectionPropertyName), knownOwner);
    }

    /// <summary>
    /// Sets a property of a sealed Setter or Style, such as Style.BasedOn. Returns <see langword="false"/> when the
    /// public API applies the edit.
    /// </summary>
    /// <remarks>
    /// A null value also stands in for clearing the property: clearing Style.BasedOn through XAML Diagnostics leaves
    /// the elements unchanged, while setting it to null updates them.
    /// </remarks>
    public bool TrySetProperty(object target, string fullPropertyName, object? value)
    {
        string? declaringType = target switch
        {
            Setter { IsSealed: true } => typeof(Setter).FullName,
            Style { IsSealed: true } => typeof(Style).FullName,
            _ => null,
        };
        if (declaringType is null)
        {
            return false;
        }

        string propertyName = XamlDiagnosticsPropertyName.FromActionProperty(fullPropertyName, declaringType);
        object? providedValue = XamlRuntimeUtilities.ProvideMarkupExtensionValue(value);
        return Invoke($"set {propertyName}", fallback: null, pointers => NativeMethods.WinUIDesignerDiagnostics_SetProperty(pointers[0], propertyName, pointers[1]), target, providedValue);
    }

    /// <summary>
    /// Removes a Style from a resource dictionary. Returns <see langword="false"/> when the public API applies the edit.
    /// </summary>
    /// <remarks>
    /// Visual Studio replaces a rebuilt style by removing its key and adding the new instance. Through XAML
    /// Diagnostics the removal detaches the <c>{StaticResource}</c> references that the XAML reader recorded, and
    /// the addition resolves them again, also in another dictionary when the style moved there.
    /// </remarks>
    public bool TryRemoveDictionaryEntry(object dictionary, object? key)
    {
        if (dictionary is not ResourceDictionary resources
            || key is null
            || !resources.TryGetValue(key, out object? value)
            || value is not Style style)
        {
            return false;
        }

        _replacedValues.Removed(resources, key, style);
        if (!Invoke("remove a style from its resource dictionary", DictionaryFallback, pointers => NativeMethods.WinUIDesignerDiagnostics_RemoveDictionaryItem(pointers[0], pointers[1]), resources, key))
        {
            return false;
        }

        _keysRemovedThroughDiagnostics.Add(key);
        return true;
    }

    /// <summary>
    /// Adds a Style under a key that was removed through XAML Diagnostics. Returns <see langword="false"/> when the
    /// public API applies the edit.
    /// </summary>
    /// <remarks>
    /// Only an addition through XAML Diagnostics resolves the references that a removal through it detached; after
    /// a public removal it resolves nothing. Other keys keep the public API, because XAML Diagnostics keeps every
    /// object it is given alive until the process exits.
    /// </remarks>
    public bool TryAddDictionaryEntry(object dictionary, object? key, object? value)
    {
        if (dictionary is not ResourceDictionary resources || key is null || value is not Style style)
        {
            return false;
        }

        _replacedValues.Added(resources, key, style);

        // XAML Diagnostics adds new keys only; the public indexer replaces an existing value.
        return _keysRemovedThroughDiagnostics.Contains(key)
            && !resources.ContainsKey(key)
            && Invoke("add a style to its resource dictionary", DictionaryFallback, pointers => NativeMethods.WinUIDesignerDiagnostics_AddDictionaryItem(pointers[0], pointers[1], pointers[2]), resources, key, style);
    }

    /// <summary>
    /// Replaces a dictionary value when the old or the new value is a Style. Returns <see langword="false"/> when
    /// the public API applies the edit.
    /// </summary>
    public bool TryReplaceDictionaryEntry(object dictionary, object? key, object? value)
    {
        if (dictionary is not ResourceDictionary resources
            || key is null
            || !resources.TryGetValue(key, out object? oldValue)
            || (oldValue is not Style && value is not Style))
        {
            return false;
        }

        if (oldValue is Style && !TryRemoveDictionaryEntry(resources, key))
        {
            XamlRuntimeUtilities.RemoveDictionaryEntry(resources, key);
        }

        if (!TryAddDictionaryEntry(resources, key, value))
        {
            XamlRuntimeUtilities.SetDictionaryEntry(resources, key, value);
        }

        return true;
    }

    /// <summary>
    /// Moves the references to styles that the action batch replaced in a resource dictionary to the new instances.
    /// </summary>
    /// <remarks>
    /// XAML Diagnostics re-resolves the <c>{StaticResource}</c> references that the XAML reader resolved, but the
    /// surface resolves those of construction actions itself, so the framework does not know them; without
    /// XAML Diagnostics nothing is re-resolved. This moves the Style of every element in <paramref name="roots"/>, and
    /// BasedOn of the styles in the same dictionary and in the application resources, that still refer to a replaced
    /// style. Visual Studio addresses the new style through its own new handles, so no handle has to move.
    /// </remarks>
    public void CompleteBatch(IEnumerable<FrameworkElement> roots)
    {
        List<(object Dictionary, object OldValue, object NewValue)> replacements = _replacedValues.TakeReplacements();
        if (replacements.Count == 0)
        {
            return;
        }

        var rootList = new List<FrameworkElement>(roots);
        foreach ((object dictionary, object oldValue, object newValue) in replacements)
        {
            if (oldValue is not Style oldStyle || newValue is not Style newStyle)
            {
                continue;
            }

            foreach (FrameworkElement root in rootList)
            {
                ReplaceElementStyles(root, oldStyle, newStyle);
            }

            var visited = new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance);
            ReplaceBasedOn((ResourceDictionary)dictionary, oldStyle, newStyle, visited);
            ReplaceBasedOn(Application.Current.Resources, oldStyle, newStyle, visited);
        }
    }

    // Edits go through XAML Diagnostics when the collection is sealed, or when it belongs to a VisualState: only
    // XAML Diagnostics updates the active state.
    private bool TryGetDiagnosticsRoute(
        SetterBaseCollection collection,
        [NotNullWhen(true)] out DependencyObject? owner,
        [NotNullWhen(true)] out string? collectionPropertyName)
    {
        collectionPropertyName = null;
        if (!_setterCollectionOwners.TryGetValue(collection, out owner))
        {
            if (collection.IsSealed)
            {
                throw Report(
                    "unknown-owner",
                    $"The setters belong to a style in use, which WinUI does not allow to change, and the surface does not know the style. {RefreshAdvice}");
            }

            return false;
        }

        if (owner is VisualState)
        {
            collectionPropertyName = VisualStateSettersPropertyName;
            return true;
        }

        collectionPropertyName = StyleSettersPropertyName;
        return collection.IsSealed;
    }

    // A null fallback marks an edit that only XAML Diagnostics can apply: it throws when that fails. Otherwise
    // the edit falls back to the public API and the note says, once, what the preview misses.
    private bool Invoke(string operation, string? fallback, Func<IntPtr[], int> call, params object?[] objects)
    {
        int hr;
        try
        {
            hr = _nativeUnavailable ? NativeMethods.E_PENDING : CallWithAbiPointers(call, objects);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _nativeUnavailable = true;
            WinUIDesignerLogger.LogTrace("Surface", $"XAML Diagnostics edits are unavailable: {ex}");
            hr = NativeMethods.E_PENDING;
        }

        if (hr == NativeMethods.S_OK)
        {
            WinUIDesignerLogger.LogTrace("Surface", $"Applied through XAML Diagnostics: {operation}.");
            return true;
        }

        if (hr == NativeMethods.E_PENDING)
        {
            requestDiagnosticsConnection();
            if (fallback is not null)
            {
                LogOnce($"unavailable:{fallback}", $"XAML Diagnostics is not connected: {fallback}");
                return false;
            }

            throw Report(
                "unavailable",
                $"Could not {operation}: WinUI does not allow changing a style that is in use, and the XAML Diagnostics connection that applies such edits is not available. {RefreshAdvice}");
        }

        if (fallback is not null)
        {
            LogOnce($"failed:{operation}:{hr}", $"Could not {operation} through XAML Diagnostics (0x{hr:X8}): {fallback}");
            return false;
        }

        throw Report($"failed:{operation}:{hr}", $"Could not {operation}: XAML Diagnostics returned 0x{hr:X8}. {RefreshAdvice}");
    }

    private static int CallWithAbiPointers(Func<IntPtr[], int> call, object?[] objects)
    {
        // The native diagnostics API takes the original WinRT IInspectable of every object.
        var pointers = new IntPtr[objects.Length];
        try
        {
            for (int index = 0; index < objects.Length; index++)
            {
                pointers[index] = objects[index] is { } value ? WinRT.MarshalInspectable<object>.FromManaged(value, true) : IntPtr.Zero;
            }

            return call(pointers);
        }
        finally
        {
            foreach (IntPtr pointer in pointers)
            {
                if (pointer != IntPtr.Zero)
                {
                    WinRT.MarshalInspectable<object>.DisposeAbi(pointer);
                }
            }
        }
    }

    private StyleEditException Report(string problem, string message)
    {
        LogOnce(problem, message);
        return new StyleEditException(message);
    }

    private void LogOnce(string problem, string message)
    {
        if (_loggedProblems.Add(problem))
        {
            WinUIDesignerLogger.LogWarning("Surface", message);
        }
        else
        {
            WinUIDesignerLogger.LogTrace("Surface", message);
        }
    }

    private static void ReplaceElementStyles(DependencyObject element, Style oldStyle, Style newStyle)
    {
        if (element is FrameworkElement frameworkElement
            && ReferenceEquals(frameworkElement.ReadLocalValue(FrameworkElement.StyleProperty), oldStyle))
        {
            frameworkElement.Style = newStyle;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int index = 0; index < childCount; index++)
        {
            ReplaceElementStyles(VisualTreeHelper.GetChild(element, index), oldStyle, newStyle);
        }
    }

    private void ReplaceBasedOn(ResourceDictionary dictionary, Style oldStyle, Style newStyle, HashSet<ResourceDictionary> visited)
    {
        // Enumerating a dictionary creates its deferred resources; the framework's control styles hold thousands
        // and never refer to a project style.
        if (dictionary is XamlControlsResources || !visited.Add(dictionary))
        {
            return;
        }

        foreach (KeyValuePair<object, object> entry in dictionary)
        {
            if (entry.Value is not Style style || !ReferenceEquals(style.BasedOn, oldStyle))
            {
                continue;
            }

            try
            {
                if (!TrySetProperty(style, nameof(Style.BasedOn), newStyle))
                {
                    style.BasedOn = newStyle;
                }
            }
            catch (StyleEditException)
            {
                // Already logged once; the remaining references can still move.
            }
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            ReplaceBasedOn(merged, oldStyle, newStyle, visited);
        }
    }
}
