using Mixdeck.Core.Model;
using Mixdeck.Platform.Foreground;
using Mixdeck.Platform.Hotkeys;
using Mixdeck.Platform.Power;
using Mixdeck.Platform.Processes;
using Mixdeck.Platform.Taskbar;

int seconds = 20;
if (args.Length != 0 && (args.Length != 2 || args[0] != "--seconds" || !int.TryParse(args[1], out seconds) || seconds < 1 || seconds > 3600))
{
    Console.Error.WriteLine("Usage : Mixdeck.PlatformProbe [--seconds 1..3600]");
    return 2;
}

var placement = new TaskbarLocator().GetPrimary();
Console.WriteLine($"Barre : {placement.Edge}, {placement.TaskbarBounds} ; écran : {placement.MonitorBounds} ; travail : {placement.WorkArea} ; DPI : {placement.Dpi}");
using var foreground = new ForegroundWatcher();
using var processes = new ProcessWatcher();
using var hotkeys = new HotkeyService();
using var system = new SystemEventsWatcher();
foreground.ForegroundProcessChanged += (_, name) => Console.WriteLine($"Premier plan : {name ?? "inconnu"}");
processes.ProcessStarted += (_, e) => Console.WriteLine($"Lancé : {e.ProcessName} ({e.ProcessId})");
processes.ProcessExited += (_, e) => Console.WriteLine($"Arrêté : {e.ProcessName} ({e.ProcessId})");
hotkeys.Triggered += (_, e) => Console.WriteLine($"{HotkeyFormatter.Format(e.Binding)} : {(e.IsPressed ? "appui" : "relâchement")} ({e.Binding.Action})");
system.Resumed += (_, _) => Console.WriteLine("Reprise du système");
system.Suspending += (_, _) => Console.WriteLine("Mise en veille");
system.DisplayChanged += (_, _) => Console.WriteLine("Affichage modifié");
system.ThemeChanged += (_, _) => Console.WriteLine("Thème modifié");
hotkeys.Apply([
    new HotkeyBinding(HotkeyAction.PushToTalk, HotkeyModifiers.None, 0x7C),
    new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D)
]);
foreach (var failed in hotkeys.Failed) Console.Error.WriteLine($"Raccourci indisponible : {HotkeyFormatter.Format(failed)}");
foreground.Start();
processes.Start();
Console.WriteLine($"Observation pendant {seconds} s ; F13 = PTT, Ctrl+Alt+M = raccourci classique ; {processes.GetRunningProcessNames().Count} exécutables actifs.");
await Task.Delay(TimeSpan.FromSeconds(seconds), TimeProvider.System);
Console.WriteLine("Sonde terminée.");
return hotkeys.Failed.Count == 0 ? 0 : 1;
