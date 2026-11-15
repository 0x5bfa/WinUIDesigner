// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.DesignTools.Utility.Clipboard;

namespace WinUIDesigner.Platform;

/// <summary>
/// The clipboard the designer reads through: a clipboard that cannot be read counts as an empty one.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio asks whether the clipboard holds something to paste whenever it refreshes the state of its
/// commands. The clipboard cannot be opened while another process holds it or while the session is locked;
/// the question then fails with <c>CLIPBRD_E_CANT_OPEN</c>, and Visual Studio handles that as an unhandled
/// designer exception: it shows the exception in place of the design surface and ends the surface process.
/// </para>
/// <para>
/// Writing is passed through unchanged, because Visual Studio reports a failed copy to the user itself.
/// </para>
/// </remarks>
internal sealed class GuardedClipboard : IClipboard
{
    /// <summary>
    /// How long <see cref="ContainsData"/> answers without asking after the clipboard could not be read.
    /// </summary>
    /// <remarks>
    /// A read that fails has already kept the UI thread waiting for about a second in the retries of WPF,
    /// and Visual Studio asks for several formats on every refresh.
    /// </remarks>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IClipboard _clipboard;
    private readonly Action<Exception?>? _availabilityChanged;
    private readonly Func<TimeSpan> _getTime;

    // The designer uses the clipboard on the UI thread only, so this state is not synchronized.
    private bool _isUnavailable;
    private TimeSpan _retryTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuardedClipboard"/> class.
    /// </summary>
    /// <param name="clipboard">The clipboard to read and write.</param>
    /// <param name="availabilityChanged">
    /// Called with the failure when the clipboard stops being readable, and with <see langword="null"/>
    /// when it is readable again.
    /// </param>
    /// <param name="getTime">A monotonic clock; when omitted, the time since this instance was created.</param>
    public GuardedClipboard(IClipboard clipboard, Action<Exception?>? availabilityChanged = null, Func<TimeSpan>? getTime = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _availabilityChanged = availabilityChanged;

        if (getTime is null)
        {
            var stopwatch = Stopwatch.StartNew();
            getTime = () => stopwatch.Elapsed;
        }

        _getTime = getTime;
    }

    public bool ContainsData(string format)
    {
        // This is the question Visual Studio repeats. The other reads happen when the user pastes,
        // so they always ask.
        if (_isUnavailable && _getTime() < _retryTime)
        {
            return false;
        }

        try
        {
            bool containsData = _clipboard.ContainsData(format);
            MarkAvailable();
            return containsData;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return false;
        }
    }

    public object? GetData(string format)
    {
        try
        {
            object? data = _clipboard.GetData(format);
            MarkAvailable();
            return data;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return null;
        }
    }

    public IDataObject GetDataObject()
    {
        try
        {
            IDataObject data = _clipboard.GetDataObject();
            MarkAvailable();
            return data;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);

            // Not null: some of Visual Studio's paste code uses the result without checking it.
            // With an object that holds no format, pasting ends with the "cannot paste" message.
            return new DataObject();
        }
    }

    public bool IsCurrent(IDataObject data)
    {
        try
        {
            bool isCurrent = _clipboard.IsCurrent(data);
            MarkAvailable();
            return isCurrent;
        }
        catch (Exception exception) when (IsClipboardFailure(exception))
        {
            MarkUnavailable(exception);
            return false;
        }
    }

    public void SetDataObject(object data)
    {
        _clipboard.SetDataObject(data);
    }

    public void SetDataObject(object data, bool copy)
    {
        _clipboard.SetDataObject(data, copy);
    }

    // The failures Visual Studio's own guarded read (SafeDataObject.FromClipboard) takes for an unreadable
    // clipboard. COMException, which carries CLIPBRD_E_CANT_OPEN, is an ExternalException.
    private static bool IsClipboardFailure(Exception exception)
    {
        return exception is ExternalException or UnauthorizedAccessException;
    }

    private void MarkAvailable()
    {
        if (_isUnavailable)
        {
            _isUnavailable = false;
            _availabilityChanged?.Invoke(null);
        }
    }

    private void MarkUnavailable(Exception exception)
    {
        _retryTime = _getTime() + RetryDelay;

        if (!_isUnavailable)
        {
            _isUnavailable = true;
            _availabilityChanged?.Invoke(exception);
        }
    }
}
