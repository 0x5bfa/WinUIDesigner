// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace WinUIDesigner;

/// <summary>
/// Writes structured designer diagnostics to the process trace and log file.
/// </summary>
internal static class WinUIDesignerLogger
{
    private static readonly int _ProcessId = GetProcessId();
    private static readonly string _LogPath = CreateLogPath();
    private static readonly Mutex _LogMutex = new(false, $"Local\\WinUIDesignerLogger-{_ProcessId}");
    private static bool _isVerboseLoggingEnabled;

    public static bool IsVerboseLoggingEnabled => Volatile.Read(ref _isVerboseLoggingEnabled);

    public static string LogDirectoryPath => Path.GetDirectoryName(_LogPath)!;

    public static void ConfigureVerboseLogging(bool enabled)
    {
        Volatile.Write(ref _isVerboseLoggingEnabled, enabled);
    }

    public static void LogCritical(string category, string message, Exception? exception = null)
    {
        Write("Critical", category, message, exception);
    }

    public static void LogError(string category, string message, Exception? exception = null)
    {
        Write("Error", category, message, exception);
    }

    public static void LogInformation(string category, string message, Exception? exception = null)
    {
        Write("Information", category, message, exception);
    }

    public static void LogTrace(string category, string message, Exception? exception = null)
    {
        if (IsVerboseLoggingEnabled)
        {
            Write("Trace", category, message, exception);
        }
    }

    public static void LogTrace(string category, ref WinUIDesignerLogMessage message, Exception? exception = null)
    {
        if (message.ShouldAppend)
        {
            Write("Trace", category, message.GetFormattedMessage(), exception);
        }
    }

    public static void LogWarning(string category, string message, Exception? exception = null)
    {
        Write("Warning", category, message, exception);
    }

    private static void Write(string level, string category, string message, Exception? exception)
    {
        string entry = $"{DateTime.UtcNow:O} [{level}] [{category}] [AppDomain={AppDomain.CurrentDomain.FriendlyName}] {message}";
        if (exception is not null)
        {
            entry += Environment.NewLine + exception;
        }

        try
        {
            Trace.WriteLine($"[WinUIDesigner] {entry}");
        }
        catch (Exception)
        {
            // A diagnostic listener must not interfere with designer activation.
        }

        bool mutexAcquired = false;
        try
        {
            try
            {
                mutexAcquired = _LogMutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // The previous Visual Studio thread exited while writing a log entry.
                mutexAcquired = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_LogPath)!);
            File.AppendAllText(_LogPath, entry + Environment.NewLine);
        }
        catch (IOException)
        {
            // Diagnostics must not interfere with designer activation.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must not interfere with designer activation.
        }
        catch (Exception)
        {
            // Logging is best-effort and must not interfere with designer activation.
        }
        finally
        {
            if (mutexAcquired)
            {
                _LogMutex.ReleaseMutex();
            }
        }
    }

    private static string CreateLogPath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(basePath, "WinUIDesigner", "Logs", $"{timestamp}-{_ProcessId}.log");
    }

    private static int GetProcessId()
    {
        using Process process = Process.GetCurrentProcess();
        return process.Id;
    }
}

[InterpolatedStringHandler]
internal ref struct WinUIDesignerLogMessage
{
    private readonly StringBuilder? _message;

    public WinUIDesignerLogMessage(int literalLength, int formattedCount, out bool shouldAppend)
    {
        shouldAppend = WinUIDesignerLogger.IsVerboseLoggingEnabled;
        ShouldAppend = shouldAppend;
        _message = shouldAppend ? new StringBuilder(literalLength) : null;
    }

    public bool ShouldAppend { get; }

    public void AppendLiteral(string value)
    {
        _message?.Append(value);
    }

    public void AppendFormatted<T>(T value)
    {
        _message?.Append(value);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        if (_message is not null)
        {
            AppendFormattedValue(value, format);
        }
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
        if (_message is not null)
        {
            AppendAligned(AppendFormattedValue(value, null), alignment);
        }
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        if (_message is not null)
        {
            AppendAligned(AppendFormattedValue(value, format), alignment);
        }
    }

    public string GetFormattedMessage()
    {
        return _message?.ToString() ?? string.Empty;
    }

    private string AppendFormattedValue<T>(T value, string? format)
    {
        string formattedValue = value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.CurrentCulture) ?? string.Empty
            : value?.ToString() ?? string.Empty;

        _message!.Append(formattedValue);
        return formattedValue;
    }

    private void AppendAligned(string value, int alignment)
    {
        int padding = Math.Abs(alignment) - value.Length;
        if (padding <= 0)
        {
            _message!.Append(value);
        }
        else if (alignment < 0)
        {
            _message!.Append(value).Append(' ', padding);
        }
        else
        {
            _message!.Append(' ', padding).Append(value);
        }
    }
}
