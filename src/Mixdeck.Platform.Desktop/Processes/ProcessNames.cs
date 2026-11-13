namespace Mixdeck.Platform.Desktop.Processes;

internal readonly record struct ProcessRecord(int Pid, string Name);

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

    public static IReadOnlyList<ProcessRecord> Snapshot()
    {
        var records = new List<ProcessRecord>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var file = SafeFileName(process);
                var name = string.IsNullOrWhiteSpace(file) ? process.ProcessName : Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(name)) records.Add(new ProcessRecord(process.Id, name));
            }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        return records;
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
