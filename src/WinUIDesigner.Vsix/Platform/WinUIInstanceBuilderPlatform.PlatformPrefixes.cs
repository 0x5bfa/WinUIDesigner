// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders;

namespace WinUIDesigner.Platform;

/// <summary>
/// Lets the XAML cleaner keep an element whose type it cannot resolve, which is what an element under an
/// Uno platform prefix such as <c>&lt;android:Border&gt;</c> is to the designer.
/// </summary>
internal partial class WinUIInstanceBuilderPlatform
{
    // The cleaner runs synchronously on the thread that calls it, so the scope is kept per thread: a document
    // cleaned on another thread at the same time is not affected.
    [ThreadStatic]
    private static int _keepUnresolvedElements;

    /// <summary>
    /// Makes the XAML cleaner keep the elements whose type it cannot resolve, on the calling thread and until
    /// the returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// The cleaner replaces such an element with a fallback control and removes its children. It asks this
    /// platform for the control to use; answering that there is none leaves the element as it was written.
    /// The cleaner hands over one shared stand-in type, not the element, so every unresolved element of the
    /// text is kept, not only those under a platform prefix.
    /// </remarks>
    public static IDisposable KeepUnresolvedElements()
    {
        _keepUnresolvedElements++;
        return new KeepUnresolvedElementsScope();
    }

    public override FallbackTypeInfo GetFallbackTypeInfo(IType typeToReplace, bool forLayoutFault)
    {
        if (_keepUnresolvedElements > 0 && !forLayoutFault && FallbackTypeUtility.DoesTypeAlwaysNeedFallback(typeToReplace))
        {
            return FallbackTypeInfo.Invalid;
        }

        return base.GetFallbackTypeInfo(typeToReplace, forLayoutFault);
    }

    private sealed class KeepUnresolvedElementsScope : IDisposable
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private bool _isDisposed;

        public void Dispose()
        {
            // The scope belongs to the thread that opened it: it is closed once, and only there.
            if (!_isDisposed && _threadId == Environment.CurrentManagedThreadId)
            {
                _isDisposed = true;
                _keepUnresolvedElements--;
            }
        }
    }
}
