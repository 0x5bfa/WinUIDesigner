// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Provides awaitable operations for work dispatched to the WinUI UI thread.
/// </summary>
internal static class DispatcherOperation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static T Invoke<T>(DispatcherQueue queue, Func<T> callback, CancellationToken cancellationToken)
    {
        return InvokeAsync(queue, callback, cancellationToken).GetAwaiter().GetResult();
    }

    public static async Task<T> InvokeAsync<T>(DispatcherQueue queue, Func<T> callback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (queue.HasThreadAccess)
        {
            return callback();
        }

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        void OnShutdown(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args)
        {
            shutdown.Cancel();
        }

        queue.ShutdownStarting += OnShutdown;

        try
        {
            return await QueuedOperation.RunAsync(action => queue.TryEnqueue(() => action()), callback, Timeout, shutdown.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            queue.ShutdownStarting -= OnShutdown;
        }
    }
}
