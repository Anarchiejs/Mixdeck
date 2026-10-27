using Microsoft.UI.Dispatching;

namespace Mixdeck.App.Services;

/// <summary>Repasse sur le thread de l'interface. Les événements du mixeur arrivent sur le thread Voicemeeter.</summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    void Post(Action action);
}

public sealed class UiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public void Post(Action action)
    {
        if (queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }
}
