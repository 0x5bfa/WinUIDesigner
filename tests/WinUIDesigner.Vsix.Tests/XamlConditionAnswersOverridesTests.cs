// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;
using WinUIDesigner.Protocol;

/// <summary>
/// Covers the answers the user forces on the Visual Studio side: they decide before the surface's answers, also
/// before the surface has answered at all, and they take effect only between documents, like the answers.
/// </summary>
[TestClass]
public sealed class XamlConditionAnswersOverridesTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = "IsApiContractPresent(" + _Contract + ",7)";
    private const string _C7N = "IsApiContractNotPresent(" + _Contract + ",7)";
    private const string _C99 = "IsApiContractPresent(" + _Contract + ",99)";
    private const string _NewExperience = "cond:FeatureFlagCondition(NewExperience)";
    private const string _LegacyMode = "cond:FeatureFlagCondition(LegacyMode)";

    private static readonly TimeSpan _Long = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ForcedAnswerAppliesBeforeTheSurfaceAnswers()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers(() => XamlConditionOverrides.Empty.With(Key(_C7), holds: false).With(Key(_NewExperience), holds: true));

        await answers.AskAsync(Queries(_C7, _C99, _NewExperience), TimeSpan.FromMilliseconds(50));

        // The surface rewrites prepared XAML with the same overrides, which it receives with the document.
        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));

        // Everything else waits for the surface as before.
        Assert.IsNull(answers.Evaluate(_C99, Namespaces));
        Assert.IsNull(answers.Evaluate(_LegacyMode, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount, "The overrides took effect.");
    }

    [TestMethod]
    public async Task ForcedAnswerWinsOverTheSurfacesAnswer()
    {
        var surface = new FakeSurface();
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty
            .With(Key(_C7), holds: false)
            .With(Key(_NewExperience), holds: false)
            .With(Key(_LegacyMode), holds: true);
        XamlConditionAnswers answers = surface.CreateAnswers(() => overrides);

        await answers.AskAsync(Queries(_C7, _C99, _NewExperience, _LegacyMode), _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_LegacyMode, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C99, Namespaces), "A question that is not overridden has the surface's answer.");
        Assert.AreEqual(overrides, answers.Overrides);
    }

    [TestMethod]
    public async Task OverridesTakeEffectOnlyBetweenDocuments()
    {
        var surface = new FakeSurface();
        XamlConditionOverrides overrides = XamlConditionOverrides.Empty;
        XamlConditionAnswers answers = surface.CreateAnswers(() => overrides);
        await answers.AskAsync(Queries(_C7), _Long);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        int changes = surface.ChangeCount;

        // The user forces the answer while a document may be being built: nothing changes until the next document.
        overrides = overrides.With(Key(_C7), holds: false);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(changes, surface.ChangeCount);

        await answers.AskAsync([], _Long);
        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(changes + 1, surface.ChangeCount, "Verdicts reached with the old overrides are discarded.");

        // The same overrides again change nothing; removing them brings back the surface's answer.
        await answers.AskAsync([], _Long);
        Assert.AreEqual(changes + 1, surface.ChangeCount);
        overrides = XamlConditionOverrides.Empty;
        await answers.AskAsync([], _Long);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(changes + 2, surface.ChangeCount);
    }

    [TestMethod]
    public async Task MalformedConditionIsNotForced()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers(() => XamlConditionOverrides.Empty.With(Key("IsTypePresent("), holds: true));

        await answers.AskAsync(Queries(_C7), _Long);

        Assert.AreEqual(false, answers.Evaluate("IsTypePresent(", Namespaces));
    }

    [TestMethod]
    public async Task EvaluationWithoutOverridesShowsWhatTheSurfaceAnswered()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers(() => XamlConditionOverrides.Empty.With(Key(_C7), holds: false).With(Key(_NewExperience), holds: false));

        // Nothing is known before the surface answers.
        await answers.AskAsync(Queries(_C7, _NewExperience, _LegacyMode), TimeSpan.FromMilliseconds(50));
        Assert.IsNull(answers.EvaluateIgnoringOverrides(Query(_C7)));

        surface.Hold = false;
        surface.Release();
        await answers.AskAsync(Queries(_C7, _NewExperience, _LegacyMode), _Long);

        Assert.AreEqual(true, answers.EvaluateIgnoringOverrides(Query(_C7)));
        Assert.AreEqual(false, answers.EvaluateIgnoringOverrides(Query(_C7N)));
        Assert.AreEqual(true, answers.EvaluateIgnoringOverrides(Query(_NewExperience)));
        Assert.AreEqual(false, answers.EvaluateIgnoringOverrides(Query(_LegacyMode)));

        // A custom condition the surface was never asked about is not known; asking for it here sends nothing.
        int requests = surface.RequestCount;
        Assert.IsNull(answers.EvaluateIgnoringOverrides(Query("cond:Other(X)")));
        Assert.AreEqual(requests, surface.RequestCount);

        // The preview still uses the overrides.
        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_NewExperience, Namespaces));
    }

    [TestMethod]
    public async Task FailingOverrideSourceKeepsTheOverridesInEffect()
    {
        var surface = new FakeSurface();
        int reads = 0;
        XamlConditionAnswers answers = surface.CreateAnswers(() =>
        {
            if (++reads > 1)
            {
                throw new InvalidOperationException("the project is unloading");
            }

            return XamlConditionOverrides.Empty.With(Key(_C7), holds: false);
        });

        await answers.AskAsync(Queries(_C7), _Long);

        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.IsTrue(surface.Warnings.Count >= 1);
        Assert.IsTrue(surface.Warnings.All(warning => warning.Contains("InvalidOperationException", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task WithoutAnOverrideSourceNothingIsForced()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers(readOverrides: null);

        await answers.AskAsync(Queries(_C7), _Long);

        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.IsTrue(answers.Overrides.IsEmpty);
    }

    private static string? Namespaces(string prefix)
    {
        return prefix == "cond" ? "using:My.App" : null;
    }

    private static XamlConditionQuery Query(string condition)
    {
        return XamlConditionQuery.Create(condition, Namespaces);
    }

    private static string Key(string condition)
    {
        return Query(condition).Key;
    }

    private static List<XamlConditionQuery> Queries(params string[] conditions)
    {
        return conditions.Select(Query).ToList();
    }

    /// <summary>
    /// Stands in for the surface process: contract versions up to 10, the feature flag NewExperience on.
    /// </summary>
    private sealed class FakeSurface
    {
        private readonly object _gate = new();
        private readonly List<string> _warnings = [];
        private readonly List<TaskCompletionSource<EvaluateXamlConditionsResponse>> _held = [];
        private readonly List<EvaluateXamlConditionsRequest> _heldRequests = [];
        private readonly XamlConditionService _conditions = new(new XamlConditionProbeSet
        {
            IsApiContractPresent = (_, major, _) => major <= 10,
            EvaluateCustom = (type, argument) => type == "My.App.FeatureFlagCondition" ? argument == "NewExperience" : null,
        });

        private int _changeCount;
        private int _requestCount;

        public bool Hold { get; set; }

        public int ChangeCount => Volatile.Read(ref _changeCount);

        public int RequestCount => Volatile.Read(ref _requestCount);

        public List<string> Warnings
        {
            get
            {
                lock (_gate)
                {
                    return [.. _warnings];
                }
            }
        }

        public XamlConditionAnswers CreateAnswers(Func<XamlConditionOverrides>? readOverrides)
        {
            return new XamlConditionAnswers(
                Ask,
                CancellationToken.None,
                () => Interlocked.Increment(ref _changeCount),
                new XamlConditionProbeSet(),
                (message, exception) =>
                {
                    lock (_gate)
                    {
                        _warnings.Add(exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");
                    }
                },
                readOverrides);
        }

        public void Release()
        {
            TaskCompletionSource<EvaluateXamlConditionsResponse>[] held;
            EvaluateXamlConditionsRequest[] requests;
            lock (_gate)
            {
                held = [.. _held];
                requests = [.. _heldRequests];
                _held.Clear();
                _heldRequests.Clear();
            }

            for (int index = 0; index < held.Length; index++)
            {
                held[index].TrySetResult(Answer(requests[index]));
            }
        }

        private EvaluateXamlConditionsResponse Answer(EvaluateXamlConditionsRequest request)
        {
            return XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), _conditions);
        }

        private Task<EvaluateXamlConditionsResponse> Ask(EvaluateXamlConditionsRequest request)
        {
            Interlocked.Increment(ref _requestCount);
            var completion = new TaskCompletionSource<EvaluateXamlConditionsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (Hold)
                {
                    _held.Add(completion);
                    _heldRequests.Add(request);
                    return completion.Task;
                }
            }

            completion.SetResult(Answer(request));
            return completion.Task;
        }
    }
}
