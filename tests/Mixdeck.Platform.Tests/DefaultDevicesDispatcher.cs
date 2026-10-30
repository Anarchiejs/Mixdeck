// The production interface lives alongside WinUI's UiDispatcher. Keep this test contract identical
// so the production view model can be compiled here without loading the WinUI runtime.
namespace Mixdeck.App.Services;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    void Post(Action action);
}
