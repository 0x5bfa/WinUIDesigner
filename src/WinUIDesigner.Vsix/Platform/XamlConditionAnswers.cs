// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Platform;

/// <summary>
/// Knows, on the Visual Studio side, whether the conditions of conditional XAML namespaces hold.
/// </summary>
/// <remarks>
/// <para>
/// The surface process is the authority: it runs the Windows App SDK and loads the previewed project, and it
/// rewrites prepared XAML with its own answers. This class asks it with
/// <see cref="DesignerMessageId.EvaluateXamlConditions"/> and keeps what it answers, so that documents built
/// from construction actions drop the same members.
/// </para>
/// <para>
/// Nothing here waits for the surface while a document is being built. A condition that is met for the first
/// time is answered from this process where that is possible, counts as not satisfied when it is a custom
/// condition, and is asked in the background. Until the surface has answered once, no condition is evaluated
/// at all: a surface that does not know the message never answers, and it loads prepared XAML with its
/// conditional namespaces as written.
/// </para>
/// <para>
/// Answers take effect only in <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan)"/>,
/// which runs before a document is sent to the surface, never while Visual Studio builds one. A reply that
/// arrived in the middle of building would change verdicts halfway through: an element built as written would
/// lose its properties, and the surface cannot create a <c>Setter</c> without its <c>Property</c>. A reply that
/// arrives later takes effect with the next document.
/// </para>
/// <para>
/// The answers the user forces (<see cref="XamlConditionOverrides"/>) come before everything else, also before
/// the surface has answered: the surface applies the same overrides to prepared XAML, because they travel to it
/// in the CreateSurface request. They are read in <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan)"/>
/// as well, so they cannot change while a document is being built either.
/// </para>
/// <para>
/// The class does not depend on the designer: the connection to the surface and the log are passed in as
/// delegates. Instances are safe to use from any thread.
/// </para>
/// </remarks>
internal sealed class XamlConditionAnswers
{
    // After this many requests that failed in a row, the surface is not asked again.
    private const int _MaxFailedRequests = 3;

    private readonly Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>> _askSurface;
    private readonly CancellationToken _connectionClosed;
    private readonly Action _answersChanged;
    private readonly Action<string, Exception?> _report;
    private readonly Func<XamlConditionOverrides>? _readOverrides;
    private readonly XamlConditionService _conditions;
    private readonly ConcurrentDictionary<string, bool> _answeredKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // Guarded by _gate: the questions that wait for the request in flight to finish, the keys of the
    // questions that were sent, the request in flight together with the processing of its reply, the
    // replies that have not taken effect yet, and whether the long wait for the first answer was spent.
    private readonly Dictionary<string, XamlConditionQuery> _waiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _askedKeys = new(StringComparer.Ordinal);
    private readonly List<ReceivedReply> _received = [];
    private Task? _request;
    private int _failedRequests;
    private bool _firstAnswerWaited;

    private volatile bool _surfaceAnswers;

    // Replaced as a whole, only between documents.
    private volatile XamlConditionOverrides _overrides = XamlConditionOverrides.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="XamlConditionAnswers"/> class.
    /// </summary>
    /// <param name="askSurface">Sends a request to the surface and returns its reply.</param>
    /// <param name="connectionClosed">Is canceled when the connection to the surface closes.</param>
    /// <param name="answersChanged">
    /// Called, on the thread that calls <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan)"/>,
    /// when answers or overrides took effect that change what <see cref="Evaluate"/> returns.
    /// </param>
    /// <param name="localProbes">What this process can tell about the system; see <see cref="CreateLocalProbes"/>.</param>
    /// <param name="report">Receives a warning and the exception behind it, if any.</param>
    /// <param name="readOverrides">
    /// Returns the answers the user forces, the ones the surface received with the CreateSurface request; without
    /// it nothing is overridden. It is called between documents only.
    /// </param>
    public XamlConditionAnswers(
        Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>> askSurface,
        CancellationToken connectionClosed,
        Action answersChanged,
        XamlConditionProbeSet localProbes,
        Action<string, Exception?> report,
        Func<XamlConditionOverrides>? readOverrides = null)
    {
        _askSurface = askSurface ?? throw new ArgumentNullException(nameof(askSurface));
        _connectionClosed = connectionClosed;
        _answersChanged = answersChanged ?? throw new ArgumentNullException(nameof(answersChanged));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _readOverrides = readOverrides;
        _conditions = new XamlConditionService(
            localProbes ?? throw new ArgumentNullException(nameof(localProbes)),
            message => Report("Conditional XAML: " + message));
    }

