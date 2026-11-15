// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections.Generic;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Protocol;

/// <summary>
/// Turns condition questions into an <see cref="EvaluateXamlConditionsRequest"/> and back, on both ends of
/// the designer protocol. Both processes compile this code, so they agree on what a question is.
/// </summary>
internal static class XamlConditionExchange
{
    /// <summary>
    /// Creates the request Visual Studio sends for <paramref name="queries"/>.
    /// </summary>
    public static EvaluateXamlConditionsRequest CreateRequest(IReadOnlyList<XamlConditionQuery> queries)
    {
        var request = new EvaluateXamlConditionsRequest
        {
            Conditions = new List<string?>(queries.Count),
            PredicateNamespaces = new List<string?>(queries.Count),
        };

        for (int index = 0; index < queries.Count; index++)
        {
            request.Conditions.Add(queries[index].Condition.Text);
            request.PredicateNamespaces.Add(queries[index].PredicateNamespace);
        }

        return request;
    }

    /// <summary>
    /// Reads the questions of a request, in the surface process. A request that was built by
    /// <see cref="CreateRequest"/> yields queries with the keys of the ones it was built from.
    /// </summary>
    public static List<XamlConditionQuery> ReadQueries(EvaluateXamlConditionsRequest? request)
    {
        var queries = new List<XamlConditionQuery>();
        List<string?>? conditions = request?.Conditions;
        if (conditions is null)
        {
            return queries;
        }

        List<string?>? namespaces = request!.PredicateNamespaces;
        for (int index = 0; index < conditions.Count; index++)
        {
            string? predicateNamespace = namespaces is not null && index < namespaces.Count ? namespaces[index] : null;
            queries.Add(XamlConditionQuery.Create(conditions[index], predicateNamespace));
        }

        return queries;
    }

    /// <summary>
    /// Returns whether answering <paramref name="queries"/> may create condition objects of the previewed
    /// application, which has to happen on its UI thread.
    /// </summary>
    public static bool NeedsApplicationObjects(IReadOnlyList<XamlConditionQuery> queries)
    {
        for (int index = 0; index < queries.Count; index++)
        {
            if (queries[index].Condition.Kind == XamlConditionKind.Custom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Answers <paramref name="queries"/> with the answers of <paramref name="conditions"/>, in the surface process.
    /// </summary>
    public static EvaluateXamlConditionsResponse Answer(IReadOnlyList<XamlConditionQuery> queries, XamlConditionService conditions)
    {
        var response = new EvaluateXamlConditionsResponse { Outcomes = new List<bool?>(queries.Count) };
        for (int index = 0; index < queries.Count; index++)
        {
            response.Outcomes.Add(conditions.Evaluate(queries[index]));
        }

        return response;
    }

    /// <summary>
    /// Returns whether <paramref name="response"/> answers the request created from <paramref name="queries"/>:
    /// one outcome per question.
    /// </summary>
    public static bool IsReplyTo(IReadOnlyList<XamlConditionQuery> queries, EvaluateXamlConditionsResponse? response)
    {
        List<bool?>? outcomes = response?.Outcomes;
        if (outcomes is null || outcomes.Count != queries.Count)
        {
            // An empty request is answered by an empty reply, whose list may not survive the transport.
            return response is not null && queries.Count == 0;
        }

        return true;
    }

    /// <summary>
    /// Records the answers of a reply in <paramref name="conditions"/>, in Visual Studio.
    /// </summary>
    /// <param name="queries">The queries the request was created from.</param>
    /// <param name="response">The reply of the surface.</param>
    /// <param name="conditions">Receives the answers.</param>
    /// <param name="changed">Whether an answer differs from what <paramref name="conditions"/> knew before.</param>
    /// <returns><see langword="false"/> for a reply that does not answer the request; nothing is recorded then.</returns>
    public static bool TryApply(
        IReadOnlyList<XamlConditionQuery> queries,
        EvaluateXamlConditionsResponse? response,
        XamlConditionService conditions,
        out bool changed)
    {
        changed = false;
        if (!IsReplyTo(queries, response))
        {
            return false;
        }

        List<bool?>? outcomes = response!.Outcomes;
        if (outcomes is null)
        {
            // The reply to an empty request.
            return true;
        }

        for (int index = 0; index < queries.Count; index++)
        {
            bool? outcome = outcomes[index];
            XamlConditionQuery query = queries[index];
            if (!outcome.HasValue || query.Condition.Kind == XamlConditionKind.Empty)
            {
                continue;
            }

            // The answer is stored for the question, so the reply to a ...NotPresent condition is turned around.
            bool positive = query.Condition.IsNegated ? !outcome.Value : outcome.Value;
            changed |= conditions.SetOutcome(query.Key, positive);
        }

        return true;
    }
}
