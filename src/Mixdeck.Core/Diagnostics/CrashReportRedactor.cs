namespace Mixdeck.Core.Diagnostics;

/// <summary>Retire les chemins personnels d'un rapport avant de le copier.</summary>
public static class CrashReportRedactor
{
    public static string Redact(string report, params string?[] personalRoots)
    {
        if (string.IsNullOrEmpty(report)) return report;
        var text = report.Replace(Environment.NewLine, "\n");
        foreach (var root in personalRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var trimmed = root.TrimEnd('\\', '/');
            if (trimmed.Length < 3) continue;
            text = Replace(text, trimmed, "<user>");
            text = Replace(text, trimmed.Replace('\\', '/'), "<user>");
        }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            text = Replace(text, profile.TrimEnd('\\', '/'), "<user>");
            text = Replace(text, profile.TrimEnd('\\', '/').Replace('\\', '/'), "<user>");
        }
        return text.Replace("\n", Environment.NewLine);
    }

    private static string Replace(string text, string needle, string replacement) =>
        needle.Length == 0 ? text : text.Replace(needle, replacement, StringComparison.OrdinalIgnoreCase);
}
