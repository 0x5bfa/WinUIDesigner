// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WinUIDesigner.Surface;

/// <summary>
/// Writes structured designer diagnostics to the process trace and shared log file.
/// </summary>
internal static class WinUIDesignerLogger
{
    private static readonly int ProcessId = GetProcessId();
    private static readonly string LogPath = CreateLogPath();
    private static readonly Mutex LogMutex = new(false, $"Local\\WinUIDesignerLogger-{ProcessId}");

    public static void LogCritical(string category, string message, Exception? exception = null)
    {
        Write("Critical", category, message, exception);
    }

    public static void LogDebug(string category, string message, Exception? exception = null)
    {
        Write("Debug", category, message, exception);
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
        Write("Trace", category, message, exception);
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
            // A diagnostic listener must not interfere with designer activation.
            Trace.WriteLine($"[WinUIDesigner] {entry}");
        }
        catch (Exception)
        {
        }

        bool mutexAcquired = false;
        try
        {
            try
            {
                mutexAcquired = LogMutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // The previous Visual Studio thread exited while writing a log entry.
                mutexAcquired = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, entry + Environment.NewLine);
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
                LogMutex.ReleaseMutex();
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

        return Path.Combine(basePath, "WinUIDesigner", "Logs", $"WinUIDesigner-{ProcessId}.log");
    }

    private static int GetProcessId()
    {
        using Process process = Process.GetCurrentProcess();
        return process.Id;
    }
}
