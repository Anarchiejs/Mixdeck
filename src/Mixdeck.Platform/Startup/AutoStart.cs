using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Startup;

public sealed class AutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _keyPath;
    private readonly string _processPath;
    private readonly ILogger<AutoStart> _logger;

    public AutoStart(ILogger<AutoStart>? logger = null) : this(RunKey, Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable courant introuvable."), logger) { }

    internal AutoStart(string keyPath, string processPath, ILogger<AutoStart>? logger = null)
    {
        _keyPath = keyPath;
        _processPath = processPath;
        _logger = logger ?? NullLogger<AutoStart>.Instance;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
                if (key?.GetValue("Mixdeck") is not string command) return false;
                command = command.Trim();
                string executable;
                if (command.StartsWith('"'))
                {
                    int end = command.IndexOf('"', 1);
                    if (end < 0) return false;
                    executable = command[1..end];
                }
                else
                {
                    int end = command.IndexOf(' ');
                    executable = end < 0 ? command : command[..end];
                }
                return Path.IsPathFullyQualified(executable) && string.Equals(Path.GetFullPath(executable), Path.GetFullPath(_processPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            {
                _logger.LogWarning(ex, "Impossible de lire le démarrage automatique.");
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
                key.SetValue("Mixdeck", $"\"{_processPath}\" --minimized", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
                key?.DeleteValue("Mixdeck", throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Impossible de modifier le démarrage automatique.");
        }
    }
}
