// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;

/// <summary>
/// Covers what the store keeps in a project: the user's override, including the conditions the user forces, on
/// top of the MSBuild properties, and what it does when the project fails or does not keep a value.
/// </summary>
[TestClass]
public sealed class DesignTimeOptionsStoreTests
{
    private const string _C7 = "IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";
    private const string _Hostile = "using:My.App|FeatureFlagCondition( A;B=C,D%E \"quoted\" é (x) )";

    [TestMethod]
    public void ForcedConditionsAreStoredAndLoadedBack()
    {
        var project = new FakeProject();
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty.With(_C7, holds: false).With(_Hostile, holds: true);
        DesignTimeOptions options = DesignTimeOptions.Default.WithConditionOverrides(overrides);

        Assert.IsTrue(DesignTimeOptionsSettings.Save(project, options, project.Warn));

        StringAssert.StartsWith(project.UserOverride, "v=1;cond=");
        DesignTimeOptions loaded = DesignTimeOptionsSettings.Load(project, project.Warn, out string source);
        Assert.AreEqual(options, loaded);
        Assert.AreEqual(overrides, loaded.ConditionOverrides);
        StringAssert.Contains(source, "read from project 'App'");
        StringAssert.Contains(source, project.UserOverride!);
        Assert.AreEqual(0, project.Warnings.Count, string.Join(Environment.NewLine, project.Warnings));
    }

    [TestMethod]
    public void ForcedConditionsDoNotHideWhatTheProjectFileSays()
    {
        var project = new FakeProject { TargetPlatform = "android", WindowsVersion = "1809" };
        DesignTimeOptions options = DesignTimeOptionsSettings.Load(project, project.Warn, out _)
            .WithConditionOverrides(XamlConditionOverrides.Empty.With(_C7, holds: true));

        Assert.IsTrue(DesignTimeOptionsSettings.Save(project, options, project.Warn));

        // Only the conditions differ from the project file, so the target and the version still follow it.
        Assert.AreEqual("v=1;cond=IsApiContractPresent(Windows.Foundation.UniversalApiContract%2C7)=1", project.UserOverride);
        project.TargetPlatform = "ios";
        DesignTimeOptions loaded = DesignTimeOptionsSettings.Load(project, project.Warn, out _);
        Assert.AreEqual("ios", loaded.TargetPlatformId);
        Assert.AreEqual(7, loaded.ContractVersion);
        Assert.AreEqual(options.ConditionOverrides, loaded.ConditionOverrides);
    }

    [TestMethod]
    public void ResettingEveryConditionRemovesTheStoredOverride()
    {
        var project = new FakeProject();
        DesignTimeOptions forced = DesignTimeOptions.Default.WithConditionOverrides(XamlConditionOverrides.Empty.With(_C7, holds: true));
        Assert.IsTrue(DesignTimeOptionsSettings.Save(project, forced, project.Warn));

        Assert.IsTrue(DesignTimeOptionsSettings.Save(project, forced.WithConditionOverrides(XamlConditionOverrides.Empty), project.Warn));

        // Removing the override is a write of null.
        Assert.IsNull(project.UserOverride);
        Assert.AreEqual(2, project.WriteCount);
        Assert.AreEqual(DesignTimeOptions.Default, DesignTimeOptionsSettings.Load(project, project.Warn, out _));
    }

    [TestMethod]
    public void ValueVisualStudioDoesNotKeepIsReported()
    {
        var project = new FakeProject { KeepsValues = false };

        bool saved = DesignTimeOptionsSettings.Save(project, DesignTimeOptions.Default.WithConditionOverrides(XamlConditionOverrides.Empty.With(_C7, true)), project.Warn);

        Assert.IsFalse(saved);
        StringAssert.Contains(project.Warnings.Single(), "did not keep");
    }

