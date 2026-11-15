// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Reports what <see cref="XamlNamespaceRewriter.Rewrite"/> changed in a document.
/// </summary>
internal sealed class XamlNamespaceRewriteResult
{
    internal XamlNamespaceRewriteResult(
        int droppedElements,
        int droppedAttributes,
        int normalizedMembers,
        int repointedDeclarations,
        IReadOnlyList<string> diagnostics)
    {
        DroppedElements = droppedElements;
        DroppedAttributes = droppedAttributes;
        NormalizedMembers = normalizedMembers;
        RepointedDeclarations = repointedDeclarations;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the result of a run that found nothing to rewrite.
    /// </summary>
    public static XamlNamespaceRewriteResult Unchanged { get; } = new(0, 0, 0, 0, Array.Empty<string>());

    /// <summary>
    /// Gets a value indicating whether the document was modified.
    /// </summary>
    public bool Changed => DroppedElements != 0 || DroppedAttributes != 0 || NormalizedMembers != 0 || RepointedDeclarations != 0;

    /// <summary>
    /// Gets the number of removed elements. An element removed together with its parent is not counted.
    /// </summary>
    public int DroppedElements { get; }

    /// <summary>
    /// Gets the number of removed attributes.
    /// </summary>
    public int DroppedAttributes { get; }

    /// <summary>
    /// Gets the number of elements and attributes that were renamed to their bare namespace.
    /// </summary>
    public int NormalizedMembers { get; }

    /// <summary>
    /// Gets the number of <c>xmlns</c> declarations that were re-pointed to their bare namespace.
    /// </summary>
    public int RepointedDeclarations { get; }

    /// <summary>
    /// Gets the diagnostic messages of the run, in the order they were reported.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; }
}
