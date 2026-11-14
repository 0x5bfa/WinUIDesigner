// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    // The surface answers in the order it receives messages, and conditions of the application are created
    // on its UI thread. Loading a document must not wait long for either.
    private static readonly TimeSpan _ConditionAnswerTimeout = TimeSpan.FromSeconds(2);

    private static readonly object _Reported = new();

    // Places in the source text that were looked at for a lost value already; the keys die with their documents.
    private readonly ConditionalWeakTable<object, object> _reportedLostValues = new();

    private XamlConditionAnswers? _conditionAnswers;

    /// <summary>
    /// Gets the answers of the surface process to the conditions of conditional XAML namespaces. Creating them
    /// makes them the evaluator of the member dispositions; until the surface answers they evaluate nothing.
    /// </summary>
    private XamlConditionAnswers ConditionAnswers => LazyInitializer.EnsureInitialized(ref _conditionAnswers, CreateConditionAnswers)!;

    /// <summary>
    /// Prepares the evaluation of conditional XAML namespaces before a document is sent to the surface.
    /// </summary>
    /// <remarks>
    /// The surface evaluates the conditions of prepared XAML itself. Its answers are needed here for the
    /// other path: when the surface asks for construction actions, and for every edit afterwards, the
    /// document model decides which members exist.
    /// </remarks>
    private async Task PrepareConditionalNamespacesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        XamlConditionAnswers answers = ConditionAnswers;

        // Everything that may be built for this request: the target, the documents prepared with it, and
        // what else the markup provider has registered, such as App.xaml and the design-time resources.
        var queries = new Dictionary<string, XamlConditionQuery>(StringComparer.Ordinal);
        var scanned = new HashSet<IDocumentRoot>();
        AddConditions(queries, scanned, targetDocument);
        foreach (ISurfaceProcessDocument document in preparedDocuments)
        {
            AddConditions(queries, scanned, document);
        }

        AddConditions(queries, scanned, MarkupProvider.AppXaml);
        foreach (ISurfaceProcessDocument document in MarkupProvider.RegisteredDocuments)
        {
            AddConditions(queries, scanned, document);
        }

        await answers.AskAsync(new List<XamlConditionQuery>(queries.Values), _ConditionAnswerTimeout).ConfigureAwait(false);
        if (queries.Count > 0)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Conditional XAML: the documents of this request contain {queries.Count} condition(s); the surface {(answers.SurfaceAnswers ? "answers" : "has not answered yet")}.");
        }
    }

    /// <summary>
    /// Reports a value the document model lost, when a property is not built because its prefix is dropped.
    /// </summary>
    /// <remarks>
    /// The document model keeps one value per property: of <c>c7:CornerRadius="4" c7n:CornerRadius="0"</c>
    /// only the last attribute is in it. When that one is dropped, the value of the attribute that applies
    /// is gone and the property stays unset. Prepared XAML does not have this problem.
    /// </remarks>
    private void ReportValueLostToDroppedProperty(DocumentCompositeNode parentNode, IProperty property)
    {
        try
        {
            // The builders ask about a property more than once; one report per place in the text is enough.
            object? site = parentNode.GetContainerContext(property);
            if (site is null)
            {
                return;
            }

            bool isFirstReport = false;
            _reportedLostValues.GetValue(site, _ =>
            {
                isFirstReport = true;
                return _Reported;
            });
            if (!isFirstReport)
            {
                return;
            }

            XamlSourceAttribute dropped = XamlSourcePrefixReader.ReadAttribute(parentNode, property);
            if (!dropped.IsKnown)
            {
                return;
            }

            foreach (XamlAttributeName sibling in dropped.ElementAttributes)
            {
                if (!string.Equals(sibling.LocalName, dropped.Name.LocalName, StringComparison.Ordinal)
                    || string.Equals(sibling.Prefix, dropped.Name.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // An attribute without a prefix always applies; one with a prefix applies unless it is dropped as well.
                if (sibling.Prefix.Length != 0
                    && _memberDispositions.Classify(dropped.ResolvePrefix(sibling.Prefix)) == XamlPrefixDisposition.Drop)
                {
                    continue;
                }

                WinUIDesignerLogger.LogWarning(
                    "Platform",
                    $"The value of {sibling} on <{parentNode.Type.Name}> is lost while the document is built from construction actions: "
                    + $"{dropped.Name} follows it and does not apply, and Visual Studio's document model keeps only the last value written for a property. "
                    + "The property stays unset in the preview. Writing the attribute that applies last avoids this.");
                return;
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Looking for a value lost to a dropped attribute failed: {ex}");
        }
    }

    private XamlConditionAnswers CreateConditionAnswers()
    {
        var answers = new XamlConditionAnswers(
            request => ProtocolHandler.SendMessageAsync<EvaluateXamlConditionsResponse>((int)DesignerMessageId.EvaluateXamlConditions, request),
            ProtocolHandler.CancellationToken,
            _memberDispositions.Invalidate,
            XamlConditionAnswers.CreateLocalProbes(),
            (message, exception) => WinUIDesignerLogger.LogWarning("Platform", message, exception));

        _memberDispositions.EvaluateCondition = answers.Evaluate;
        return answers;
    }

    private void AddConditions(Dictionary<string, XamlConditionQuery> queries, HashSet<IDocumentRoot> scanned, ISurfaceProcessDocument? document)
    {
        IDocumentRoot? root = document?.Document;
        if (root is null || !scanned.Add(root))
        {
            return;
        }

        XamlNamespaceScanResult namespaces = _memberDispositions.GetNamespaces(root);
        if (!namespaces.HasConditionalNamespaces)
        {
            return;
        }

        IReadOnlyList<XamlNamespaceDeclaration> declarations = namespaces.Declarations;
        for (int index = 0; index < declarations.Count; index++)
        {
            string? condition = declarations[index].Uri.Condition;
            if (condition is null)
            {
                continue;
            }

            // The name of a custom condition resolves through a prefix. The scan does not record where a
            // declaration is in scope, so the condition is asked for every namespace the prefix is bound to
            // in the document; usually that is one.
            string prefix = XamlCondition.Parse(condition).Prefix;
            bool isDeclared = false;
            for (int candidate = 0; candidate < declarations.Count; candidate++)
            {
                if (string.Equals(declarations[candidate].Prefix, prefix, StringComparison.Ordinal))
                {
                    string predicateNamespace = declarations[candidate].Uri.Original;
                    AddQuery(queries, XamlConditionQuery.Create(condition, _ => predicateNamespace));
                    isDeclared = true;
                }
            }

            if (!isDeclared)
            {
                AddQuery(queries, XamlConditionQuery.Create(condition, resolveNamespace: null));
            }
        }
    }

    private static void AddQuery(Dictionary<string, XamlConditionQuery> queries, XamlConditionQuery query)
    {
        if (!queries.ContainsKey(query.Key))
        {
            queries.Add(query.Key, query);
        }
    }
}
