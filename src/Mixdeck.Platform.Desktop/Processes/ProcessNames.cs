using System.Runtime.InteropServices;
using System.Text;

namespace Mixdeck.Platform.Desktop.Processes;

internal static class ProcessNames
{
    public static string? FromPid(int pid)
    {
        if (pid <= 0) return null;
        if (OperatingSystem.IsLinux())
        {
            var target = ReadLink("/proc/" + pid.ToString() + "/exe");
            if (!string.IsNullOrEmpty(target)) return Path.GetFileName(target);
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            var file = SafeFileName(process);
            return string.IsNullOrEmpty(file) ? process.ProcessName : Path.GetFileName(file);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static IReadOnlyCollection<string> Snapshot()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsLinux() && Directory.Exists("/proc"))
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
                var name = FromPid(pid);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.ProcessName)) names.Add(process.ProcessName);
            }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return names;
    }

    private static string? SafeFileName(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadLink(string path)
    {
        var buffer = new byte[4096];
        var read = readlink(path, buffer, (nuint)buffer.Length);
        if (read <= 0) return null;
        return Encoding.UTF8.GetString(buffer, 0, (int)Math.Min(read, buffer.Length));
    }

    [DllImport("libc", SetLastError = true)]
    private static extern nint readlink(string path, byte[] buffer, nuint size);
}
