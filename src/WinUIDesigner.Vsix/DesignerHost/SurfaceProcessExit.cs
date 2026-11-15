// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Globalization;

namespace WinUIDesigner.DesignerHost;

/// <summary>
/// Tells how a WinUISurface.exe process ended, so that the log reports a failed surface as an error and a surface
/// that Visual Studio ended on purpose as information.
/// </summary>
internal static class SurfaceProcessExit
{
    /// <summary>
    /// The exit code of a process that <c>Process.Kill</c> ended. Visual Studio ends the surface process this way
    /// whenever it replaces or closes the design surface: when it reloads the designer (after a build, for
    /// example), and when the project closes. The surface itself never exits with this code.
    /// </summary>
    public const int KilledExitCode = -1;

    /// <summary>
    /// Whether the exit code is the one <c>Process.Kill</c> leaves, which is how Visual Studio ends a surface it
    /// no longer needs.
    /// </summary>
    public static bool IsKilled(int exitCode)
    {
        return exitCode == KilledExitCode;
    }

    /// <summary>
    /// Whether the exit code means that the surface failed: anything but a normal exit or an end by
    /// <c>Process.Kill</c>.
    /// </summary>
    public static bool IsFailure(int exitCode)
    {
        return exitCode != 0 && !IsKilled(exitCode);
    }

    /// <summary>
    /// Formats an exit code for the log, together with the hexadecimal form under which a failure code is
    /// documented, such as <c>0xC0000005</c> (an access violation) or <c>0xE0434352</c> (an unhandled .NET
    /// exception).
    /// </summary>
    public static string FormatExitCode(int exitCode)
    {
        return exitCode.ToString(CultureInfo.InvariantCulture) + " (0x" + exitCode.ToString("X8", CultureInfo.InvariantCulture) + ")";
    }
}
