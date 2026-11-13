namespace Mixdeck.Core.Audio;

/// <summary>Filtre la liste des applications du mélangeur de volume.</summary>
public static class AppSessionFilter
{
    public static bool Matches(string? displayName, string? processName, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = query.Trim();
        return Contains(displayName, needle) || Contains(processName, needle);
    }

    private static bool Contains(string? value, string needle) =>
        !string.IsNullOrEmpty(value) && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
