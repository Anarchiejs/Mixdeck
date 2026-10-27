namespace Mixdeck.Core.Settings;

/// <summary>Emplacements des fichiers de Mixdeck. Racine par défaut : %LOCALAPPDATA%\Mixdeck.</summary>
public sealed class MixdeckPaths
{
    public MixdeckPaths(string root)
    {
        Root = root;
    }

    public static MixdeckPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mixdeck"));

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SettingsBackupFile => Path.Combine(Root, "settings.json.bak");
    public string ProfilesDirectory => Path.Combine(Root, "profiles");
    public string LogsDirectory => Path.Combine(Root, "logs");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
