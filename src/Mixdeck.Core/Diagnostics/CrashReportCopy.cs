namespace Mixdeck.Core.Diagnostics;

/// <summary>Lit le dernier rapport de plantage et en retire les chemins personnels.</summary>
public static class CrashReportCopy
{
    public static string? ReadLatest(string logsDirectory)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory)) return null;
        var file = Directory.EnumerateFiles(logsDirectory, CrashPolicy.ReportPrefix + "*")
            .Where(path => Path.GetFileName(path).StartsWith(CrashPolicy.ReportPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return file is null ? null : CrashReportRedactor.Redact(File.ReadAllText(file));
    }
}
