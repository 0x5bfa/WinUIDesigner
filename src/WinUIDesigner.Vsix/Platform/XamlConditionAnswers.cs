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
/// condition, and is asked in the background; the answer applies to what is built afterwards. Until the surface
/// has answered once, no condition is evaluated at all: a surface that does not know the message never answers,
/// and it loads prepared XAML with its conditional namespaces as written.
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
    private readonly XamlConditionService _conditions;
    private readonly ConcurrentDictionary<string, bool> _answeredKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // Guarded by _gate: the questions that wait for the request in flight to finish, the keys of the
    // questions that were sent, and the request in flight together with the processing of its reply.
    private readonly Dictionary<string, XamlConditionQuery> _waiting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _askedKeys = new(StringComparer.Ordinal);
    private Task? _request;
    private int _failedRequests;

    private volatile bool _surfaceAnswers;

    /// <summary>
    /// Initializes a new instance of the <see cref="XamlConditionAnswers"/> class.
    /// </summary>
    /// <param name="askSurface">Sends a request to the surface and returns its reply.</param>
    /// <param name="connectionClosed">Is canceled when the connection to the surface closes.</param>
    /// <param name="answersChanged">
    /// Called, on a thread-pool thread, when answers arrived that change what <see cref="Evaluate"/> returns.
    /// </param>
    /// <param name="localProbes">What this process can tell about the system; see <see cref="CreateLocalProbes"/>.</param>
    /// <param name="report">Receives a warning and the exception behind it, if any.</param>
    public XamlConditionAnswers(
        Func<EvaluateXamlConditionsRequest, Task<EvaluateXamlConditionsResponse>> askSurface,
        CancellationToken connectionClosed,
        Action answersChanged,
        XamlConditionProbeSet localProbes,
        Action<string, Exception?> report)
    {
        _askSurface = askSurface ?? throw new ArgumentNullException(nameof(askSurface));
        _connectionClosed = connectionClosed;
        _answersChanged = answersChanged ?? throw new ArgumentNullException(nameof(answersChanged));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _conditions = new XamlConditionService(
            localProbes ?? throw new ArgumentNullException(nameof(localProbes)),
            message => Report("Conditional XAML: " + message));
    }

    /// <summary>
    /// Gets a value indicating whether the surface has answered a request. Conditions are evaluated only then.
    /// </summary>
    public bool SurfaceAnswers => _surfaceAnswers;

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
    /// Asks the surface the questions it has not been asked yet and waits a limited time for the answers.
    /// Answers that arrive later are kept as well.
    /// </summary>
    /// <param name="queries">The conditions of the documents that are about to be built; may be empty.</param>
    /// <param name="timeout">How long to wait. The surface answers in the order it receives messages.</param>
    public async Task AskAsync(IReadOnlyCollection<XamlConditionQuery> queries, TimeSpan timeout)
    {
        foreach (XamlConditionQuery query in queries)
        {
            Enqueue(query);
        }

        if (queries.Count == 0)
        {
            // Nothing to wait for. An empty request still shows whether the surface answers at all, so that
            // a condition typed later is evaluated the moment it appears.
            if (!_surfaceAnswers)
            {
                _ = Send(force: true);
            }

            return;
        }

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            // A request of an earlier call may still be in flight; the questions of this call follow it.
            Task? request = Send(force: false);
            if (request is null)
            {
                return;
            }

            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero
                || await Task.WhenAny(request, Task.Delay(remaining)).ConfigureAwait(false) != request)
            {
                Report($"The surface did not answer which conditional XAML namespaces apply within {timeout.TotalSeconds:0.#} s. Documents built from construction actions use its answers once they arrive.");
                return;
            }
        }
    }

    /// <summary>
    /// Evaluates a condition for the document model. The signature is that of <see cref="XamlConditionEvaluator"/>.
    /// Never blocks.
    /// </summary>
    public bool? Evaluate(string condition, Func<string, string?> resolveNamespace)
    {
        XamlConditionQuery query = XamlConditionQuery.Create(condition, resolveNamespace);

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
        bool failed = true;
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
            else if (!XamlConditionExchange.TryApply(queries, reply.Result, _conditions, out bool changed))
            {
                Report($"The surface's reply does not answer the {queries.Count} conditional XAML namespace condition(s) it was asked.");
            }
            else
            {
                failed = false;
                foreach (XamlConditionQuery query in queries)
                {
                    _answeredKeys[query.Key] = true;
                }

                bool firstAnswer = !_surfaceAnswers;
                _surfaceAnswers = true;
                if (changed || firstAnswer)
                {
                    // Verdicts that were reached without these answers are stale.
                    _answersChanged();
                }
            }
        }
        catch (Exception ex)
        {
            Report("Processing the surface's answer to the conditional XAML namespace request failed.", ex);
        }

        lock (_gate)
        {
            _request = null;
            if (failed)
            {
                // The questions may be asked again, but not without end.
                Forget(queries);
                _failedRequests++;
                return;
            }

            _failedRequests = 0;
        }

        // Questions that came up while the request was in flight.
        Send(force: false);
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
