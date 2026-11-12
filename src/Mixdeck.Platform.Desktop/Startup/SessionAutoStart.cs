using System.Text;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Startup;

public sealed class XdgAutoStart : IAutoStart
{
    private readonly string _file;
    private readonly string _executable;
    private readonly string _arguments;

    public XdgAutoStart(string directory, string executable, string arguments = "--minimized")
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "mixdeck.desktop");
        _executable = executable;
        _arguments = arguments;
    }

    public static XdgAutoStart ForCurrentProcess()
    {
        var config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new XdgAutoStart(Path.Combine(config, "autostart"), Environment.ProcessPath ?? "");
    }

    public bool IsEnabled => File.Exists(_file);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(_file)) File.Delete(_file);
            return;
        }
        if (string.IsNullOrWhiteSpace(_executable)) return;
        var content = $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=Mixdeck
            Comment=Mixdeck audio control
            Exec="{_executable}" {_arguments}
            Terminal=false
            Categories=AudioVideo;Audio;
            X-GNOME-Autostart-enabled=true

            """;
        File.WriteAllText(_file, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    }
}

public sealed class LaunchAgentAutoStart : IAutoStart
{
    private readonly string _file;
    private readonly string _executable;
    private readonly string _arguments;

    public LaunchAgentAutoStart(string directory, string executable, string arguments = "--minimized")
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "com.mixdeck.desktop.plist");
        _executable = executable;
        _arguments = arguments;
    }

    public static LaunchAgentAutoStart ForCurrentProcess()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new LaunchAgentAutoStart(Path.Combine(home, "Library", "LaunchAgents"), Environment.ProcessPath ?? "");
    }

    public bool IsEnabled => File.Exists(_file);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(_file)) File.Delete(_file);
            return;
        }
        if (string.IsNullOrWhiteSpace(_executable)) return;
        var content = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>com.mixdeck.desktop</string>
              <key>RunAtLoad</key>
              <true/>
              <key>ProgramArguments</key>
              <array>
                <string>{Escape(_executable)}</string>
                <string>{Escape(_arguments)}</string>
              </array>
            </dict>
            </plist>

            """;
        File.WriteAllText(_file, content, new UTF8Encoding(false));
    }

    private static string Escape(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}

public static class SessionAutoStart
{
    public static void DisableCurrentProcess()
    {
        if (OperatingSystem.IsLinux()) XdgAutoStart.ForCurrentProcess().SetEnabled(false);
        else if (OperatingSystem.IsMacOS()) LaunchAgentAutoStart.ForCurrentProcess().SetEnabled(false);
    }
}

public sealed class UnsupportedAutoStart(string reason) : IAutoStart
{
    public string Reason { get; } = reason;
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
}
