using Mixdeck.Core.Model;

namespace Mixdeck.Core.Abstractions;

public sealed class HotkeyEventArgs(HotkeyBinding binding, bool isPressed) : EventArgs
{
    public HotkeyBinding Binding { get; } = binding;

    /// <summary>true à l'appui ; false au relâchement (uniquement pour PushToTalk).</summary>
    public bool IsPressed { get; } = isPressed;
}

/// <summary>Raccourcis globaux (RegisterHotKey) et push-to-talk (crochet clavier bas niveau).</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Remplace tous les raccourcis enregistrés.</summary>
    void Apply(IReadOnlyList<HotkeyBinding> bindings);

    /// <summary>Raccourcis qui n'ont pas pu être enregistrés (déjà pris par une autre application).</summary>
    IReadOnlyList<HotkeyBinding> Failed { get; }

    /// <summary>Levé sur un thread quelconque ; le gestionnaire doit rendre la main vite.</summary>
    event EventHandler<HotkeyEventArgs>? Triggered;
}

public sealed class ProcessEventArgs(string processName, int processId) : EventArgs
{
    /// <summary>Nom de l'exécutable avec extension, ex. "FL64.exe".</summary>
    public string ProcessName { get; } = processName;

    public int ProcessId { get; } = processId;
}

/// <summary>Processus lancés et arrêtés, sans sondage en boucle.</summary>
public interface IProcessWatcher : IDisposable
{
    event EventHandler<ProcessEventArgs>? ProcessStarted;

    event EventHandler<ProcessEventArgs>? ProcessExited;

    /// <summary>Noms des exécutables qui tournent maintenant (sans doublons, insensible à la casse).</summary>
    IReadOnlyCollection<string> GetRunningProcessNames();

    void Start();
}

/// <summary>Fenêtre au premier plan (SetWinEventHook EVENT_SYSTEM_FOREGROUND).</summary>
public interface IForegroundWatcher : IDisposable
{
    /// <summary>Nom de l'exécutable de la nouvelle fenêtre au premier plan, null si inconnu.</summary>
    event EventHandler<string?>? ForegroundProcessChanged;

    string? Current { get; }

    void Start();
}

/// <summary>Démarrage avec Windows (clé HKCU\...\Run).</summary>
public interface IAutoStart
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
