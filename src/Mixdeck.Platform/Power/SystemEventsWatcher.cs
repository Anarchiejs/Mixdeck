using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Platform.Interop;

namespace Mixdeck.Platform.Power;

public sealed class SystemEventsWatcher : IDisposable
{
    private readonly MessageWindow _window;
    private readonly ILogger<SystemEventsWatcher> _logger;

    public SystemEventsWatcher(ILogger<SystemEventsWatcher>? logger = null)
    {
        _logger = logger ?? NullLogger<SystemEventsWatcher>.Instance;
        _window = new MessageWindow("Mixdeck système", OnMessage, receiveBroadcasts: true);
    }

    public event EventHandler? Resumed;
    public event EventHandler? Suspending;
    public event EventHandler? DisplayChanged;
    public event EventHandler? ThemeChanged;

    private nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case 0x218:
                    if (wParam == 0x12) Resumed?.Invoke(this, EventArgs.Empty);
                    else if (wParam == 4) Suspending?.Invoke(this, EventArgs.Empty);
                    return 1;
                case 0x7E:
                case 0x2E0: DisplayChanged?.Invoke(this, EventArgs.Empty); break;
                case 0x1A when lParam != 0 && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal):
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Erreur du gestionnaire d'événement système."); }
        return null;
    }

    public void Dispose() => _window.Dispose();
}
