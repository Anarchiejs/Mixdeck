using Microsoft.Win32;

namespace Mixdeck.Voicemeeter;

public static class VoicemeeterLocator
{
    public const string LibraryFileName = "VoicemeeterRemote64.dll";

    public static string? FindInstallationDirectory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                foreach (var prefix in new[] { @"SOFTWARE\Microsoft", @"SOFTWARE\WOW6432Node\Microsoft" })
                {
                    using var key = root.OpenSubKey(prefix + @"\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}");
                    if (key?.GetValue("UninstallString") is not string uninstall) continue;
                    var directory = DirectoryFromUninstallString(uninstall);
                    if (directory is not null && File.Exists(Path.Combine(directory, LibraryFileName))) return directory;
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Un registre inaccessible ne doit pas empêcher le chemin de repli.
            }
        }

        const string fallback = @"C:\Program Files (x86)\VB\Voicemeeter";
        return File.Exists(Path.Combine(fallback, LibraryFileName)) ? fallback : null;
    }

    internal static string? DirectoryFromUninstallString(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string executable;
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return null;
            executable = command[1..end];
        }
        else
        {
            var end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return null;
            executable = command[..(end + 4)];
        }
        return ParentDirectory(executable);
    }

    /// <summary>
    /// Directory portion of a Windows uninstall path. Separators are handled as characters so the
    /// result is the same on every operating system (<c>Path.GetDirectoryName</c> only treats '\' as a separator on Windows).
    /// </summary>
    private static string? ParentDirectory(string path)
    {
        var normalized = path.Replace('/', '\\');
        var separator = normalized.LastIndexOf('\\');
        if (separator <= 0) return null;
        if (separator == 2 && normalized[1] == ':') return normalized[..3];
        return normalized[..separator];
    }
}
