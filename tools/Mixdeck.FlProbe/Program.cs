using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Mixdeck.FlStudio;

Console.OutputEncoding = Encoding.UTF8;
var options = args.ToList();
var outName = TakeOption("--out") ?? "Mixdeck to FL";
var inName = TakeOption("--in") ?? "FL to Mixdeck";
if (options.Count == 0 || options[0] is "help" or "-h" or "--help")
{
    Console.WriteLine("""
        Mixdeck.FlProbe [--out <port>] [--in <port>] <commande>
          ports                    ports MIDI Windows
          ping                     version de l'API et du protocole
          count                    nombre de pistes du mixeur
          list                     liste des pistes utilisées
          watch                    messages reçus pendant 30 s
          get <piste>              état d'une piste (0 = Master)
          select <piste>           sélectionner une piste
          mute <piste> <0|1>       couper ou rétablir
          vol <piste> <0..1>       volume (0,8 = 0 dB)
          slots <piste> <0|1>      effets de la piste
          slot <piste> <n> <0|1>   slot d'effet (0 à 9)
          preset <piste> <n> <next|prev>
        Noms de ports : recherche par morceau du nom.
        """);
    return 0;
}
var ports = new WinMidiPorts();
try
{
    if (options[0] == "ports")
    {
        Console.WriteLine("Sorties : " + string.Join(", ", ports.OutputNames()));
        Console.WriteLine("Entrées : " + string.Join(", ", ports.InputNames()));
        return 0;
    }
    int Number(int i) => int.Parse(options[i], CultureInfo.InvariantCulture);
    bool Bit(int i) => Number(i) switch { 0 => false, 1 => true, _ => throw new ArgumentException("0 ou 1 attendu") };
    byte[] message = options[0] switch
    {
        "ping" => FlProtocol.Ping(), "count" => FlProtocol.GetCount(),
        "list" or "watch" => FlProtocol.List(), "get" => FlProtocol.GetTrack(Number(1)),
        "select" => FlProtocol.Select(Number(1)), "mute" => FlProtocol.Mute(Number(1), Bit(2)),
        "vol" => FlProtocol.Volume(Number(1), Volume(options[2])),
        "slots" => FlProtocol.SlotsEnabled(Number(1), Bit(2)),
        "slot" => FlProtocol.Slot(Number(1), Number(2), Bit(3)),
        "preset" => FlProtocol.Preset(Number(1), Number(2), options[3] switch
        {
            "next" => true, "prev" => false, _ => throw new ArgumentException("next ou prev attendu"),
        }),
        _ => throw new ArgumentException("Commande inconnue (voir help)."),
    };
    using var replies = new BlockingCollection<byte[]>();
    using var input = ports.OpenInput(inName, replies.Add);
    using var output = ports.OpenOutput(outName);
    output.SendSysex(message);
    var watching = options[0] == "watch";
    var listing = options[0] == "list";
    var until = Environment.TickCount64 + (watching ? 30000 : 1500);
    var received = false;
    while (Environment.TickCount64 < until)
    {
        if (!replies.TryTake(out var reply, (int)Math.Max(1, until - Environment.TickCount64))) break;
        var decoded = FlProtocol.Decode(reply);
        if (decoded is null) continue;
        received = true;
        Console.WriteLine(decoded is FlTrackState state
            ? $"Piste {state.Track.Index} « {state.Track.Name} » : volume {state.Track.Volume:0.000}, muet {state.Track.Muted}, effets {state.Track.SlotsEnabled}, slots [{string.Join(", ", state.Track.Slots)}]"
            : decoded);
        if (!watching && (!listing || decoded is FlListEnd)) return 0;
    }
    if (watching) return 0;
    Console.Error.WriteLine(received ? "Réponse incomplète (ListEnd absent)." : "Pas de réponse : vérifier FL Studio et le script Mixdeck.");
    return 4;
}
catch (Exception e) when (e is FormatException or ArgumentException or IndexOutOfRangeException or OverflowException)
{
    Console.Error.WriteLine($"Commande invalide : {e.Message}");
    return 2;
}
catch (MidiPortException e)
{
    Console.Error.WriteLine(e.Message);
    return 3;
}

string? TakeOption(string name)
{
    var i = options.IndexOf(name);
    if (i < 0 || i + 1 >= options.Count) return null;
    var value = options[i + 1];
    options.RemoveRange(i, 2);
    return value;
}
static float Volume(string text)
{
    var value = float.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);
    if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentException("Volume attendu entre 0 et 1.");
    return value;
}
