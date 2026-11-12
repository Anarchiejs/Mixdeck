using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mixdeck.Core.Settings;

/// <summary>
/// Deletes credentials left behind by the removed Discord integration.
/// The secret lived only in discord.bin. settings.json could still hold the non-secret Discord section.
/// </summary>
internal static class LegacyDiscordCleanup
{
    public static void Remove(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(root))
            {
                if (IsCredentialFile(Path.GetFileName(path)))
                    SecureDelete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        StripDiscordProperty(Path.Combine(root, "settings.json"));
        StripDiscordProperty(Path.Combine(root, "settings.json.bak"));
    }

    private static bool IsCredentialFile(string name) =>
        name.Equals("discord.bin", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("discord.bin.", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

    private static void SecureDelete(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if (info.IsReadOnly) info.IsReadOnly = false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new byte[81920];
                var remaining = stream.Length;
                while (remaining > 0)
                {
                    var count = (int)Math.Min(buffer.Length, remaining);
                    stream.Write(buffer, 0, count);
                    remaining -= count;
                }
                stream.Flush(true);
            }
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void StripDiscordProperty(string path)
    {
        if (!File.Exists(path)) return;
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
        if (text.IndexOf("discord", StringComparison.OrdinalIgnoreCase) < 0) return;

        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (JsonException) { return; }
        if (node is not JsonObject obj) return;

        var removed = false;
        foreach (var key in obj.Select(property => property.Key).ToArray())
        {
            if (!key.Equals("discord", StringComparison.OrdinalIgnoreCase)) continue;
            obj.Remove(key);
            removed = true;
        }
        if (!removed) return;

        var updated = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".cleanup-tmp";
        try
        {
            File.WriteAllText(temporary, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
        }
    }
}