    /// <summary>
    /// Gets a value indicating whether an answer of the surface has taken effect. Conditions are evaluated only then.
    /// </summary>
    public bool SurfaceAnswers => _surfaceAnswers;

    /// <summary>
    /// Gets the answers the user forces that are in effect: the ones read before the latest document was sent.
    /// </summary>
    public XamlConditionOverrides Overrides => _overrides;

    /// <summary>
    /// Gets a value indicating whether a request is waiting for its reply. A reply that arrived is either kept
    /// for the next call of <see cref="AskAsync(IReadOnlyCollection{XamlConditionQuery}, TimeSpan, TimeSpan)"/> or failed.
    /// </summary>
    internal bool IsRequestInFlight
    {
        get
        {
            lock (_gate)
            {
                return _request is not null;
            }
        }
    }

    /// <summary>
    /// Returns the probes for the conditions Visual Studio can evaluate itself, because they ask about the
    /// operating system both processes run on. The probes are missing where the Windows Runtime class behind
    /// them cannot be reached; only the surface answers then, which takes effect later.
    /// </summary>
    public static XamlConditionProbeSet CreateLocalProbes()
    {
        return WindowsApiInformation.CreateProbes();
    }

    /// <summary>
    /// Asks the surface the questions it has not been asked yet, waits a limited time for the answers, and puts
    /// every answer that has arrived into effect.
    /// </summary>
    /// <param name="queries">The conditions of the documents that are about to be built; may be empty.</param>
    /// <param name="timeout">How long to wait. The surface answers in the order it receives messages.</param>
    public Task AskAsync(IReadOnlyCollection<XamlConditionQuery> queries, TimeSpan timeout)
    {
        return AskAsync(queries, timeout, timeout);
    }

