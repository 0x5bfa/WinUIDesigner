// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    // The answers of this process. The XAML reader keeps its own answers for the lifetime of the process too.
    private XamlConditionService? _conditions;

    private XamlConditionService Conditions => LazyInitializer.EnsureInitialized(ref _conditions, CreateConditionService);

    /// <summary>
    /// Configures how conditional XAML namespaces are evaluated in the surface process.
    /// </summary>
    /// <remarks>
    /// Every condition is decided here, so that no conditional namespace reaches the XAML reader: the reader
    /// ends the process, without an exception, on a condition whose type it cannot find, on an unknown method
    /// and on several malformed conditions. The answers the user forces come first; they arrive with the
    /// design-time options of the CreateSurface request, and Visual Studio applies the same ones to the documents
    /// it builds from construction actions.
    /// </remarks>
    private void ConfigureConditionalNamespacePolicy(XamlNamespaceRewriteOptions options)
    {
        options.EvaluateCondition = XamlConditionOverrides.CreateEvaluator(GetConditionOverrides(), Conditions.Evaluate);
    }

    /// <summary>
    /// Answers Visual Studio's question whether conditions hold, with the answers the prepared XAML is rewritten with.
    /// </summary>
    /// <remarks>
    /// The answers leave the user's overrides aside: Visual Studio applies the overrides itself, and it shows the
    /// evaluated answer next to an override.
    /// </remarks>
    private async Task<EvaluateXamlConditionsResponse> HandleEvaluateXamlConditionsAsync(EvaluateXamlConditionsRequest request)
    {
        // A request that fails must still be answered: a faulted handler is reported to Visual Studio as an
        // unhandled exception of the surface, and the sender would wait for its reply in vain.
        try
        {
            List<XamlConditionQuery> queries = XamlConditionExchange.ReadQueries(request);
            XamlConditionService conditions = Conditions;

            // Conditions of the application are created on its UI thread. The others are answered right here,
            // without waiting for a UI thread that may be busy loading a document.
            EvaluateXamlConditionsResponse response = XamlConditionExchange.NeedsApplicationObjects(queries)
                ? await DispatcherOperation.InvokeAsync(_dispatcherQueue, () => XamlConditionExchange.Answer(queries, conditions), _protocolHandler.CancellationToken).ConfigureAwait(false)
                : XamlConditionExchange.Answer(queries, conditions);

            WinUIDesignerLogger.LogTrace("Surface", $"Conditional XAML: answered {queries.Count} condition(s) for Visual Studio.");
            return response;
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogWarning("Surface", "Evaluating conditional XAML namespaces for Visual Studio failed.", ex);
            return new EvaluateXamlConditionsResponse { Outcomes = [], Error = ex.Message };
        }
    }

    private XamlConditionService CreateConditionService()
    {
        XamlConditionProbeSet probes = XamlConditionProbes.Create();
        Func<string, string, bool?> evaluateCustom = probes.EvaluateCustom!;
        probes.EvaluateCustom = (typeName, argument) =>
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                return evaluateCustom(typeName, argument);
            }

            // Prepared XAML is rewritten on the UI thread and requests are moved there, so this is not the
            // usual way in. Should another thread ask, the condition is still created where it belongs.
            return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => evaluateCustom(typeName, argument), _protocolHandler.CancellationToken)
                .GetAwaiter().GetResult();
        };

        return new XamlConditionService(probes, message => WinUIDesignerLogger.LogWarning("Surface", "Conditional XAML: " + message));
    }
}
