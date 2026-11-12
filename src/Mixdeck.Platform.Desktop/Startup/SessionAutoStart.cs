using System.Text;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Startup;

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
        if (OperatingSystem.IsMacOS()) LaunchAgentAutoStart.ForCurrentProcess().SetEnabled(false);
    }
}

public sealed class UnsupportedAutoStart(string reason) : IAutoStart
{
    public string Reason { get; } = reason;
    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
}