    [TestMethod]
    public void ProjectThatFailsLeavesTheDefaults()
    {
        var project = new FakeProject { Fails = true };

        DesignTimeOptions loaded = DesignTimeOptionsSettings.Load(project, project.Warn, out string source);
        bool saved = DesignTimeOptionsSettings.Save(project, DesignTimeOptions.Default.WithConditionOverrides(XamlConditionOverrides.Empty.With(_C7, true)), project.Warn);

        Assert.AreEqual(DesignTimeOptions.Default, loaded);
        StringAssert.Contains(source, "not set");
        Assert.IsFalse(saved);
        Assert.IsTrue(project.Warnings.Count >= 4, string.Join(Environment.NewLine, project.Warnings));
        Assert.IsTrue(project.Warnings.All(warning => warning.Contains("InvalidOperationException")), string.Join(Environment.NewLine, project.Warnings));
    }

    [TestMethod]
    public void NoProjectMeansTheDefaults()
    {
        var warnings = new List<string>();

        DesignTimeOptions loaded = DesignTimeOptionsSettings.Load(null, (message, _) => warnings.Add(message), out string source);

        Assert.AreEqual(DesignTimeOptions.Default, loaded);
        Assert.AreEqual("no project to read them from", source);
        Assert.AreEqual(0, warnings.Count);
    }

    [TestMethod]
    public void UnreadableValuesAreReportedAndIgnored()
    {
        var project = new FakeProject
        {
            UserOverride = "v=1;target=ios;cond=" + XamlConditionOverrides.Empty.With(_C7, false).Format() + ",broken%ZZ=1",
            TargetPlatform = "playstation",
            WindowsVersion = "Windows 95",
        };

        DesignTimeOptions loaded = DesignTimeOptionsSettings.Load(project, project.Warn, out _);

        Assert.AreEqual("ios", loaded.TargetPlatformId);
        Assert.IsNull(loaded.ContractVersion);
        CollectionAssert.AreEqual(new[] { _C7 }, loaded.ConditionOverrides.Keys.ToArray(), "The readable conditions still apply.");
        Assert.AreEqual(3, project.Warnings.Count, string.Join(Environment.NewLine, project.Warnings));
        StringAssert.Contains(project.Warnings[0], "cannot be read");
        StringAssert.Contains(project.Warnings[1], "playstation");
        StringAssert.Contains(project.Warnings[2], "Windows 95");

        project.Warnings.Clear();
        project.UserOverride = "garbage";
        Assert.AreEqual("win", DesignTimeOptionsSettings.Load(project, project.Warn, out _).TargetPlatformId);
        StringAssert.Contains(project.Warnings[0], "not understood");
    }

    /// <summary>
    /// A project that keeps its settings in memory, as Visual Studio keeps them in the solution's user options.
    /// </summary>
    private sealed class FakeProject : IDesignTimeOptionsProject
    {
        public string ProjectName => "App";

        public string? UserOverride { get; set; }

        public string? TargetPlatform { get; set; }

        public string? WindowsVersion { get; set; }

        /// <summary>Gets or sets a value indicating whether a written value can be read back.</summary>
        public bool KeepsValues { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether every call throws.</summary>
        public bool Fails { get; set; }

        public int WriteCount { get; private set; }

        public List<string> Warnings { get; } = [];

        public void Warn(string message, Exception? exception)
        {
            Warnings.Add(exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");
        }

        public string? ReadUserOverride()
        {
            ThrowIfFailing();
            return UserOverride;
        }

        public void WriteUserOverride(string? userOverride)
        {
            ThrowIfFailing();
            WriteCount++;
            if (KeepsValues)
            {
                UserOverride = userOverride;
            }
        }

        public string? ReadBuildProperty(string propertyName)
        {
            ThrowIfFailing();
            return propertyName switch
            {
                DesignTimeOptionsResolver.TargetPlatformPropertyName => TargetPlatform,
                DesignTimeOptionsResolver.WindowsVersionPropertyName => WindowsVersion,
                _ => null,
            };
        }

        private void ThrowIfFailing()
        {
            if (Fails)
            {
                throw new InvalidOperationException("the project is unloaded");
            }
        }
    }
}
