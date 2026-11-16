using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;

namespace WinUIDesignerSampleApp.Conditions;

/// <summary>
/// A custom XAML condition. A conditional namespace names it after the <c>?</c>, through the prefix of this
/// CLR namespace: <c>xmlns:newExperience="...presentation?cond:FeatureFlagCondition(NewExperience)"</c>.
/// </summary>
public sealed partial class FeatureFlagCondition : DependencyObject, IXamlCondition
{
    /// <summary>
    /// Gets the feature flags. An application sets them before its XAML is parsed; the designer does not
    /// run the application, so it sees the values assigned here.
    /// </summary>
    public static IDictionary<string, bool> FeatureFlags { get; } = new Dictionary<string, bool>
    {
        ["NewExperience"] = true,
        ["LegacyMode"] = false,
    };

    /// <summary>
    /// Returns whether the flag named by <paramref name="argument"/>, the text between the parentheses, is on.
    /// </summary>
    public bool Evaluate(string argument)
    {
        return FeatureFlags.TryGetValue(argument, out bool enabled) && enabled;
    }
}
