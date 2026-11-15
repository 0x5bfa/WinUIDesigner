// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.DesignerHost;

[TestClass]
public sealed class SurfaceProcessExitTests
{
    [TestMethod]
    public void NormalExitIsNoFailure()
    {
        Assert.IsFalse(SurfaceProcessExit.IsFailure(0));
        Assert.IsFalse(SurfaceProcessExit.IsKilled(0));
    }

    [TestMethod]
    public void EndByProcessKillIsNoFailure()
    {
        // Visual Studio ends the surface it reloads or closes with Process.Kill, which leaves exit code -1.
        Assert.IsTrue(SurfaceProcessExit.IsKilled(-1));
        Assert.IsFalse(SurfaceProcessExit.IsFailure(-1));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(unchecked((int)0xC0000005))] // Access violation.
    [DataRow(unchecked((int)0xC0000409))] // Fail fast.
    [DataRow(unchecked((int)0xE0434352))] // Unhandled .NET exception.
    public void OtherExitCodesAreFailures(int exitCode)
    {
        Assert.IsTrue(SurfaceProcessExit.IsFailure(exitCode));
        Assert.IsFalse(SurfaceProcessExit.IsKilled(exitCode));
    }

    [TestMethod]
    public void ExitCodeIsFormattedWithItsHexadecimalForm()
    {
        Assert.AreEqual("-1073741819 (0xC0000005)", SurfaceProcessExit.FormatExitCode(unchecked((int)0xC0000005)));
        Assert.AreEqual("1 (0x00000001)", SurfaceProcessExit.FormatExitCode(1));
    }
}
