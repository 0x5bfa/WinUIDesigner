// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.DesignTime;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Keeps the design-time options Visual Studio sends with each CreateSurface (516) request, per document.
/// </summary>
internal sealed partial class SurfaceService
{
    // Requests arrive on the protocol thread; documents are built on the UI thread.
    private readonly object _designTimeOptionsLock = new();
    private readonly Dictionary<int, DesignTimeOptions> _designTimeOptions = [];
    private DesignTimeOptions _latestDesignTimeOptions = DesignTimeOptions.Default;

    /// <summary>
    /// Returns the options the document <paramref name="documentId"/> is previewed with.
    /// </summary>
    /// <remarks>
    /// A document Visual Studio never sent a request for gets the options of the latest request: one surface
    /// process serves one project, whose documents all share their options. Safe to call on any thread.
    /// </remarks>
    internal DesignTimeOptions GetDesignTimeOptions(int documentId)
    {
        lock (_designTimeOptionsLock)
        {
            return _designTimeOptions.TryGetValue(documentId, out DesignTimeOptions? options) ? options : _latestDesignTimeOptions;
        }
    }

    // Records the options of a request for every document it builds: the target and the resource documents.
    // The options never decide whether a document can be built, so a failure here only costs the options.
    private void StoreDesignTimeOptions(CreateSurfaceRequestContract request)
    {
        try
        {
            string? text = request.SerializedDesignTimeOptions;
            bool isUnderstood = DesignTimeOptions.TryParse(text, out DesignTimeOptions options);

            lock (_designTimeOptionsLock)
            {
                _latestDesignTimeOptions = options;
                foreach (CreateDocumentInfo? document in new[] { request.Document, request.Application, request.DesignTimeResources })
                {
                    if (document is not null && document.DocumentId != 0)
                    {
                        _designTimeOptions[document.DocumentId] = options;
                    }
                }
            }

            int documentId = request.Document?.DocumentId ?? 0;
            if (isUnderstood)
            {
                WinUIDesignerLogger.LogInformation("Surface", $"Design-time options for document {documentId}: {options}.");
            }
            else if (string.IsNullOrWhiteSpace(text))
            {
                WinUIDesignerLogger.LogInformation("Surface", $"No design-time options came with document {documentId}; using {options}.");
            }
            else
            {
                WinUIDesignerLogger.LogWarning("Surface", $"The design-time options '{text}' for document {documentId} are not understood; using {options}.");
            }
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Surface", "The design-time options of a CreateSurface request could not be stored.", exception);
        }
    }

    // Visual Studio closed the document; a later document can reuse neither its identity nor its options.
    private void ForgetDesignTimeOptions(CloseDocumentRequestInfo request)
    {
        lock (_designTimeOptionsLock)
        {
            _designTimeOptions.Remove(request.DocumentId);
        }
    }

    private void ClearDesignTimeOptions()
    {
        lock (_designTimeOptionsLock)
        {
            _designTimeOptions.Clear();
            _latestDesignTimeOptions = DesignTimeOptions.Default;
        }
    }
}
