// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIDesigner.Surface;

/// <summary>
/// Runs asynchronous operations sequentially while preserving their results and failures.
/// </summary>
internal static class QueuedOperation
{
    public static async Task<T> RunAsync<T>(Func<Action, bool> enqueue, Func<T> callback, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!enqueue(() =>
        {
            if (completion.Task.IsCompleted || cancellationToken.IsCancellationRequested)
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
            return await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            _ = completion.Task.Exception;

            throw;
        }
    }
}
