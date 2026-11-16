// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Concurrent;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;
using WinUIDesigner.Protocol;

/// <summary>
/// Sends the extension's own message through Visual Studio's protocol handler, the class both designer
/// processes talk through. Two handlers are connected back to back in this process, one in the role of
/// Visual Studio and one in the role of the surface.
/// </summary>
[TestClass]
public sealed class XamlConditionProtocolTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";
    private const int _MessageId = (int)DesignerMessageId.EvaluateXamlConditions;

    private static readonly TimeSpan _Long = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task RequestIsAnsweredThroughTheDesignerProtocol()
    {
        using var link = new ProtocolLink();
        XamlConditionService surface = CreateSurfaceConditions();
        link.Surface.RegisterAsyncMessageObserver<EvaluateXamlConditionsRequest, EvaluateXamlConditionsResponse>(
            _MessageId,
            request => Task.FromResult(XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), surface)));
        link.Start();
        List<XamlConditionQuery> queries =
        [
            XamlConditionQuery.Create($"IsApiContractPresent({_Contract},7)", predicateNamespace: null),
            XamlConditionQuery.Create($"IsApiContractNotPresent({_Contract},7)", predicateNamespace: null),
            XamlConditionQuery.Create("cond:FeatureFlagCondition(A, B)", "using:My.App"),
            XamlConditionQuery.Create("cond:NoSuchCondition(é ∑ \"quoted\" \\ /)", "using:My.App"),
        ];

        EvaluateXamlConditionsResponse response = await Within(
            link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(_MessageId, XamlConditionExchange.CreateRequest(queries)),
            _Long);

        CollectionAssert.AreEqual(new bool?[] { true, false, true, false }, response.Outcomes);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public async Task EmptyRequestIsAnswered()
    {
        using var link = new ProtocolLink();
        XamlConditionService surface = CreateSurfaceConditions();
        link.Surface.RegisterAsyncMessageObserver<EvaluateXamlConditionsRequest, EvaluateXamlConditionsResponse>(
            _MessageId,
            request => Task.FromResult(XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), surface)));
        link.Start();

        EvaluateXamlConditionsResponse response = await Within(
            link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(_MessageId, XamlConditionExchange.CreateRequest([])),
            _Long);

        Assert.IsTrue(XamlConditionExchange.TryApply([], response, new XamlConditionService(new XamlConditionProbeSet()), out _));
    }

    [TestMethod]
    public async Task RequestToAProcessThatDoesNotKnowTheMessageIsNeverAnswered()
    {
        // A surface built before the message existed has no observer for it: the handler drops the message.
        using var link = new ProtocolLink();
        link.Start();

        Task<EvaluateXamlConditionsResponse> reply = link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(
            _MessageId,
            XamlConditionExchange.CreateRequest([XamlConditionQuery.Create("IsTypePresent(My.Type)", predicateNamespace: null)]));

        await Assert.ThrowsAsync<TimeoutException>(() => Within(reply, TimeSpan.FromMilliseconds(300)));
        Assert.IsFalse(reply.IsCompleted);

        // Closing the connection ends the wait.
        link.VisualStudio.Shutdown();
        await Assert.ThrowsAsync<TaskCanceledException>(() => Within(reply, _Long));
    }

    [TestMethod]
    public async Task MessagesOfVisualStudioKeepFlowingAroundTheExtensionMessage()
    {
        // The handler dispatches by number alone, and replies do not wait for the thread that processes requests.
        using var link = new ProtocolLink();
        var held = new TaskCompletionSource<EvaluateXamlConditionsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        link.Surface.RegisterAsyncMessageObserver<EvaluateXamlConditionsRequest, EvaluateXamlConditionsResponse>(_MessageId, _ => held.Task);
        link.Surface.RegisterMessageObserver<EvaluateXamlConditionsRequest, EvaluateXamlConditionsResponse>(
            (int)DesignerMessageId.GetProperties,
            _ => new EvaluateXamlConditionsResponse { Error = "another message" });
        link.Start();

        Task<EvaluateXamlConditionsResponse> slow = link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(_MessageId, XamlConditionExchange.CreateRequest([]));
        EvaluateXamlConditionsResponse other = await Within(
            link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>((int)DesignerMessageId.GetProperties, XamlConditionExchange.CreateRequest([])),
            _Long);

        Assert.AreEqual("another message", other.Error);
        Assert.IsFalse(slow.IsCompleted);

        held.SetResult(new EvaluateXamlConditionsResponse { Outcomes = [] });
        Assert.IsNotNull(await Within(slow, _Long));
    }

    [TestMethod]
    public async Task VisualStudioSideUsesTheAnswersOfTheSurfaceSide()
    {
        using var link = new ProtocolLink();
        XamlConditionService surface = CreateSurfaceConditions();
        link.Surface.RegisterAsyncMessageObserver<EvaluateXamlConditionsRequest, EvaluateXamlConditionsResponse>(
            _MessageId,
            request => Task.FromResult(XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), surface)));
        link.Start();
        int changes = 0;
        var warnings = new ConcurrentQueue<string>();
        var answers = new XamlConditionAnswers(
            request => link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(_MessageId, request),
            link.VisualStudio.CancellationToken,
            () => Interlocked.Increment(ref changes),
            new XamlConditionProbeSet(),
            (message, _) => warnings.Enqueue(message));
        Func<string, string?> namespaces = prefix => prefix == "cond" ? "using:My.App" : null;
        string c7 = $"IsApiContractPresent({_Contract},7)";
        string c99n = $"IsApiContractNotPresent({_Contract},99)";

        Assert.IsNull(answers.Evaluate(c7, namespaces));

        await answers.AskAsync(
            [
                XamlConditionQuery.Create(c7, namespaces),
                XamlConditionQuery.Create(c99n, namespaces),
                XamlConditionQuery.Create("cond:FeatureFlagCondition(A, B)", namespaces),
                XamlConditionQuery.Create("cond:NoSuchCondition(X)", namespaces),
            ],
            _Long);

        Assert.IsTrue(answers.SurfaceAnswers);
        Assert.AreEqual(true, answers.Evaluate(c7, namespaces));
        Assert.AreEqual(true, answers.Evaluate(c99n, namespaces));
        Assert.AreEqual(false, answers.Evaluate($"IsApiContractPresent( {_Contract} , 99 )", namespaces));
        Assert.AreEqual(true, answers.Evaluate("cond:FeatureFlagCondition(A, B)", namespaces));
        Assert.AreEqual(false, answers.Evaluate("cond:NoSuchCondition(X)", namespaces));
        Assert.IsTrue(Volatile.Read(ref changes) >= 1);
        Assert.AreEqual(0, warnings.Count, string.Join(Environment.NewLine, warnings));
    }

    [TestMethod]
    public async Task VisualStudioSideGivesUpWaitingForASurfaceThatDoesNotAnswer()
    {
        using var link = new ProtocolLink();
        link.Start();
        var warnings = new ConcurrentQueue<string>();
        var answers = new XamlConditionAnswers(
            request => link.VisualStudio.SendMessageAsync<EvaluateXamlConditionsResponse>(_MessageId, request),
            link.VisualStudio.CancellationToken,
            () => { },
            new XamlConditionProbeSet { IsApiContractPresent = (_, _, _) => true },
            (message, _) => warnings.Enqueue(message));

        await answers.AskAsync([XamlConditionQuery.Create($"IsApiContractPresent({_Contract},7)", predicateNamespace: null)], TimeSpan.FromMilliseconds(200));

        // Nothing is decided without the surface, not even what this process could tell by itself.
        Assert.IsFalse(answers.SurfaceAnswers);
        Assert.IsNull(answers.Evaluate($"IsApiContractPresent({_Contract},7)", _ => null));
        Assert.AreEqual(1, warnings.Count);
        StringAssert.Contains(warnings.Single(), "did not answer");
    }

    /// <summary>
    /// Awaits <paramref name="task"/>, but not longer than <paramref name="timeout"/>.
    /// </summary>
    private static async Task<T> Within<T>(Task<T> task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException();
        }

        return await task;
    }

    private static XamlConditionService CreateSurfaceConditions()
    {
        return new XamlConditionService(new XamlConditionProbeSet
        {
            IsApiContractPresent = (_, major, _) => major <= 10,
            EvaluateCustom = (type, argument) => type == "My.App.FeatureFlagCondition" ? argument == "A, B" : null,
        });
    }

    /// <summary>
    /// Two protocol handlers, each reading what the other one writes.
    /// </summary>
    private sealed class ProtocolLink : IDisposable
    {
        private readonly MemoryBridge _visualStudioBridge;
        private readonly MemoryBridge _surfaceBridge;

        public ProtocolLink()
        {
            var toSurface = new BlockingCollection<byte[]>();
            var toVisualStudio = new BlockingCollection<byte[]>();
            _visualStudioBridge = new MemoryBridge(incoming: toVisualStudio, outgoing: toSurface);
            _surfaceBridge = new MemoryBridge(incoming: toSurface, outgoing: toVisualStudio);

            // Both processes create their handler without starting it and start it once the observers are registered.
            VisualStudio = new ProtocolHandler(_visualStudioBridge, tokenSource: null, shouldStart: false);
            Surface = new ProtocolHandler(_surfaceBridge, tokenSource: null, shouldStart: false);
        }

        public ProtocolHandler VisualStudio { get; }

        public ProtocolHandler Surface { get; }

        public void Start()
        {
            Surface.Start();
            VisualStudio.Start();
        }

        public void Dispose()
        {
            VisualStudio.Dispose();
            Surface.Dispose();
            _visualStudioBridge.Dispose();
            _surfaceBridge.Dispose();
        }
    }

    /// <summary>
    /// The transport of one side: whole messages, as the pipes of the designer deliver them.
    /// </summary>
    private sealed class MemoryBridge(BlockingCollection<byte[]> incoming, BlockingCollection<byte[]> outgoing) : IDataBridge, IDisposable
    {
        private volatile bool _closed;

        public ManualResetEvent ReadyEvent { get; } = new(initialState: true);

        public ManualResetEvent FirstMessageEvent { get; } = new(initialState: false);

        public bool IsConnected => !_closed;

        public byte[] ReadMessage()
        {
            try
            {
                // A missing message means the connection is closed, which is how the handler stops reading.
                return incoming.TryTake(out byte[]? message, Timeout.Infinite) ? message : null!;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                return null!;
            }
        }

        public void WriteMessage(byte[] buffer)
        {
            if (!_closed && !outgoing.IsAddingCompleted)
            {
                outgoing.Add(buffer);
            }
        }

        public void Close()
        {
            _closed = true;
            incoming.CompleteAdding();
        }

        public bool VerifyConnected(CancellationToken token)
        {
            return !_closed;
        }

        public void Dispose()
        {
            Close();
            ReadyEvent.Dispose();
            FirstMessageEvent.Dispose();
        }
    }
}
