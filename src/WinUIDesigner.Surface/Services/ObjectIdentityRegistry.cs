// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
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
    private int currentDocument;
    private readonly Dictionary<int, HashSet<object>> documents = new();
    private readonly Dictionary<object, HashSet<int>> owners = new(ReferenceEqualityComparer.Instance);

    public IDisposable EnterDocument(int documentId)
    {
        int previous = currentDocument;
        currentDocument = documentId;
        return new DocumentScope(() => currentDocument = previous);
    }

    public int GetDocumentId(object value)
    {
        return owners.TryGetValue(value, out var ids) ? ids.FirstOrDefault() : 0;
    }

    public void Track(object value)
    {
        if (currentDocument == 0) return;
        if (!documents.TryGetValue(currentDocument, out var objects))
            documents[currentDocument] = objects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        objects.Add(value);
        if (!owners.TryGetValue(value, out var ids))
            owners[value] = ids = new HashSet<int>();
        ids.Add(currentDocument);
    }

    public HashSet<object> ReleaseDocument(int documentId)
    {
        var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!documents.Remove(documentId, out var objects)) return released;
        foreach (object value in objects)
        {
            if (!owners.TryGetValue(value, out var ids)) continue;
            ids.Remove(documentId);
            if (ids.Count != 0) continue;
            owners.Remove(value);
            RemoveObject(value);
            released.Add(value);
        }
        return released;
    }

    public void Clear()
    {
        objectToHandle.Clear(); handleToObject.Clear(); sourceInfo.Clear();
        documents.Clear(); owners.Clear(); currentDocument = 0;
        // Never reuse a handle that the frontend may still have cached.
    }

    private sealed class DocumentScope(Action restore) : IDisposable
    {
        private Action? restoreAction = restore;
        public void Dispose() { var action = restoreAction; restoreAction = null; action?.Invoke(); }
    }

    public long GetHandle(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Track(value);

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
        Track(value);
        if (handle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }

        if (handleToObject.TryGetValue(handle, out object? oldValue) && !ReferenceEquals(oldValue, value))
        {
            RemoveObject(oldValue);
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
        Track(value);
    }

    public void RegisterMarkupInfo(object value, long markupHandle, int changeVersion)
    {
        ArgumentNullException.ThrowIfNull(value);
        Track(value);
        sourceInfo[value] = new SourceInfo
        {
            MarkupHandle = markupHandle,
            ChangeVersion = changeVersion,
        };
    }

    public SourceInfo? GetSourceInfo(object value)
    {
        return sourceInfo.TryGetValue(value, out SourceInfo? info) ? CloneSourceInfo(info) : null;
    }

    public bool TryGetObject(long handle, out object? value)
    {
        return handleToObject.TryGetValue(handle, out value);
    }

    public bool TryGetHandle(object value, out long handle)
    {
        return objectToHandle.TryGetValue(value, out handle);
    }

    public void RemoveHandle(long handle)
    {
        if (handleToObject.TryGetValue(handle, out object? value)) RemoveObject(value);
    }

    public void RemoveObject(object value)
    {
        if (objectToHandle.Remove(value, out long handle))
        {
            handleToObject.Remove(handle);
        }

        sourceInfo.Remove(value);
        if (owners.Remove(value, out var ids))
            foreach (int id in ids)
                if (documents.TryGetValue(id, out var objects)) objects.Remove(value);
    }

    private static SourceInfo CloneSourceInfo(SourceInfo info)
    {
        return new()
        {
            FileName = info.FileName,
            LineNumber = info.LineNumber,
            ColumnNumber = info.ColumnNumber,
            MarkupHandle = info.MarkupHandle,
            ChangeVersion = info.ChangeVersion,
        };
    }
}
