using System.Diagnostics;
using System.Reflection;
using System.Text;
using Mixdeck.Core.Diagnostics;
using Mixdeck.Core.Settings;
using Serilog;

namespace Mixdeck.App.Services;

/// <summary>
/// Dernier filet : une exception qui va arrêter le processus est écrite dans logs\crash-*.txt, puis Mixdeck se relance
/// une fois, discrètement. Deux plantages en moins de 5 minutes : pas de relance (pas de boucle).
/// Les erreurs de l'interface, elles, sont rattrapées par App.OnUnhandledException et ne passent pas ici.
/// </summary>
internal static class CrashReporter
{
    private const string MarkerFile = "last-crash.txt";
    private static int _reported;

    public static void Install(MixdeckPaths paths)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.IsTerminating && e.ExceptionObject is Exception ex) Report(paths, ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Une tâche oubliée ne doit pas tuer l'appli : on la journalise seulement.
            Log.Warning(e.Exception, "Exception de tâche non observée");
            e.SetObserved();
        };
    }

    private static void Report(MixdeckPaths paths, Exception exception)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;
        try
        {
            Log.Fatal(exception, "Plantage");
            Log.CloseAndFlush();
            Directory.CreateDirectory(paths.LogsDirectory);
            var now = DateTimeOffset.Now;
            File.WriteAllText(Path.Combine(paths.LogsDirectory, CrashPolicy.ReportFileName(now)), Describe(paths, exception), Encoding.UTF8);
            foreach (var old in CrashPolicy.ReportsToDelete(Directory.EnumerateFiles(paths.LogsDirectory, CrashPolicy.ReportPrefix + "*")))
            {
                TryDelete(old);
            }

            var marker = Path.Combine(paths.LogsDirectory, MarkerFile);
            DateTimeOffset? previous = File.Exists(marker) && DateTimeOffset.TryParse(File.ReadAllText(marker), out var at) ? at : null;
            File.WriteAllText(marker, now.ToString("O"));
            if (CrashPolicy.ShouldRestart(now, previous) && Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe, "--minimized --after-crash") { UseShellExecute = false });
            }
        }
        catch
        {
            // Le processus s'arrête de toute façon : rien de plus à faire.
        }
    }

    private static string Describe(MixdeckPaths paths, Exception exception)
    {
        var text = new StringBuilder()
            .AppendLine($"Mixdeck {Assembly.GetEntryAssembly()?.GetName().Version} · {DateTimeOffset.Now:O}")
            .AppendLine($"{Environment.OSVersion} · .NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .AppendLine()
            .AppendLine("--- Fin du journal du jour ---");
        try
        {
            var log = Directory.EnumerateFiles(paths.LogsDirectory, "mixdeck-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (log is not null)
            {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = new Queue<string>();
                while (reader.ReadLine() is { } line)
                {
                    lines.Enqueue(line);
                    if (lines.Count > 50) lines.Dequeue();
                }

                foreach (var line in lines) text.AppendLine(line);
            }
        }
        catch (IOException)
        {
        }

        return text.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
