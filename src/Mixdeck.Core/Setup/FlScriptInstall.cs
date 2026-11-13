namespace Mixdeck.Core.Setup;

/// <summary>Copie le script FL Studio livré avec Mixdeck dans le dossier Hardware de l'utilisateur.</summary>
public static class FlScriptInstall
{
    public const string FileName = "device_Mixdeck.py";

    public static string? FindBundledScript(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "fl", "Mixdeck", FileName);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static string CopyToDocuments(string scriptPath, string documentsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDirectory);
        if (!File.Exists(scriptPath)) throw new FileNotFoundException("Script FL Studio introuvable.", scriptPath);
        var destinationDir = Path.Combine(documentsDirectory, "Image-Line", "FL Studio", "Settings", "Hardware", "Mixdeck");
        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, FileName);
        File.Copy(scriptPath, destination, overwrite: true);
        return destination;
    }
}
