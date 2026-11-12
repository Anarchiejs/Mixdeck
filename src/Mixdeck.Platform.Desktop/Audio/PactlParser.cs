using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mixdeck.Platform.Desktop.Audio;

internal readonly record struct ParsedDevice(string Id, string Name, int Index, float Volume, bool Mute);

internal readonly record struct ParsedSession(
    string Id,
    int Index,
    int ProcessId,
    string ProcessName,
    string DisplayName,
    float Volume,
    bool Mute,
    string? Sink);

/// <summary>
/// Lit la sortie de <c>pactl</c>, en JSON (PulseAudio 16+ et PipeWire) ou en texte.
/// Les deux formes sont celles que <c>pactl list</c> produit vraiment.
/// </summary>
internal static partial class PactlParser
{
    public static bool LooksLikeJson(string text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character)) continue;
            return character is '{' or '[';
        }
        return false;
    }

    public static IReadOnlyList<ParsedDevice> ParseDevices(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!LooksLikeJson(text)) return ParseDevicesText(text);
        try { return ParseDevicesJson(text); }
        catch (JsonException) { return ParseDevicesText(text); }
    }

    public static IReadOnlyList<ParsedSession> ParseSessions(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!LooksLikeJson(text)) return ParseSessionsText(text);
        try { return ParseSessionsJson(text); }
        catch (JsonException) { return ParseSessionsText(text); }
    }

    public static (string? Sink, string? Source) ParseDefaults(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        if (LooksLikeJson(text))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                return (StringValue(root, "default_sink_name"), StringValue(root, "default_source_name"));
            }
            catch (JsonException)
            {
                // Le texte ci-dessous couvre « Default Sink: ».
            }
        }

        string? sink = null;
        string? source = null;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith("Default Sink:", StringComparison.Ordinal))
                sink = line["Default Sink:".Length..].Trim();
            else if (line.StartsWith("Default Source:", StringComparison.Ordinal))
                source = line["Default Source:".Length..].Trim();
        }
        return (EmptyToNull(sink), EmptyToNull(source));
    }

    private static IReadOnlyList<ParsedDevice> ParseDevicesJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var devices = new List<ParsedDevice>();
        foreach (var item in Items(document.RootElement))
        {
            var index = IntValue(item, "index") ?? devices.Count;
            var name = StringValue(item, "name");
            var description = StringValue(item, "description");
            var id = string.IsNullOrWhiteSpace(name) ? index.ToString(CultureInfo.InvariantCulture) : name;
            var label = string.IsNullOrWhiteSpace(description) ? id : description;
            devices.Add(new ParsedDevice(id, label, index, VolumeOf(item), IsMuted(item)));
        }
        return devices;
    }

    private static IReadOnlyList<ParsedSession> ParseSessionsJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var sessions = new List<ParsedSession>();
        foreach (var item in Items(document.RootElement))
        {
            var index = IntValue(item, "index");
            if (index is not int id) continue;
            item.TryGetProperty("properties", out var properties);
            var binary = Property(properties, "application.process.binary");
            var application = Property(properties, "application.name");
            var processText = Property(properties, "application.process.id");
            var processId = int.TryParse(processText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            var processName = FirstNonEmpty(binary, application);
            var display = FirstNonEmpty(application, binary) ?? "Application";
            sessions.Add(new ParsedSession(
                id.ToString(CultureInfo.InvariantCulture),
                id,
                processId,
                processName ?? "",
                display,
                VolumeOf(item),
                IsMuted(item),
                SinkOf(item)));
        }
        return sessions;
    }

    private static IReadOnlyList<ParsedDevice> ParseDevicesText(string text)
    {
        var devices = new List<ParsedDevice>();
        int? index = null;
        string? name = null;
        string? description = null;
        var volume = 1f;
        var mute = false;
        void Flush()
        {
            if (index is int parsed)
            {
                var id = string.IsNullOrWhiteSpace(name) ? parsed.ToString(CultureInfo.InvariantCulture) : name;
                var label = string.IsNullOrWhiteSpace(description) ? id : description;
                devices.Add(new ParsedDevice(id, label, parsed, volume, mute));
            }
            index = null;
            name = null;
            description = null;
            volume = 1f;
            mute = false;
        }

        foreach (var line in Lines(text))
        {
            if (line.StartsWith("Sink #", StringComparison.Ordinal) || line.StartsWith("Source #", StringComparison.Ordinal))
            {
                Flush();
                var hash = line.IndexOf('#');
                if (int.TryParse(line[(hash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    index = parsed;
                continue;
            }
            if (index is null) continue;
            if (line.StartsWith("Name:", StringComparison.Ordinal)) name = line["Name:".Length..].Trim();
            else if (line.StartsWith("Description:", StringComparison.Ordinal)) description = line["Description:".Length..].Trim();
            else if (line.StartsWith("Mute:", StringComparison.Ordinal)) mute = line.Contains("yes", StringComparison.OrdinalIgnoreCase);
            else if (line.StartsWith("Volume:", StringComparison.Ordinal)) volume = AveragePercents(line);
        }
        Flush();
        return devices;
    }

    private static IReadOnlyList<ParsedSession> ParseSessionsText(string text)
    {
        var sessions = new List<ParsedSession>();
        int? index = null;
        string? sink = null;
        string? application = null;
        string? binary = null;
        var processId = 0;
        var volume = 1f;
        var mute = false;
        void Flush()
        {
            if (index is int parsed)
            {
                var processName = FirstNonEmpty(binary, application) ?? "";
                var display = FirstNonEmpty(application, binary) ?? "Application";
                sessions.Add(new ParsedSession(
                    parsed.ToString(CultureInfo.InvariantCulture), parsed, processId, processName, display, volume, mute, sink));
            }
            index = null;
            sink = null;
            application = null;
            binary = null;
            processId = 0;
            volume = 1f;
            mute = false;
        }

        foreach (var line in Lines(text))
        {
            if (line.StartsWith("Sink Input #", StringComparison.Ordinal))
            {
                Flush();
                var hash = line.IndexOf('#');
                if (int.TryParse(line[(hash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    index = parsed;
                continue;
            }
            if (index is null) continue;
            if (line.StartsWith("Sink:", StringComparison.Ordinal)) sink = line["Sink:".Length..].Trim();
            else if (line.StartsWith("Mute:", StringComparison.Ordinal)) mute = line.Contains("yes", StringComparison.OrdinalIgnoreCase);
            else if (line.StartsWith("Volume:", StringComparison.Ordinal)) volume = AveragePercents(line);
            else if (line.StartsWith("application.name", StringComparison.Ordinal)) application = Quoted(line);
            else if (line.StartsWith("application.process.binary", StringComparison.Ordinal)) binary = Quoted(line);
            else if (line.StartsWith("application.process.id", StringComparison.Ordinal))
            {
                var raw = Quoted(line);
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) processId = parsed;
            }
        }
        Flush();
        return sessions;
    }

    private static IEnumerable<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object) yield return item;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object) yield break;
        if (root.TryGetProperty("name", out _) || root.TryGetProperty("volume", out _) || root.TryGetProperty("properties", out _))
        {
            yield return root;
            yield break;
        }
        foreach (var property in root.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object) yield return property.Value;
    }

    private static float VolumeOf(JsonElement item)
    {
        if (!item.TryGetProperty("volume", out var volume) || volume.ValueKind != JsonValueKind.Object) return 1f;
        if (volume.TryGetProperty("value", out var flat) && flat.TryGetDouble(out var linear))
            return Unit(linear / 65536d);
        if (volume.TryGetProperty("value_percent", out var flatPercent) && flatPercent.ValueKind == JsonValueKind.String)
            return Percent(flatPercent.GetString());
        double sum = 0;
        var count = 0;
        foreach (var channel in volume.EnumerateObject())
        {
            if (channel.Value.ValueKind != JsonValueKind.Object) continue;
            if (channel.Value.TryGetProperty("value", out var value) && value.TryGetDouble(out var channelLinear))
            {
                sum += channelLinear / 65536d;
                count++;
            }
            else if (channel.Value.TryGetProperty("value_percent", out var channelPercent) && channelPercent.ValueKind == JsonValueKind.String)
            {
                sum += Percent(channelPercent.GetString());
                count++;
            }
        }
        return count == 0 ? 1f : Unit(sum / count);
    }

    private static bool IsMuted(JsonElement item)
    {
        if (!item.TryGetProperty("mute", out var mute)) return false;
        return mute.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => mute.GetString() is "yes" or "true" or "1",
            JsonValueKind.Number => mute.TryGetInt32(out var number) && number != 0,
            _ => false,
        };
    }

    private static string? SinkOf(JsonElement item)
    {
        if (!item.TryGetProperty("sink", out var sink)) return null;
        return sink.ValueKind switch
        {
            JsonValueKind.Number => sink.TryGetInt32(out var index) ? index.ToString(CultureInfo.InvariantCulture) : null,
            JsonValueKind.String => sink.GetString(),
            _ => null,
        };
    }

    private static string? Property(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? IntValue(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static string? StringValue(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static float AveragePercents(string line)
    {
        double sum = 0;
        var count = 0;
        foreach (Match match in PercentExpression().Matches(line))
        {
            sum += Percent(match.Value);
            count++;
        }
        return count == 0 ? 1f : Unit(sum / count);
    }

    private static float Percent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var cleaned = text.Trim().TrimEnd('%').Trim().Replace(',', '.');
        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Unit(number / 100d) : 0;
    }

    private static float Unit(double value) => (float)Math.Clamp(value, 0, 1);

    private static string? Quoted(string line)
    {
        var equals = line.IndexOf('=');
        if (equals < 0) return null;
        return line[(equals + 1)..].Trim().Trim('"');
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Trim());

    [GeneratedRegex(@"\d+(?:[.,]\d+)?%")]
    private static partial Regex PercentExpression();
}
