using Microsoft.Extensions.DependencyInjection;
using Mixdeck.App.Views.Osd;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.App.Services;

/// <summary>Affiche l'OSD depuis n'importe quel thread ; la fenêtre est créée à la première utilisation.</summary>
public sealed class OsdService(IServiceProvider services, IUiDispatcher ui) : IOsdService
{
    private OsdWindow? _window;

    public void Show(string text, string glyph, bool isWarning = false) =>
        ui.Post(() =>
        {
            _window ??= services.GetRequiredService<OsdWindow>();
            _window.Show(text, glyph, isWarning);
        });
}
