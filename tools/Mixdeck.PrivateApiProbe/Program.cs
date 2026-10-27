using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mixdeck.Platform.Audio.AudioPolicy;
using Mixdeck.Platform.Taskbar;

namespace Mixdeck.PrivateApiProbe;

internal static partial class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static volatile bool _cancelled;

    [MTAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cancelled = true; };
        if (args.Length == 0) return Usage();
        try
        {
            Trace($"Windows={Environment.OSVersion.Version} ; processus={Environment.ProcessId} ; session={Process.GetCurrentProcess().SessionId} ; x64={Environment.Is64BitProcess}");
            var router = new AppAudioRouter(new ProbeLogger(), Trace);
            switch (args[0])
            {
                case "route-list" when args.Length == 1:
                    bool supported = router.IsSupported;
                    Trace($"Audio IsSupported={supported}");
                    var endpoints = router.GetRenderEndpoints();
                    Print(endpoints);
                    return supported && endpoints.Count > 0 ? 0 : 2;
                case "route-get" when args.Length == 2 && TryPid(args[1], out int readPid):
                    return ReadRoute(router, readPid);
                case "route-set" when args.Length == 3 && TryPid(args[1], out int writePid):
                    bool changed = router.SetEndpoint(writePid, args[2] == "default" ? null : args[2]);
                    Trace($"SetEndpoint accepté et relu={changed}");
                    int readResult = ReadRoute(router, writePid);
                    return changed ? readResult : 2;
                case "route-roundtrip" when args.Length == 2:
                    return RouteRoundtrip(router, args[1]);
                case "sound-loop" when args.Length == 1:
                    return SoundLoop();
                case "tray-promote" when args.Length == 3 && args[2] is "on" or "off":
                    bool promoted = TrayNotifyInterop.TryPromoteNow(args[1], args[2] == "on", Trace);
                    Print(new { PreferenceConfirmed = promoted, VisualConfirmationRequired = true });
                    return promoted ? 0 : 2;
                case "tray-get" when args.Length == 2 && TrayNotifyInterop.IsValidPath(args[1]):
                    using (var session = new TrayNotifyInterop.TraySession(Trace))
                    {
                        var icons = session.Read(args[1]);
                        Print(icons.Select(item => new { item.ExecutablePath, Window = $"0x{item.Window:X}", item.Id, item.Guid, item.Preference }));
                        return icons.Count > 0 ? 0 : 2;
                    }
                case "tray-roundtrip" when args.Length == 2 && TrayNotifyInterop.IsValidPath(args[1]):
                    return TrayRoundtrip(args[1]);
                case "qi-negative" when args.Length == 1:
                    return QueryUnsupportedInterface();
                default:
                    return Usage();
            }
        }
        catch (Exception ex)
        {
            Trace($"ÉCHEC contrôlé : HRESULT=0x{ex.HResult:X8}, {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    private static bool TryPid(string value, out int processId) => int.TryParse(value, out processId) && processId > 0;

    private static int ReadRoute(AppAudioRouter router, int processId)
    {
        bool read = router.TryGetEndpoints(processId, out string? multimedia, out string? console);
        Print(new { ProcessId = processId, Success = read, Multimedia = multimedia, Console = console });
        return read ? 0 : 2;
    }

    private static int RouteRoundtrip(AppAudioRouter router, string endpointId)
    {
        if (!router.GetRenderEndpoints().Any(endpoint => string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choisissez l'identifiant exact d'une sortie active retournée par route-list.");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable de sonde inconnu.");
        if (!Path.GetFileName(executable).Equals("Mixdeck.PrivateApiProbe.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lancez directement Mixdeck.PrivateApiProbe.exe pour cet essai.");
        using var child = Process.Start(new ProcessStartInfo(executable, "sound-loop")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Impossible de lancer le lecteur de test.");
        Trace($"Lecteur temporaire pid={child.Id}, exe={executable}");
        // Drainer les flux du lecteur, y compris en cas d'erreur, pour ne jamais bloquer sur son pipe.
        child.OutputDataReceived += (_, e) => { if (e.Data is not null) Trace($"Lecteur : {e.Data}"); };
        child.ErrorDataReceived += (_, e) => { if (e.Data is not null) Trace($"Lecteur : {e.Data}"); };
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        int result = 2;
        try
        {
            Observe("Son sur la sortie initiale", 3);
            if (child.HasExited) throw new InvalidOperationException("Le lecteur de test a échoué.");
            using var policy = new AudioPolicySession(Trace);
            string? multimedia = policy.Get(child.Id, AudioPolicyRole.Multimedia);
            string? console = policy.Get(child.Id, AudioPolicyRole.Console);
            Trace($"SAUVEGARDE pid={child.Id} multimedia={multimedia ?? "<default>"}, console={console ?? "<default>"}");
            EnsureDifferentOutput(multimedia, AudioPolicyRole.Multimedia, endpointId);
            EnsureDifferentOutput(console, AudioPolicyRole.Console, endpointId);
            try
            {
                if (!_cancelled && router.SetEndpoint(child.Id, endpointId) && ReadRoute(router, child.Id) == 0)
                {
                    Observe("Vérifiez que le son passe sur la sortie choisie (la relecture seule ne prouve pas la migration du son)", 10);
                    result = 0;
                }
            }
            finally
            {
                // Restaurer les DEUX rôles, même si leurs valeurs initiales différaient.
                bool restored = RestoreRoute(policy, child.Id, AudioPolicyRole.Multimedia, multimedia);
                restored &= RestoreRoute(policy, child.Id, AudioPolicyRole.Console, console);
                Trace($"Restauration audio confirmée={restored}");
                if (!restored) result = 3;
                Observe("Son sur la sortie initiale restaurée", 3);
            }
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            child.WaitForExit();
            Trace("Lecteur de test arrêté ; aucun autre processus arrêté.");
        }
        return result;
    }

    private static void EnsureDifferentOutput(string? persisted, AudioPolicyRole role, string target)
    {
        if (!AudioPolicyIds.TryUnpack(persisted, out string? current))
            throw new NotSupportedException("Format de préférence inconnu ; aucune écriture.");
        current ??= RenderEndpoints.ReadDefault(role, Trace);
        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Le lecteur utilise déjà cette sortie pour {role}. Choisissez une autre sortie active pour prouver la migration.");
    }

    private static bool RestoreRoute(IAudioPolicySession session, int processId, AudioPolicyRole role, string? original)
    {
        try
        {
            session.Set(processId, role, original);
            bool restored = string.Equals(session.Get(processId, role) ?? "", original ?? "", StringComparison.OrdinalIgnoreCase);
            Trace($"Restauration {role} confirmée={restored}");
            return restored;
        }
        catch (Exception ex)
        {
            Trace($"RESTAURATION REFUSÉE pid={processId}, rôle={role}, valeur={original ?? "<default>"}, HRESULT=0x{ex.HResult:X8}");
            return false;
        }
    }

    private static int TrayRoundtrip(string path)
    {
        using var session = new TrayNotifyInterop.TraySession(Trace);
        var before = session.Read(path);
        if (before.Count == 0 || before.Any(item => item.Preference > 2))
        {
            Trace("Icône active absente ou préférence inconnue ; aucune écriture.");
            return 2;
        }
        int result = 2;
        try
        {
            if (TrayNotifyInterop.Change(session, path, true, Trace))
            {
                Observe("Vérifiez l'icône près de l'horloge (on)", 8);
                if (!_cancelled && TrayNotifyInterop.Change(session, path, false, Trace))
                {
                    Observe("Vérifiez l'icône rangée derrière ^ (off)", 8);
                    result = 0;
                }
            }
        }
        finally
        {
            if (!TrayNotifyInterop.Restore(session, path, before, Trace)) result = 3;
        }
        Trace("Essai terminé ; confirmez visuellement l'état initial. L'Explorateur n'a pas été redémarré.");
        return result;
    }

    private static int QueryUnsupportedInterface()
    {
        using var apartment = new PrivateApiApartment();
        var classId = new Guid("bcde0395-e52f-467c-8e3d-c4579291692e");
        nint unknown = 0, unsupported = 0;
        try
        {
            PrivateApiNative.Check(PrivateApiNative.CoCreateInstance(classId, 0, 1, PrivateApiNative.UnknownId, out unknown), "CoCreateInstance MMDeviceEnumerator IUnknown", Trace);
            var missingIid = new Guid("f4729118-647d-4efe-9b77-370c3f7c1d87");
            int hr = Marshal.QueryInterface(unknown, in missingIid, out unsupported);
            Trace($"QueryInterface IID volontairement absent={missingIid:B}, HRESULT=0x{hr:X8}, pointeur=0x{unsupported:X}");
            Trace("Aucune méthode privée appelée sur cette interface.");
            return hr == PrivateApiNative.NoInterface && unsupported == 0 ? 0 : 2;
        }
        finally
        {
            if (unsupported != 0) Marshal.Release(unsupported);
            if (unknown != 0) Marshal.Release(unknown);
        }
    }

    private static int SoundLoop()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "chimes.wav");
        if (!File.Exists(file)) throw new FileNotFoundException("Son Windows introuvable.", file);
        while (!_cancelled)
        {
            // SND_FILENAME | SND_NODEFAULT, synchrone : seul le processus de sonde joue le son.
            if (!PlaySound(file, 0, 0x00020002)) throw new InvalidOperationException("PlaySound a refusé le son de test.");
            Thread.Sleep(150);
        }
        return 0;
    }

    private static void Observe(string message, int seconds)
    {
        Trace($"{message} — {seconds} s ; Ctrl+C déclenche la restauration.");
        for (int i = 0; i < seconds * 10 && !_cancelled; i++) Thread.Sleep(100);
    }

    private static void Trace(string value) => Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {value}");
    private static void Print<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static int Usage()
    {
        Console.Error.WriteLine("Commandes : route-list | route-get <pid> | route-set <pid> <endpointId|default> | route-roundtrip <endpointId> | sound-loop | tray-get <exe> | tray-promote <exe> on|off | tray-roundtrip <exe> | qi-negative");
        return 1;
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string sound, nint module, uint flags);

    private sealed class ProbeLogger : ILogger<AppAudioRouter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Trace($"{logLevel}: {formatter(state, exception)} {exception?.Message}");
        }
    }
}
