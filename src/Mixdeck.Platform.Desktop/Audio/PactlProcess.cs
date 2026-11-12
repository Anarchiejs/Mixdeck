namespace Mixdeck.Platform.Desktop.Audio;

internal static class PactlProcess
{
    public static string? Find()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "pactl");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string pactl, CancellationToken cancellationToken, params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(pactl)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in args) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new IOException("pactl did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
