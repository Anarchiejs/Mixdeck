namespace Mixdeck.Platform.Desktop.Processes;

internal static class ProcessNames
{
    public static string? FromPid(int pid)
    {
        if (pid <= 0) return null;
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
}
