global using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Dispatching;

internal delegate void DispatcherQueueShutdownStartingEventHandler(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args);

internal sealed class DispatcherQueue(Func<Action, bool> enqueue)
{
    public bool HasThreadAccess { get; set; }

    public event DispatcherQueueShutdownStartingEventHandler? ShutdownStarting;

    public bool TryEnqueue(Action callback) => enqueue(callback);

    public void BeginShutdown() => ShutdownStarting?.Invoke(this, new DispatcherQueueShutdownStartingEventArgs());
}

internal sealed class DispatcherQueueShutdownStartingEventArgs : EventArgs
{
}
