// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Services;

// Maintain the stable object<->handle identity expected by VS's LiveObject cache.
// Reference identity matters: equal values from distinct runtime objects stay distinct.
internal sealed class ObjectIdentityRegistry
{
    private readonly Dictionary<object, long> objectToHandle = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<long, object> handleToObject = new();
    private readonly Dictionary<object, SourceInfo> sourceInfo = new(ReferenceEqualityComparer.Instance);
    private long nextHandle = 1;

    public long GetHandle(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (objectToHandle.TryGetValue(value, out long handle))
        {
            return handle;
        }

        while (handleToObject.ContainsKey(nextHandle))
        {
            nextHandle++;
        }

        handle = nextHandle++;
        objectToHandle.Add(value, handle);
        handleToObject.Add(handle, value);
        return handle;
    }

    public void RegisterHandle(long handle, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }

        if (handleToObject.TryGetValue(handle, out object? oldValue) && !ReferenceEquals(oldValue, value))
        {
            objectToHandle.Remove(oldValue);
            sourceInfo.Remove(oldValue);
        }

        if (objectToHandle.TryGetValue(value, out long oldHandle) && oldHandle != handle)
        {
            handleToObject.Remove(oldHandle);
        }

        objectToHandle[value] = handle;
        handleToObject[handle] = value;
        if (handle >= nextHandle)
        {
            nextHandle = handle + 1;
        }
    }

    public void RegisterSourceInfo(object value, SourceInfo info)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(info);
        sourceInfo[value] = CloneSourceInfo(info);
    }

    public void RegisterMarkupInfo(object value, long markupHandle, int changeVersion)
    {
        ArgumentNullException.ThrowIfNull(value);
        sourceInfo[value] = new SourceInfo
        {
            MarkupHandle = markupHandle,
            ChangeVersion = changeVersion,
        };
    }

    public SourceInfo? GetSourceInfo(object value)
        => sourceInfo.TryGetValue(value, out SourceInfo? info) ? CloneSourceInfo(info) : null;

    public bool TryGetObject(long handle, out object? value)
        => handleToObject.TryGetValue(handle, out value);

    public bool TryGetHandle(object value, out long handle)
        => objectToHandle.TryGetValue(value, out handle);

    public void RemoveHandle(long handle)
    {
        if (handleToObject.Remove(handle, out object? value))
        {
            objectToHandle.Remove(value);
            sourceInfo.Remove(value);
        }
    }

    public void RemoveObject(object value)
    {
        if (objectToHandle.Remove(value, out long handle))
        {
            handleToObject.Remove(handle);
        }

        sourceInfo.Remove(value);
    }

    private static SourceInfo CloneSourceInfo(SourceInfo info)
        => new()
        {
            FileName = info.FileName,
            LineNumber = info.LineNumber,
            ColumnNumber = info.ColumnNumber,
            MarkupHandle = info.MarkupHandle,
            ChangeVersion = info.ChangeVersion,
        };
}
