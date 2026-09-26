using Microsoft.UI.Dispatching;

namespace SystemCore.App.Services;

public sealed class UiDispatcher(DispatcherQueue queue)
{
    public bool Post(Action action)
    {
        if (queue.HasThreadAccess) { action(); return true; }
        return queue.TryEnqueue(() => action());
    }
}
