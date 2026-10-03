// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Diagnostics;
using System.IO;

namespace WinUIDesigner.Vsix.Toolbox;

internal static class ToolboxTrace
{
    private static readonly string TracePath = CreateTracePath();

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TracePath)!);
            File.AppendAllText(
                TracePath,
                $"{DateTime.UtcNow:O} [AppDomain={AppDomain.CurrentDomain.FriendlyName}] {message}\r\n");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateTracePath()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        return Path.Combine(basePath, "WinUIDesigner", "Logs", $"Toolbox-{Process.GetCurrentProcess().Id}.log");
    }
}
