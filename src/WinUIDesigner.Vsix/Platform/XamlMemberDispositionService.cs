// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Answers what the namespace policies decide for a document node or one of its properties,
/// judged by the prefix it was written with.
/// </summary>
/// <remarks>
/// Construction actions carry CLR names only, so on that path the policies have to be applied here, on
/// the document model. A document without policy namespaces is recognized from one scan of its text per
/// text version and never has the source of its nodes read. Instances are safe to use from any thread.
/// </remarks>
internal sealed class XamlMemberDispositionService
{
    private readonly ConditionalWeakTable<IDocumentRoot, DocumentCache> _documents = new();
    private volatile string _targetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;
    private volatile XamlConditionEvaluator? _evaluateCondition;
    private int _generation;

    /// <summary>
    /// Gets or sets the platform whose members are kept. Changing it discards the cached verdicts.
    /// </summary>
    public string TargetPlatform
    {
        get => _targetPlatform;
        set
        {
            string platform = string.IsNullOrEmpty(value) ? UnoPlatformPrefixes.DefaultTargetPlatform : value;
            if (!string.Equals(_targetPlatform, platform, StringComparison.Ordinal))
            {
                _targetPlatform = platform;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Gets or sets the evaluator for conditional namespaces. Without one, their members are left untouched.
    /// Changing it discards the cached verdicts.
    /// </summary>
    public XamlConditionEvaluator? EvaluateCondition
    {
        get => _evaluateCondition;
        set
        {
            if (!ReferenceEquals(_evaluateCondition, value))
            {
                _evaluateCondition = value;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Discards the cached verdicts, for example after the outcome of a condition changed.
    /// </summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
    }

    /// <summary>
    /// Returns the namespace facts of the current text of <paramref name="document"/>. They are scanned once
    /// per text version; a document without text yields <see cref="XamlNamespaceScanResult.Empty"/>.
    /// </summary>
    public XamlNamespaceScanResult GetNamespaces(IDocumentRoot? document)
    {
        try
        {
            return GetState(document)?.Namespaces ?? XamlNamespaceScanResult.Empty;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlNamespaceScanResult.Empty;
        }
    }

    /// <summary>
    /// Returns the verdict for the element <paramref name="node"/> was written as.
    /// Nodes without source, and failures of any kind, yield <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition GetNodeDisposition(DocumentNode? node)
    {
        if (node is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        try
        {
            ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts = GetState(node.DocumentRoot)?.Verdicts;
            object? site = verdicts is null ? null : node.SourceContext;
            if (verdicts is null || site is null)
            {
                return XamlPrefixDisposition.Leave;
            }

            if (!verdicts.TryGetValue(site, out XamlPrefixDisposition verdict))
            {
                verdict = Classify(XamlSourcePrefixReader.ReadElementPrefix(node));
                verdicts[site] = verdict;
            }

            return verdict;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlPrefixDisposition.Leave;
        }
    }

    /// <summary>
    /// Returns the verdict for <paramref name="property"/> as it was written on <paramref name="parentNode"/>,
    /// as an attribute or as a property element. Properties without source, and failures of any kind,
    /// yield <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition GetPropertyDisposition(DocumentCompositeNode? parentNode, IProperty? property)
    {
        if (parentNode is null || property is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        try
        {
            // The source reference of the attribute or property element identifies this place in the text.
            ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts = GetState(parentNode.DocumentRoot)?.Verdicts;
            object? site = verdicts is null ? null : parentNode.GetContainerContext(property);
            if (verdicts is null || site is null)
            {
                return XamlPrefixDisposition.Leave;
            }

            if (!verdicts.TryGetValue(site, out XamlPrefixDisposition verdict))
            {
                verdict = Classify(XamlSourcePrefixReader.ReadPropertyPrefix(parentNode, property));
                verdicts[site] = verdict;
            }

            return verdict;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlPrefixDisposition.Leave;
        }
    }

    private static void LogFailure(Exception exception)
    {
        // This runs for every property that is built: a failure costs a skipped policy and, unless
        // verbose logging is on, nothing else.
        WinUIDesignerLogger.LogTrace("Platform", $"Reading the namespace policy of a document node failed: {exception}");
    }

    /// <summary>
    /// Returns the cached state of the current text of a document, or <see langword="null"/> when the
    /// document has no text.
    /// </summary>
    private DocumentState? GetState(IDocumentRoot? root)
    {
        IMarkupTextBuffer? buffer = root?.TextBuffer;
        if (root is null || buffer is null)
        {
            return null;
        }

        DocumentCache cache = _documents.GetValue(root, static _ => new DocumentCache());
        int textVersion = buffer.Version;
        int textLength = buffer.Length;
        int generation = Volatile.Read(ref _generation);

        DocumentState? state = Volatile.Read(ref cache.State);
        if (state is null || state.TextVersion != textVersion || state.TextLength != textLength || state.Generation != generation)
        {
            // One pass over the text each time it changes: the namespaces of a document are declared in its
            // text. Not every buffer counts versions (SimpleTextBuffer always reports 0), so the length
            // takes part in telling one text from the next.
            XamlNamespaceScanResult namespaces = XamlNamespaceScanner.Scan(ReadText(buffer, textLength));
            bool hasPolicyNamespaces = XamlNamespacePolicies.MayAffect(namespaces, _targetPlatform, _evaluateCondition);
            state = new DocumentState(
                textVersion,
                textLength,
                generation,
                namespaces,
                hasPolicyNamespaces ? new ConcurrentDictionary<object, XamlPrefixDisposition>(ReferenceComparer.Instance) : null);
            Volatile.Write(ref cache.State, state);
        }

        return state;
    }

    private static string? ReadText(IMarkupTextBuffer buffer, int length)
    {
        try
        {
            return buffer.GetText(0, length);
        }
        catch (Exception ex)
        {
            // The text changed while it was being read. The state built from this result is replaced on
            // the next call, which sees the new text; until then the document has no policy namespaces.
            LogFailure(ex);
            return null;
        }
    }

    /// <summary>
    /// Returns the verdict for members written under <paramref name="source"/>. A prefix that is unknown
    /// or not bound to a namespace yields <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition Classify(XamlSourcePrefix source)
    {
        if (!source.IsKnown || source.Namespace is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        XamlNamespaceUri uri = source.Uri;
        Func<string, bool?>? evaluate = null;
        XamlConditionEvaluator? evaluator = _evaluateCondition;
        if (evaluator is not null && uri.IsConditional)
        {
            evaluate = condition => evaluator(condition, source.ResolveNamespace);
        }

        return XamlNamespacePolicies.Classify(source.Prefix, uri, source.IsIgnorable, _targetPlatform, evaluate);
    }

    /// <summary>
    /// Holds the state of one document; the state is replaced as a whole when the text or the policy inputs change.
    /// </summary>
    private sealed class DocumentCache
    {
        public DocumentState? State;
    }

    /// <summary>
    /// What is known about one text of a document.
    /// </summary>
    private sealed class DocumentState(
        int textVersion,
        int textLength,
        int generation,
        XamlNamespaceScanResult namespaces,
        ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts)
    {
        public int TextVersion { get; } = textVersion;

        public int TextLength { get; } = textLength;

        public int Generation { get; } = generation;

        public XamlNamespaceScanResult Namespaces { get; } = namespaces;

        // Keyed by source reference, of which there is one per element and per attribute in the text.
        // Null when no namespace of the document is subject to a policy: nothing needs to be looked up then.
        public ConcurrentDictionary<object, XamlPrefixDisposition>? Verdicts { get; } = verdicts;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static ReferenceComparer Instance { get; } = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        int IEqualityComparer<object>.GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
