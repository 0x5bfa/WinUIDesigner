// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Versioning;
using Microsoft.VisualStudio.DesignTools.Utility;
using WinUIDesigner.DesignerHost;
using WinUIDesigner.Platform;

[TestClass]
public sealed class PlatformSupportTests
{
    // The specification of Visual Studio's desktop WinUI configuration, which receives the host binding.
    private static readonly PlatformSpecification _DesktopWinUI = new(
        "TargetPlatformIdentifier=Windows;TargetPlatformVersion=10.0-..;TargetRuntime=Managed,Native;TargetFrameworkIdentifier=.NETCoreApp;TargetFrameworkVersion=5.0-..;XamlRuntime=WinUI");

    private static readonly FrameworkName _Net10 = new(".NETCoreApp", new Version(10, 0));

    // net10.0-windows10.0.26100 of a WinUI or Uno project.
    private static PlatformIdentifier WindowsTarget => new(
        new PlatformName("Windows", new Version(10, 0, 26100, 0), new Version(10, 0, 26100, 0)),
        "Managed",
        _Net10,
        new SdkName("Windows", new Version(10, 0)),
        XamlRuntimeNames.WinUI);

    // net10.0-android of an Uno project: Uno.Sdk sets DefaultXamlRuntime=WinUI for every target framework.
    private static PlatformIdentifier AndroidTarget => new(
        new PlatformName("Android", new Version(36, 0)),
        "Managed",
        _Net10,
        XamlRuntimeNames.WinUI);

    // net10.0 of an Uno project: no target platform at all.
    private static PlatformIdentifier PlatformNeutralTarget => new(null, "Managed", _Net10, XamlRuntimeNames.WinUI);

    [TestCleanup]
    public void Cleanup()
    {
        WinUIPlatformSupport.Reset();
    }

    [TestMethod]
    public void WindowsTargetOfWinUIProjectIsCovered()
    {
        Assert.IsTrue(WinUIPlatformSupport.Matches(WindowsTarget, _DesktopWinUI));
    }

    [TestMethod]
    public void NonWindowsTargetsReportingWinUIRuntimeAreNotCovered()
    {
        Assert.IsFalse(WinUIPlatformSupport.Matches(AndroidTarget, _DesktopWinUI), "Android target");
        Assert.IsFalse(WinUIPlatformSupport.Matches(PlatformNeutralTarget, _DesktopWinUI), "Platform-neutral target");
    }

    [TestMethod]
    public void OtherXamlRuntimesAreNotCovered()
    {
        var wpf = new PlatformIdentifier(new PlatformName("Windows", new Version(7, 0)), "Managed", _Net10, XamlRuntimeNames.WpfCore);
        var unspecified = new PlatformIdentifier(new PlatformName("Windows", new Version(10, 0, 26100, 0)), "Managed", _Net10, null);

        Assert.IsFalse(WinUIPlatformSupport.Matches(wpf, _DesktopWinUI), "WPF");
        Assert.IsFalse(WinUIPlatformSupport.Matches(unspecified, _DesktopWinUI), "No XAML runtime");
    }

    [TestMethod]
    public void NothingIsHostedBeforeConfigurationOrAfterReset()
    {
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));

        WinUIPlatformSupport.Configure(_DesktopWinUI);
        Assert.IsTrue(WinUIPlatformSupport.IsHosted(WindowsTarget));
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(AndroidTarget));
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(null));

        WinUIPlatformSupport.Reset();
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));
    }

    [TestMethod]
    public void HostBindingProbeCanVetoACoveredIdentity()
    {
        int probed = 0;
        WinUIPlatformSupport.Configure(_DesktopWinUI, _ =>
        {
            probed++;
            return false;
        });

        Assert.IsFalse(WinUIPlatformSupport.IsHosted(WindowsTarget));
        Assert.AreEqual(1, probed);

        // An identity the configuration does not cover never reaches the probe.
        Assert.IsFalse(WinUIPlatformSupport.IsHosted(AndroidTarget));
        Assert.AreEqual(1, probed);
    }

    [TestMethod]
    public void SupportedProjectHasNoUnsupportedReason()
    {
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, WinUIProjectRequirements.VerifiedWinUIRuntimeFileVersion));

        // A project without a Microsoft.WinUI.dll reference has nothing to verify.
        Assert.IsNull(WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, (string?)null));
    }

    [TestMethod]
    public void OtherWindowsAppSdkVersionIsExplained()
    {
        string? reason = WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, "3.0.0.2508");

        Assert.IsNotNull(reason);
        StringAssert.Contains(reason, WinUIProjectRequirements.VerifiedWindowsAppSdkVersion);
        StringAssert.Contains(reason, "3.0.0.2508");

        // A file without version information cannot be verified either.
        StringAssert.Contains(WinUIProjectRequirements.GetUnsupportedReason(WindowsTarget, string.Empty), "unknown version");
    }

    [TestMethod]
    public void NonManagedAndNonCoreProjectsAreExplained()
    {
        var native = new PlatformIdentifier(new PlatformName("Windows", new Version(10, 0, 26100, 0)), "Native", _Net10, XamlRuntimeNames.WinUI);
        var framework = new PlatformIdentifier(
            new PlatformName("Windows", new Version(10, 0, 26100, 0)),
            "Managed",
            new FrameworkName(".NETFramework", new Version(4, 8)),
            XamlRuntimeNames.WinUI);

        StringAssert.Contains(WinUIProjectRequirements.GetUnsupportedReason(native, (string?)null), "managed");
        StringAssert.Contains(WinUIProjectRequirements.GetUnsupportedReason(framework, (string?)null), ".NETCoreApp");
        StringAssert.Contains(WinUIProjectRequirements.GetUnsupportedReason(null, (string?)null), ".NETCoreApp");
    }

    [TestMethod]
    public void WinUIRuntimeReferenceIsFoundByFileName()
    {
        string?[] references =
        [
            null,
            @"C:\packages\microsoft.windowsappsdk.winui\2.5.1\lib\net8.0-windows10.0.19041.0\Microsoft.InteractiveExperiences.Projection.dll",
            @"C:\packages\microsoft.windowsappsdk.winui\2.5.1\lib\net8.0-windows10.0.19041.0\microsoft.winui.DLL",
        ];

        Assert.AreEqual(references[2], WinUIProjectRequirements.FindWinUIRuntimePath(references));
        Assert.IsNull(WinUIProjectRequirements.FindWinUIRuntimePath([references[1]]));
        Assert.IsNull(WinUIProjectRequirements.FindWinUIRuntimePath(null));
    }
}
