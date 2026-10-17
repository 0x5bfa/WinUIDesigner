// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Maintains the stable object-to-handle identity expected by the Visual Studio LiveObject cache.
/// Uses reference identity so distinct runtime objects remain distinct even when they compare equal.
/// </summary>
internal sealed partial class ObjectIdentityRegistry
{
    private readonly Dictionary<object, long> _objectToHandle = [with(ReferenceEqualityComparer.Instance)];

    private readonly Dictionary<long, object> _handleToObject = [];

    private readonly Dictionary<object, SourceInfo> _sourceInfo = [with(ReferenceEqualityComparer.Instance)];

    private long _nextHandle = 1;

    private int _currentDocument;

    private readonly Dictionary<int, HashSet<object>> _documents = [];

    private readonly Dictionary<object, HashSet<int>> _owners = [with(ReferenceEqualityComparer.Instance)];

    public IDisposable EnterDocument(int documentId)
    {
        int previous = _currentDocument;
        _currentDocument = documentId;

        return new DocumentScope(() => _currentDocument = previous);
    }

    public int GetDocumentId(object value)
    {
        return _owners.TryGetValue(value, out var ids) ? ids.FirstOrDefault() : 0;
    }

    public void Track(object value)
    {
        if (_currentDocument == 0)
        {
            return;
        }

        if (!_documents.TryGetValue(_currentDocument, out var objects))
        {
            _documents[_currentDocument] = objects = [with(ReferenceEqualityComparer.Instance)];
        }

        objects.Add(value);

        if (!_owners.TryGetValue(value, out var ids))
        {
            _owners[value] = ids = [];
        }

        ids.Add(_currentDocument);
    }

    public HashSet<object> ReleaseDocument(int documentId)
    {
        var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!_documents.Remove(documentId, out var objects))
        {
            return released;
        }

        foreach (object value in objects)
        {
            if (!_owners.TryGetValue(value, out var ids))
            {
                continue;
            }

            ids.Remove(documentId);
            if (ids.Count != 0)
            {
                continue;
            }

            _owners.Remove(value);
            RemoveObject(value);
            released.Add(value);
        }

        return released;
    }

    public void Clear()
    {
        _objectToHandle.Clear();
        _handleToObject.Clear();
        _sourceInfo.Clear();

        _documents.Clear();
        _owners.Clear();
        _currentDocument = 0;
        // Never reuse a handle that the frontend may still have cached.

    }

    public long GetHandle(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Track(value);

        if (_objectToHandle.TryGetValue(value, out long handle))
        {
            return handle;
        }

        while (_handleToObject.ContainsKey(_nextHandle))
        {
            _nextHandle++;
        }

        handle = _nextHandle++;
        _objectToHandle.Add(value, handle);
        _handleToObject.Add(handle, value);

        return handle;
    }

    public void RegisterHandle(long handle, object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Track(value);

        ArgumentOutOfRangeException.ThrowIfZero(handle);

        if (_handleToObject.TryGetValue(handle, out object? oldValue) && !ReferenceEquals(oldValue, value))
        {
            RemoveObject(oldValue);
        }

        if (_objectToHandle.TryGetValue(value, out long oldHandle) && oldHandle != handle)
        {
            _handleToObject.Remove(oldHandle);
        }

        _objectToHandle[value] = handle;
        _handleToObject[handle] = value;

        if (handle >= _nextHandle)
        {
            _nextHandle = handle + 1;
        }
    }

    public void RegisterSourceInfo(object value, SourceInfo info)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(info);
        _sourceInfo[value] = new SourceInfo
        {
            FileName = info.FileName,
            LineNumber = info.LineNumber,
            ColumnNumber = info.ColumnNumber,
            MarkupHandle = info.MarkupHandle,
            ChangeVersion = info.ChangeVersion
        };
        Track(value);
    }

    public void RegisterMarkupInfo(object value, long markupHandle, int changeVersion)
    {
        ArgumentNullException.ThrowIfNull(value);
        Track(value);
        _sourceInfo[value] = new SourceInfo
        {
            MarkupHandle = markupHandle,
            ChangeVersion = changeVersion,
        };
    }

    public SourceInfo? GetSourceInfo(object value)
    {
        return _sourceInfo.TryGetValue(value, out SourceInfo? info) ? new SourceInfo
        {
            FileName = info.FileName,
            LineNumber = info.LineNumber,
            ColumnNumber = info.ColumnNumber,
            MarkupHandle = info.MarkupHandle,
            ChangeVersion = info.ChangeVersion
        }

        : null;
    }

    public bool TryGetObject(long handle, out object? value)
    {
        return _handleToObject.TryGetValue(handle, out value);
    }

    public bool TryGetHandle(object value, out long handle)
    {
        return _objectToHandle.TryGetValue(value, out handle);
    }

    public void RemoveHandle(long handle)
    {
        if (_handleToObject.TryGetValue(handle, out object? value))
        {
            RemoveObject(value);
        }
    }

    public void RemoveObject(object value)
    {
        if (_objectToHandle.Remove(value, out long handle))
        {
            _handleToObject.Remove(handle);
        }

        _sourceInfo.Remove(value);

        if (_owners.Remove(value, out var ids))
        {
            foreach (int id in ids)
            {
                if (_documents.TryGetValue(id, out var objects))
                {
                    objects.Remove(value);
                }
            }
        }
    }
}
