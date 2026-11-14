// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;
using WinUIDesigner.Protocol;

/// <summary>
/// Covers how the Visual Studio side obtains and uses the surface's answers to conditional XAML conditions:
/// what it returns before the surface answered, while a request is in flight, when an answer arrives between
/// two documents, and when the surface never answers.
/// </summary>
[TestClass]
public sealed class XamlConditionAnswersTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const string _C7 = "IsApiContractPresent(" + _Contract + ",7)";
    private const string _C7N = "IsApiContractNotPresent(" + _Contract + ",7)";
    private const string _C99 = "IsApiContractPresent(" + _Contract + ",99)";
    private const string _NewExperience = "cond:FeatureFlagCondition(NewExperience)";
    private const string _LegacyMode = "cond:FeatureFlagCondition(LegacyMode)";
    private const string _ColorPicker = "IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)";

    private static readonly TimeSpan _Long = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task NothingIsEvaluatedBeforeTheSurfaceAnswers()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();

        // Not even what this process could tell by itself: the surface loads prepared XAML as written until it answers.
        Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        Assert.IsNull(answers.Evaluate(_NewExperience, Namespaces));
        Assert.IsNull(answers.Evaluate("IsTypePresent(", Namespaces));
        Assert.IsFalse(answers.SurfaceAnswers);

        // The question is on its way all the same.
        Assert.AreEqual(1, surface.RequestCount);
        CollectionAssert.AreEqual(new[] { _C7 }, surface.Requests[0].Conditions);

        surface.Hold = false;
        surface.Release();
        await answers.AskAsync(Queries(_C7, _NewExperience), _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(false, answers.Evaluate("IsTypePresent(", Namespaces));
        Assert.AreEqual(2, surface.RequestCount);
        Assert.IsTrue(surface.ChangeCount >= 1);
    }

    [TestMethod]
    public async Task AskedConditionsAreSentInOneRequestAndTheirAnswersAreKept()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers();
        List<XamlConditionQuery> queries = Queries(_C7, _C99, _NewExperience, _LegacyMode, _ColorPicker, "IsTypePresent(", string.Empty);

        await answers.AskAsync(queries, _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(1, surface.RequestCount);

        // A malformed condition and an empty one need no surface.
        CollectionAssert.AreEquivalent(
            new[] { _C7 + "|", _C99 + "|", _NewExperience + "|using:My.App", _LegacyMode + "|using:My.App", _ColorPicker + "|" },
            surface.Requests[0].Conditions!.Zip(surface.Requests[0].PredicateNamespaces!, (condition, predicateNamespace) => condition + "|" + predicateNamespace).ToArray());

        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C99, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_LegacyMode, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_ColorPicker, Namespaces));
        Assert.AreEqual(false, answers.Evaluate("IsTypePresent(", Namespaces));

        // Asking again, and using the answers, sends nothing.
        await answers.AskAsync(queries, _Long);
        Assert.AreEqual(1, surface.RequestCount);
        Assert.AreEqual(1, surface.ChangeCount);

        // The malformed condition was reported when it was evaluated; nothing else was.
        Assert.AreEqual(1, surface.Warnings.Count);
        StringAssert.Contains(surface.Warnings[0], "IsTypePresent(");
    }

    [TestMethod]
    public async Task AskingReturnsAfterTheTimeoutAndTheLateAnswerStillCounts()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();
        var elapsed = Stopwatch.StartNew();

        await answers.AskAsync(Queries(_C7, _NewExperience), TimeSpan.FromMilliseconds(100));

        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), elapsed.Elapsed.ToString());
        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(0, surface.ChangeCount);
        Assert.AreEqual(1, surface.Warnings.Count);
        StringAssert.Contains(surface.Warnings[0], "did not answer");

        // Asking the same again does not send it again; it waits for the request in flight.
        await answers.AskAsync(Queries(_C7), TimeSpan.FromMilliseconds(50));
        Assert.AreEqual(1, surface.RequestCount);

        surface.Release();
        await answers.AskAsync(Queries(_C7), _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);
    }

    [TestMethod]
    public async Task ConditionMetLaterIsAnsweredFromThisProcessUntilTheSurfaceAnswers()
    {
        // This process says contract 7 is present; the surface, which is the authority, says it is not.
        var surface = new FakeSurface { ContractAnswer = major => major <= 5 };
        XamlConditionAnswers answers = surface.CreateAnswers(new XamlConditionProbeSet { IsApiContractPresent = (_, major, _) => major <= 10 });
        await WaitForTheFirstAnswer(answers, surface);

        surface.Hold = true;
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);

        // The contradicting answer arrives while a document may be being built: it waits for the next one.
        surface.Release();
        await WaitFor(() => !answers.IsRequestInFlight);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);

        await answers.AskAsync(Queries(_C7), _Long);

        Assert.AreEqual(false, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_C7N, Namespaces));
        Assert.AreEqual(2, surface.ChangeCount);
    }

    [TestMethod]
    public async Task LateAnswerTakesEffectWithTheNextDocument()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();

        await answers.AskAsync(Queries(_C7, _NewExperience), TimeSpan.FromMilliseconds(50));
        Assert.IsNull(answers.Evaluate(_C7, Namespaces));

        // The reply arrives after the document was sent, possibly while its actions are being built.
        surface.Release();
        await WaitFor(() => !answers.IsRequestInFlight);
        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        Assert.IsNull(answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(0, surface.ChangeCount);

        // The next document, even one without conditions, starts with the answers.
        await answers.AskAsync([], _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);
        Assert.AreEqual(1, surface.RequestCount);
    }

    [TestMethod]
    public async Task FirstAnswerIsAwaitedLonger()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();

        // A surface that has just started answers once it is ready.
        Task first = answers.AskAsync(Queries(_C7), TimeSpan.FromMilliseconds(50), _Long);
        await Task.Delay(500);
        Assert.IsFalse(first.IsCompleted, "The first answer is waited for longer than the usual time.");

        surface.Release();
        await first;
        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(0, surface.Warnings.Count);

        // Once the surface has answered, the usual time applies.
        var elapsed = Stopwatch.StartNew();
        await answers.AskAsync(Queries(_C99), TimeSpan.FromMilliseconds(50), _Long);
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5), elapsed.Elapsed.ToString());
        Assert.AreEqual(1, surface.Warnings.Count);
        StringAssert.Contains(surface.Warnings[0], "did not answer");
    }

    [TestMethod]
    public async Task SurfaceThatNeverAnswersCostsTheLongWaitOnce()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();
        TimeSpan firstAnswerTimeout = TimeSpan.FromMilliseconds(800);

        var elapsed = Stopwatch.StartNew();
        await answers.AskAsync(Queries(_C7), TimeSpan.FromMilliseconds(50), firstAnswerTimeout);
        Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromMilliseconds(700), elapsed.Elapsed.ToString());

        elapsed.Restart();
        await answers.AskAsync(Queries(_C99), TimeSpan.FromMilliseconds(50), firstAnswerTimeout);
        await answers.AskAsync(Queries(_NewExperience), TimeSpan.FromMilliseconds(50), firstAnswerTimeout);
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(700), elapsed.Elapsed.ToString());

        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.AreEqual(1, surface.RequestCount);
        Assert.AreEqual(3, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task ConfirmationOfTheOwnAnswerChangesNothing()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers(new XamlConditionProbeSet { IsApiContractPresent = (_, major, _) => major <= 10 });
        await WaitForTheFirstAnswer(answers, surface);

        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        await answers.AskAsync(Queries(_C7), _Long);

        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);
        Assert.AreEqual(2, surface.RequestCount);
    }

    [TestMethod]
    public async Task CustomConditionMetLaterCountsAsNotSatisfiedUntilTheSurfaceAnswers()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers();
        await WaitForTheFirstAnswer(answers, surface);

        surface.Hold = true;
        Assert.AreEqual(false, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_LegacyMode, Namespaces));
        Assert.AreEqual(1, surface.ChangeCount);

        surface.Hold = false;
        surface.Release();
        await answers.AskAsync(Queries(_NewExperience, _LegacyMode), _Long);

        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_LegacyMode, Namespaces));
        Assert.IsTrue(surface.ChangeCount > 1);
    }

    [TestMethod]
    public async Task TypeConditionOutsideTheOperatingSystemStaysOpenUntilTheSurfaceAnswers()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers(new XamlConditionProbeSet { IsWindowsTypePresent = name => name == "Windows.Foundation.Uri" });
        await WaitForTheFirstAnswer(answers, surface);

        surface.Hold = true;

        // A type of the operating system is the same in both processes; any other type only the surface can see.
        Assert.AreEqual(true, answers.Evaluate("IsTypePresent(Windows.Foundation.Uri)", Namespaces));
        Assert.AreEqual(true, answers.Evaluate("IsTypeNotPresent(Windows.Foundation.NoSuchType)", Namespaces));
        Assert.IsNull(answers.Evaluate(_ColorPicker, Namespaces));

        surface.Hold = false;
        surface.Release();
        await answers.AskAsync(Queries(_ColorPicker), _Long);

        Assert.AreEqual(true, answers.Evaluate(_ColorPicker, Namespaces));
    }

    [TestMethod]
    public async Task EmptyQuestionShowsWhetherTheSurfaceAnswersAtAll()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();

        // A document without conditions does not wait, and a second one does not ask again.
        await answers.AskAsync([], _Long);
        await answers.AskAsync([], _Long);
        Assert.AreEqual(1, surface.RequestCount);

        surface.Hold = false;
        surface.Release();
        await WaitFor(() => !answers.IsRequestInFlight);
        Assert.IsFalse(answers.SurfaceAnswers);

        // The reply takes effect with the next document.
        await answers.AskAsync([], _Long);
        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(1, surface.ChangeCount);

        await answers.AskAsync([], _Long);
        Assert.AreEqual(1, surface.RequestCount);
        Assert.AreEqual(0, surface.Requests[0].Conditions!.Count);
        Assert.AreEqual(0, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task ConditionsDecidedByCodeBothProcessesShareAreNeverSent()
    {
        var surface = new FakeSurface();
        XamlConditionAnswers answers = surface.CreateAnswers();
        await WaitForTheFirstAnswer(answers, surface);

        // Malformed, an undeclared prefix, a namespace that names no CLR namespace, and no condition at all.
        Assert.AreEqual(false, answers.Evaluate("IsApiContractPresent(X,abc)", Namespaces));
        Assert.AreEqual(false, answers.Evaluate("nope:Flag(On)", Namespaces));
        Assert.AreEqual(false, answers.Evaluate("uri:Flag(On)", Namespaces));
        Assert.AreEqual(true, answers.Evaluate(string.Empty, Namespaces));
        await answers.AskAsync(Queries("IsApiContractPresent(X,abc)", string.Empty), _Long);

        Assert.AreEqual(1, surface.RequestCount);
        Assert.AreEqual(3, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task QuestionsThatComeUpWhileARequestIsInFlightFollowInOneRequest()
    {
        var surface = new FakeSurface { Hold = true };
        XamlConditionAnswers answers = surface.CreateAnswers();

        for (int index = 0; index < 100; index++)
        {
            answers.Evaluate(_C7, Namespaces);
        }

        answers.Evaluate(_C99, Namespaces);
        answers.Evaluate(_NewExperience, Namespaces);
        answers.Evaluate(_C99, Namespaces);
        Assert.AreEqual(1, surface.RequestCount);

        surface.Release();
        await WaitFor(() => surface.RequestCount == 2);
        CollectionAssert.AreEquivalent(new[] { _C99, _NewExperience }, surface.Requests[1].Conditions);

        surface.Hold = false;
        surface.Release();
        await answers.AskAsync(Queries(_C7, _C99, _NewExperience), _Long);

        Assert.AreEqual(2, surface.RequestCount);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate(_C99, Namespaces));
        Assert.AreEqual(true, answers.Evaluate(_NewExperience, Namespaces));
    }

    [TestMethod]
    public async Task SurfaceWhoseRepliesFailIsNotAskedWithoutEnd()
    {
        var surface = new FakeSurface
        {
            Reply = _ => Task.FromException<EvaluateXamlConditionsResponse>(new InvalidOperationException("the reply could not be read")),
        };
        XamlConditionAnswers answers = surface.CreateAnswers();

        // Each call asks again what the failed request could not answer, until asking is given up.
        for (int index = 0; index < 10; index++)
        {
            await answers.AskAsync(Queries(_C7, _C99), _Long);
            Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        }

        Assert.AreEqual(3, surface.RequestCount);
        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.AreEqual(0, surface.ChangeCount);
        Assert.AreEqual(3, surface.Warnings.Count);
        Assert.IsTrue(surface.Warnings.All(warning => warning.Contains("InvalidOperationException", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ReplyThatReportsAnErrorOrDoesNotMatchIsNotUsed()
    {
        var replies = new Queue<EvaluateXamlConditionsResponse>(
        [
            new EvaluateXamlConditionsResponse { Outcomes = [], Error = "the surface is busy" },
            new EvaluateXamlConditionsResponse { Outcomes = [true, true, true] },
        ]);
        var surface = new FakeSurface();
        surface.Reply = request => Task.FromResult(replies.Count != 0 ? replies.Dequeue() : surface.Answer(request));
        XamlConditionAnswers answers = surface.CreateAnswers();

        await answers.AskAsync(Queries(_C7), _Long);
        Assert.IsFalse(answers.SurfaceAnswers);
        await answers.AskAsync(Queries(_C7), _Long);
        Assert.IsFalse(answers.SurfaceAnswers);

        // The third reply is a proper one.
        await answers.AskAsync(Queries(_C7), _Long);
        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(3, surface.RequestCount);

        Assert.AreEqual(2, surface.Warnings.Count);
        StringAssert.Contains(surface.Warnings[0], "the surface is busy");
        StringAssert.Contains(surface.Warnings[1], "does not answer");
    }

    [TestMethod]
    public async Task SenderThatThrowsDoesNotBreakTheCaller()
    {
        var surface = new FakeSurface { Reply = _ => throw new InvalidOperationException("the pipe is gone") };
        XamlConditionAnswers answers = surface.CreateAnswers();

        Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        await answers.AskAsync(Queries(_C7, _C99), _Long);
        await answers.AskAsync([], _Long);
        await answers.AskAsync(Queries(_C7, _C99), _Long);

        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.AreEqual(3, surface.RequestCount);
        Assert.AreEqual(3, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task ClosedConnectionIsNotAsked()
    {
        var surface = new FakeSurface();
        using var closed = new CancellationTokenSource();
        closed.Cancel();
        XamlConditionAnswers answers = surface.CreateAnswers(connectionClosed: closed.Token);

        Assert.IsNull(answers.Evaluate(_C7, Namespaces));
        await answers.AskAsync(Queries(_C7), _Long);
        await answers.AskAsync([], _Long);

        Assert.AreEqual(0, surface.RequestCount);
        Assert.AreEqual(0, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task CanceledRequestIsAFailureWithoutNoise()
    {
        var surface = new FakeSurface { Reply = _ => Task.FromCanceled<EvaluateXamlConditionsResponse>(new CancellationToken(canceled: true)) };
        XamlConditionAnswers answers = surface.CreateAnswers();

        await answers.AskAsync(Queries(_C7), _Long);

        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.AreEqual(1, surface.RequestCount);
        Assert.AreEqual(0, surface.Warnings.Count);
    }

    [TestMethod]
    public async Task FailingLogAndFailingListenerDoNotBreakTheAnswers()
    {
        var surface = new FakeSurface();
        var answers = new XamlConditionAnswers(
            request => Task.FromResult(surface.Answer(request)),
            CancellationToken.None,
            () => throw new InvalidOperationException("the listener failed"),
            new XamlConditionProbeSet(),
            (_, _) => throw new InvalidOperationException("the log is closed"));

        await answers.AskAsync(Queries(_C7, "IsTypePresent("), _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(_C7, Namespaces));
        Assert.AreEqual(false, answers.Evaluate("IsTypePresent(", Namespaces));
    }

    [TestMethod]
    public void OperatingSystemIsAskedThroughTheLocalProbes()
    {
        XamlConditionProbeSet probes = XamlConditionAnswers.CreateLocalProbes();

        // The tests run on Windows 10 or later, where the Windows Runtime class is available.
        Assert.IsNotNull(probes.IsApiContractPresent);
        Assert.IsNotNull(probes.IsWindowsTypePresent);
        Assert.IsNotNull(probes.IsWindowsPropertyPresent);
        Assert.IsTrue(probes.IsApiContractPresent(_Contract, 1, null));
        Assert.IsTrue(probes.IsApiContractPresent(_Contract, 1, 0));
        Assert.IsFalse(probes.IsApiContractPresent(_Contract, ushort.MaxValue, null));
        Assert.IsFalse(probes.IsApiContractPresent("No.Such.Contract", 1, null));
        Assert.IsTrue(probes.IsWindowsTypePresent("Windows.Foundation.Uri"));
        Assert.IsFalse(probes.IsWindowsTypePresent("Windows.Foundation.NoSuchType"));
        Assert.IsTrue(probes.IsWindowsPropertyPresent("Windows.Foundation.Uri", "Host"));
        Assert.IsFalse(probes.IsWindowsPropertyPresent("Windows.Foundation.Uri", "NoSuchProperty"));

        // What only the surface can see has no probe here.
        Assert.IsNull(probes.IsManagedTypePresent);
        Assert.IsNull(probes.IsManagedPropertyPresent);
        Assert.IsNull(probes.EvaluateCustom);
    }

    private static string? Namespaces(string prefix)
    {
        return prefix switch
        {
            "cond" => "using:My.App",
            "uri" => "http://example.org/conditions",
            _ => null,
        };
    }

    private static List<XamlConditionQuery> Queries(params string[] conditions)
    {
        return conditions.Select(condition => XamlConditionQuery.Create(condition, Namespaces)).ToList();
    }

    /// <summary>
    /// Lets the surface answer the empty question and puts the answer into effect, after which conditions are evaluated.
    /// </summary>
    private static async Task WaitForTheFirstAnswer(XamlConditionAnswers answers, FakeSurface surface)
    {
        await answers.AskAsync([], _Long);
        await WaitFor(() => !answers.IsRequestInFlight);
        await answers.AskAsync([], _Long);
        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(1, surface.ChangeCount);
        Assert.AreEqual(1, surface.RequestCount);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsTrue(elapsed.Elapsed < _Long, "The expected state was not reached in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Stands in for the surface process: answers requests with a condition service of its own and can
    /// hold its replies back.
    /// </summary>
    private sealed class FakeSurface
    {
        private readonly object _gate = new();
        private readonly List<EvaluateXamlConditionsRequest> _requests = [];
        private readonly List<string> _warnings = [];
        private readonly List<(TaskCompletionSource<EvaluateXamlConditionsResponse> Completion, EvaluateXamlConditionsRequest Request)> _held = [];
        private readonly XamlConditionService _conditions;
        private int _changeCount;

        public FakeSurface()
        {
            _conditions = new XamlConditionService(new XamlConditionProbeSet
            {
                IsApiContractPresent = (_, major, _) => ContractAnswer(major),
                IsWindowsTypePresent = name => name == "Windows.Foundation.Uri",
                IsWindowsPropertyPresent = (_, _) => false,
                IsManagedTypePresent = name => name == "Microsoft.UI.Xaml.Controls.ColorPicker",
                IsManagedPropertyPresent = (_, _) => false,
                EvaluateCustom = (type, argument) => type == "My.App.FeatureFlagCondition" ? argument == "NewExperience" : null,
            });
        }

        /// <summary>Gets or sets what the surface says about a contract version.</summary>
        public Func<int, bool> ContractAnswer { get; set; } = major => major <= 10;

        /// <summary>Gets or sets a value indicating whether replies wait for <see cref="Release"/>.</summary>
        public bool Hold { get; set; }

        /// <summary>Gets or sets a replacement for the whole reply.</summary>
        public Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>>? Reply { get; set; }

        /// <summary>Gets how often the answers were reported as changed.</summary>
        public int ChangeCount => Volatile.Read(ref _changeCount);

        public int RequestCount
        {
            get
            {
                lock (_gate)
                {
                    return _requests.Count;
                }
            }
        }

        public List<EvaluateXamlConditionsRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        /// <summary>Gets the warnings the answers logged, with the type and message of their exception.</summary>
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

        public XamlConditionAnswers CreateAnswers(XamlConditionProbeSet? localProbes = null, CancellationToken connectionClosed = default)
        {
            return new XamlConditionAnswers(
                Ask,
                connectionClosed,
                () => Interlocked.Increment(ref _changeCount),
                localProbes ?? new XamlConditionProbeSet(),
                (message, exception) =>
                {
                    lock (_gate)
                    {
                        _warnings.Add(exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");
                    }
                });
        }

        public EvaluateXamlConditionsResponse Answer(EvaluateXamlConditionsRequest request)
        {
            return XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), _conditions);
        }

        /// <summary>Sends the replies that were held back.</summary>
        public void Release()
        {
            (TaskCompletionSource<EvaluateXamlConditionsResponse> Completion, EvaluateXamlConditionsRequest Request)[] held;
            lock (_gate)
            {
                held = [.. _held];
                _held.Clear();
            }

            foreach ((TaskCompletionSource<EvaluateXamlConditionsResponse> completion, EvaluateXamlConditionsRequest request) in held)
            {
                completion.TrySetResult(Answer(request));
            }
        }

        private Task<EvaluateXamlConditionsResponse> Ask(EvaluateXamlConditionsRequest request)
        {
            bool hold;
            var completion = new TaskCompletionSource<EvaluateXamlConditionsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _requests.Add(request);
                hold = Hold && Reply is null;
                if (hold)
                {
                    _held.Add((completion, request));
                }
            }

            if (Reply is not null)
            {
                return Reply(request);
            }

            if (!hold)
            {
                completion.SetResult(Answer(request));
            }

            return completion.Task;
        }
    }
}
