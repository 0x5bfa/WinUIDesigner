// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Provides awaitable operations for work dispatched to the WinUI UI thread.
/// </summary>
internal static class DispatcherOperation
{
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(30);

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
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!queue.TryEnqueue(() =>
            {
                if (completion.Task.IsCompleted || shutdown.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    completion.TrySetResult(callback());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
            {
                throw new OperationCanceledException("Unable to enqueue the designer request.");
            }

            try
            {
                return await completion.Task.WaitAsync(_Timeout, shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                _ = completion.Task.Exception;

                throw;
            }
        }
        finally
        {
            queue.ShutdownStarting -= OnShutdown;
        }
    }
}
