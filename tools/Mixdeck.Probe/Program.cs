using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Voicemeeter;

Console.OutputEncoding = Encoding.UTF8;
var simulated = args.Contains("--sim", StringComparer.OrdinalIgnoreCase);
var once = args.Contains("--once", StringComparer.OrdinalIgnoreCase);
await using var mixer = simulated
    ? VoicemeeterMixer.CreateSimulated(NullLoggerFactory.Instance)
    : VoicemeeterMixer.CreateDefault(NullLoggerFactory.Instance);
await mixer.StartAsync();
PrintStatus();
if (once)
{
    PrintSnapshot();
    return;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
mixer.LevelsEnabled = true;
Console.WriteLine("m : basculer le micro · q : quitter");
var canRedraw = !Console.IsOutputRedirected;
var firstRow = canRedraw ? Console.CursorTop : 0;
try
{
    while (!cancellation.IsCancellationRequested)
    {
        if (!Console.IsInputRedirected && Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Q) break;
            if (key == ConsoleKey.M) mixer.SetFloat(VmParam.StripMute(0), mixer.Snapshot.Strips[0].Mute ? 0 : 1);
        }
        if (canRedraw)
        {
            // Un terminal trop petit conserve l'affichage en flux plutôt que de lever une exception.
            if (firstRow + 11 < Console.BufferHeight) Console.SetCursorPosition(0, firstRow);
            else canRedraw = false;
        }
        PrintStatus();
        var frame = mixer.Levels;
        var snapshot = mixer.Snapshot;
        for (var i = 0; i < BananaLayout.StripCount; i++)
            Meter(string.IsNullOrWhiteSpace(snapshot.Strips[i].Label) ? BananaLayout.DefaultStripLabels[i] : snapshot.Strips[i].Label, frame.Strips[i]);
        for (var i = 0; i < BananaLayout.BusCount; i++) Meter(BananaLayout.BusNames[i], frame.Buses[i]);
        await Task.Delay(canRedraw ? 50 : 250, cancellation.Token);
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }

void PrintStatus() => Console.WriteLine($"État : {mixer.State} · Type : {mixer.VoicemeeterType} · Version : {FormatVersion(mixer.VoicemeeterVersion)}          ");
void PrintSnapshot()
{
    var snapshot = mixer.Snapshot;
    Console.WriteLine($"Snapshot · révision {snapshot.Revision}");
    foreach (var strip in snapshot.Strips)
        Console.WriteLine($"Entrée {strip.Index + 1} · {strip.Label} · gain {strip.Gain:0.0} dB · mute {strip.Mute} · solo {strip.Solo} · pan {strip.PanX:0.00} · routes {string.Join(',', strip.Routes.Select((on, i) => on ? BananaLayout.BusNames[i] : "-"))} · EQ [{string.Join(", ", strip.EqGains)}]");
    foreach (var bus in snapshot.Buses)
        Console.WriteLine($"Bus {bus.Name} · {bus.Label} · gain {bus.Gain:0.0} dB · mute {bus.Mute} · mono {bus.Mono} · EQ {bus.EqOn}");
}
void Meter(string label, StereoLevel level)
{
    var db = level.Max > 0 ? 20 * MathF.Log10(level.Max) : -90;
    var length = (int)Math.Clamp((db + 60) / 60 * 30, 0, 30);
    var displayLabel = label.Length > 14 ? label[..14] : label;
    Console.WriteLine($"{displayLabel,-14} {new string('█', length)}{new string(' ', 30 - length)} {db,6:0.0} dB   ");
}
static string FormatVersion(int version) => $"{(version >> 24) & 255}.{(version >> 16) & 255}.{(version >> 8) & 255}.{version & 255}";
