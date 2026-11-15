// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Pairs a value that an action batch removed from a resource dictionary with the value it adds back under the same key.
/// </summary>
/// <remarks>
/// Visual Studio builds a new Style when the TargetType of a style changes or a setter is removed from an implicit
/// style, and replaces the dictionary entry by removing the key and adding the new instance. Objects that resolved
/// <c>{StaticResource}</c> to the old instance keep it; the pair tells the surface which references to move.
/// </remarks>
internal sealed class ReplacedValueTracker
{
    private readonly List<(object Dictionary, object Key, object Value)> _removed = [];
    private readonly List<(object Dictionary, object OldValue, object NewValue)> _replaced = [];

    public void Removed(object dictionary, object key, object value)
    {
        _removed.RemoveAll(entry => IsEntry(entry.Dictionary, entry.Key, dictionary, key));
        _removed.Add((dictionary, key, value));
    }

    public void Added(object dictionary, object key, object value)
    {
        int index = _removed.FindIndex(entry => IsEntry(entry.Dictionary, entry.Key, dictionary, key));
        if (index < 0)
        {
            return;
        }

        object oldValue = _removed[index].Value;
        _removed.RemoveAt(index);
        if (!ReferenceEquals(oldValue, value))
        {
            _replaced.Add((dictionary, oldValue, value));
        }
    }

    /// <summary>
    /// Returns the replacements recorded since the last call and forgets removals that were not followed by an addition.
    /// </summary>
    public List<(object Dictionary, object OldValue, object NewValue)> TakeReplacements()
    {
        var replaced = new List<(object Dictionary, object OldValue, object NewValue)>(_replaced);
        _replaced.Clear();
        _removed.Clear();
        return replaced;
    }

    // Keys are strings or types; both compare by value.
    private static bool IsEntry(object dictionary, object key, object otherDictionary, object otherKey)
    {
        return ReferenceEquals(dictionary, otherDictionary) && Equals(key, otherKey);
    }
}