    /// <summary>
    /// Asks the surface the questions it has not been asked yet, waits a limited time for the answers, and puts
    /// every answer that has arrived into effect. Called before a document is sent to the surface.
    /// </summary>
    /// <param name="queries">The conditions of the documents that are about to be built; may be empty.</param>
    /// <param name="timeout">How long to wait. The surface answers in the order it receives messages.</param>
    /// <param name="firstAnswerTimeout">
    /// How long to wait while the surface has not answered yet: a surface process that has just started
    /// answers only once it is ready. This wait is spent once, so a surface that never answers costs it a
    /// single time.
    /// </param>
    /// <remarks>
    /// Answers take effect here and nowhere else, so that they cannot change while a document is being built.
    /// A reply that arrives after this call returns takes effect at the next call.
    /// </remarks>
    public async Task AskAsync(IReadOnlyCollection<XamlConditionQuery> queries, TimeSpan timeout, TimeSpan firstAnswerTimeout)
    {
        // What arrived since the last document takes effect before anything of this one is built.
        ApplyReceivedAnswers();

        foreach (XamlConditionQuery query in queries)
        {
            Enqueue(query);
        }

        if (queries.Count == 0)
        {
            // Nothing to wait for. An empty request still shows whether the surface answers at all; its
            // reply takes effect with the next document.
            if (!_surfaceAnswers)
            {
                _ = Send(force: true);
            }

            return;
        }

        TimeSpan wait = TakeWaitTime(timeout, firstAnswerTimeout);
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                // A request of an earlier call may still be in flight; the questions of this call follow it.
                Task? request = Send(force: false);
                if (request is null)
                {
                    return;
                }

                TimeSpan remaining = wait - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero
                    || await Task.WhenAny(request, Task.Delay(remaining)).ConfigureAwait(false) != request)
                {
                    Report($"The surface did not answer which conditional XAML namespaces apply within {wait.TotalSeconds:0.#} s. Its answers take effect when the next document is loaded.");
                    return;
                }
            }
        }
        finally
        {
            ApplyReceivedAnswers();
        }
    }

    /// <summary>
    /// Evaluates a condition for the document model. The signature is that of <see cref="XamlConditionEvaluator"/>.
    /// Never blocks.
    /// </summary>
    public bool? Evaluate(string condition, Func<string, string?> resolveNamespace)
    {
        XamlConditionQuery query = XamlConditionQuery.Create(condition, resolveNamespace);

        // The user's choice decides first, whether or not the surface has answered: the surface rewrites prepared
        // XAML with the same overrides, which it receives with the document.
        bool? forced = _overrides.Evaluate(query);
        if (forced.HasValue)
        {
            return forced;
        }

        // Malformed conditions, and custom conditions whose type cannot even be named, are decided by code
        // both processes share. Only the others are worth asking.
        XamlConditionKind kind = query.Condition.Kind;
        bool surfaceDecides = kind is XamlConditionKind.ApiContract or XamlConditionKind.Type or XamlConditionKind.Property
            || (kind == XamlConditionKind.Custom && query.TypeName is not null);

        // Without an answering surface nothing is decided: deciding conditions here alone would make the two
        // loading paths disagree. Otherwise this process answers first, so that the surface's answer, which
        // may arrive at any moment, is compared with what was returned.
        bool surfaceAnswers = _surfaceAnswers;
        bool? holds = surfaceAnswers ? _conditions.Evaluate(query) : null;

        if (surfaceDecides && !_answeredKeys.ContainsKey(query.Key))
        {
            Enqueue(query);
            Send(force: false);
        }

        if (!holds.HasValue && kind == XamlConditionKind.Custom && surfaceAnswers)
        {
            // Not answered yet. The surface treats a condition it cannot create as not satisfied, and so
            // does this side until it knows better.
            return false;
        }

        return holds;
    }

    /// <summary>
    /// Returns what a condition evaluates to without the user's overrides, as far as this side knows it: for the
    /// designer's user interface, which shows it next to an override. Never blocks and asks nothing.
    /// </summary>
    /// <returns>
    /// Whether the condition holds as written, or <see langword="null"/> while the surface has not answered it.
    /// </returns>
    public bool? EvaluateIgnoringOverrides(XamlConditionQuery query)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (!_surfaceAnswers)
        {
            return null;
        }

        // A condition only the surface can answer has no value here until its answer arrived; the operating
        // system, which both processes share, is asked here as it is while documents are built.
        XamlConditionKind kind = query.Condition.Kind;
        bool onlySurfaceKnows = (kind == XamlConditionKind.Custom && query.TypeName is not null)
            || ((kind is XamlConditionKind.Type or XamlConditionKind.Property)
                && !query.Condition.Arguments[0].StartsWith("Windows.", StringComparison.Ordinal));
        if (onlySurfaceKnows && !_answeredKeys.ContainsKey(query.Key))
        {
            return null;
        }

        return _conditions.Evaluate(query);
    }

    private void Enqueue(XamlConditionQuery query)
    {
        if (query.Condition.Kind is XamlConditionKind.Empty or XamlConditionKind.Unknown)
        {
            return;
        }

        lock (_gate)
        {
            if (!_askedKeys.Contains(query.Key))
            {
                _waiting[query.Key] = query;
            }
        }
    }

    /// <summary>
    /// Sends the waiting questions unless a request is in flight.
    /// </summary>
    /// <param name="force">Whether to send a request even when no question is waiting.</param>
    /// <returns>
    /// The request in flight, completed once its reply was processed, or <see langword="null"/> when there
    /// is none and nothing was sent.
    /// </returns>
    private Task? Send(bool force)
    {
        Exception? failure;
        lock (_gate)
        {
            if (_request is not null)
            {
                return _request;
            }

            if ((_waiting.Count == 0 && !force)
                || _failedRequests >= _MaxFailedRequests
                || _connectionClosed.IsCancellationRequested)
            {
                return null;
            }

            var queries = new List<XamlConditionQuery>(_waiting.Values);
            _waiting.Clear();
            foreach (XamlConditionQuery query in queries)
            {
                _askedKeys.Add(query.Key);
            }

            try
            {
                // The reply is completed on the thread that reads from the pipe; it is processed elsewhere.
                _request = _askSurface(XamlConditionExchange.CreateRequest(queries)).ContinueWith(
                    reply => ProcessReply(queries, reply),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                return _request;
            }
            catch (Exception ex)
            {
                Forget(queries);
                _failedRequests++;
                failure = ex;
            }
        }

        Report("Asking the surface which conditional XAML namespaces apply failed.", failure);
        return null;
    }

    private void ProcessReply(List<XamlConditionQuery> queries, Task<EvaluateXamlConditionsResponse> reply)
    {
        EvaluateXamlConditionsResponse? received = null;
        try
        {
            if (reply.IsFaulted)
            {
                Report("The surface's reply to the conditional XAML namespace request could not be read.", reply.Exception?.GetBaseException());
            }
            else if (reply.IsCanceled)
            {
                // The connection to the surface is closing; there is nothing to report.
            }
            else if (!string.IsNullOrEmpty(reply.Result?.Error))
            {
                Report($"The surface could not evaluate conditional XAML namespaces: {reply.Result!.Error}");
            }
            else if (!XamlConditionExchange.IsReplyTo(queries, reply.Result))
            {
                Report($"The surface's reply does not answer the {queries.Count} conditional XAML namespace condition(s) it was asked.");
            }
            else
            {
                received = reply.Result;
            }
        }
        catch (Exception ex)
        {
            Report("Processing the surface's answer to the conditional XAML namespace request failed.", ex);
        }

        lock (_gate)
        {
            _request = null;
            if (received is null)
            {
                // The questions may be asked again, but not without end.
                Forget(queries);
                _failedRequests++;
                return;
            }

            // A document may be being built right now; the answers wait for the next one.
            _received.Add(new ReceivedReply(queries, received));
            foreach (XamlConditionQuery query in queries)
            {
                _answeredKeys[query.Key] = true;
            }

            _failedRequests = 0;
        }

        // Questions that came up while the request was in flight.
        Send(force: false);
    }

    /// <summary>
    /// Puts the user's overrides and the replies that arrived into effect. Called only between documents.
    /// </summary>
    private void ApplyReceivedAnswers()
    {
        bool changed = ApplyOverrides();
        bool firstAnswer = false;

        List<ReceivedReply>? received = null;
        lock (_gate)
        {
            if (_received.Count != 0)
            {
                received = new List<ReceivedReply>(_received);
                _received.Clear();
            }
        }

        if (received is not null)
        {
            foreach (ReceivedReply reply in received)
            {
                try
                {
                    XamlConditionExchange.TryApply(reply.Queries, reply.Response, _conditions, out bool replyChanged);
                    changed |= replyChanged;
                }
                catch (Exception ex)
                {
                    Report("Processing the surface's answer to the conditional XAML namespace request failed.", ex);
                }
            }

            firstAnswer = !_surfaceAnswers;
            _surfaceAnswers = true;
        }

        if (changed || firstAnswer)
        {
            try
            {
                // Verdicts that were reached without these answers are stale.
                _answersChanged();
            }
            catch (Exception ex)
            {
                Report("Discarding the verdicts reached without the surface's answers failed.", ex);
            }
        }
    }

    /// <summary>
    /// Reads the user's overrides and puts them into effect. Called only between documents.
    /// </summary>
    /// <returns>Whether the overrides in effect changed.</returns>
    private bool ApplyOverrides()
    {
        if (_readOverrides is null)
        {
            return false;
        }

        XamlConditionOverrides overrides;
        try
        {
            overrides = _readOverrides() ?? XamlConditionOverrides.Empty;
        }
        catch (Exception ex)
        {
            // The overrides in effect stay; the surface received the same ones with the previous document.
            Report("Reading the conditional XAML conditions the user forces failed; the previous ones stay in effect.", ex);
            return false;
        }

        if (overrides.Equals(_overrides))
        {
            return false;
        }

        _overrides = overrides;
        return true;
    }

    private TimeSpan TakeWaitTime(TimeSpan timeout, TimeSpan firstAnswerTimeout)
    {
        lock (_gate)
        {
            bool waitForFirstAnswer = !_surfaceAnswers && !_firstAnswerWaited;
            _firstAnswerWaited = true;
            return waitForFirstAnswer && firstAnswerTimeout > timeout ? firstAnswerTimeout : timeout;
        }
    }

    private void Forget(List<XamlConditionQuery> queries)
    {
        foreach (XamlConditionQuery query in queries)
        {
            _askedKeys.Remove(query.Key);
        }
    }

    private void Report(string message, Exception? exception = null)
    {
        try
        {
            _report(message, exception);
        }
        catch (Exception)
        {
            // A log that fails must not change which members a document has.
        }
    }

    /// <summary>
    /// A reply of the surface that has not taken effect yet, with the questions it answers.
    /// </summary>
    private sealed class ReceivedReply(List<XamlConditionQuery> queries, EvaluateXamlConditionsResponse response)
    {
        public List<XamlConditionQuery> Queries { get; } = queries;

        public EvaluateXamlConditionsResponse Response { get; } = response;
    }

    /// <summary>
    /// Reaches <c>Windows.Foundation.Metadata.ApiInformation</c> through reflection: the extension has no
    /// reference to Windows metadata.
    /// </summary>
    private static class WindowsApiInformation
    {
        private const string _TypeName = "Windows.Foundation.Metadata.ApiInformation, Windows, ContentType=WindowsRuntime";

        public static XamlConditionProbeSet CreateProbes()
        {
            var probes = new XamlConditionProbeSet();
            try
            {
                Type? type = Type.GetType(_TypeName, throwOnError: false);
                if (type is null)
                {
                    return probes;
                }

                MethodInfo? isContractPresent = type.GetMethod("IsApiContractPresent", [typeof(string), typeof(ushort)]);
                MethodInfo? isContractVersionPresent = type.GetMethod("IsApiContractPresent", [typeof(string), typeof(ushort), typeof(ushort)]);
                MethodInfo? isTypePresent = type.GetMethod("IsTypePresent", [typeof(string)]);
                MethodInfo? isPropertyPresent = type.GetMethod("IsPropertyPresent", [typeof(string), typeof(string)]);

                if (isContractPresent is not null && isContractVersionPresent is not null)
                {
                    probes.IsApiContractPresent = (name, major, minor) => minor.HasValue
                        ? isContractVersionPresent.Invoke(null, [name, (ushort)major, (ushort)minor.Value]) is true
                        : isContractPresent.Invoke(null, [name, (ushort)major]) is true;
                }

                if (isTypePresent is not null)
                {
                    probes.IsWindowsTypePresent = name => isTypePresent.Invoke(null, [name]) is true;
                }

                if (isPropertyPresent is not null)
                {
                    probes.IsWindowsPropertyPresent = (typeName, propertyName) => isPropertyPresent.Invoke(null, [typeName, propertyName]) is true;
                }
            }
            catch (Exception)
            {
                // The class cannot be reached from this process; the probes that were not set stay missing.
            }

            return probes;
        }
    }
}
