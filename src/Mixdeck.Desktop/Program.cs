using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Mixdeck.Core.Diagnostics;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Desktop.Instance;
using Mixdeck.Platform.Desktop.Startup;
using Velopack;

namespace Mixdeck.Desktop;

public static class Program
{
    private const string CrashMarker = "last-crash.txt";

    public static DesktopHost? Host { get; private set; }
    public static InstanceGate? Gate { get; private set; }
    public static DesktopOptions Options { get; private set; } = new(false, false, false, false);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
#pragma warning disable CA1416 // Velopack marque le hook Windows ; le même appel retire l'autostart macOS.
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ =>
                {
                    try { SessionAutoStart.DisableCurrentProcess(); }
                    catch (Exception) { /* La désinstallation continue. */ }
                })
                .Run();
#pragma warning restore CA1416
        }
        catch (Exception)
        {
            // Un binaire non installé ne doit pas refuser de démarrer si Velopack refuse le hook.
        }

        Options = DesktopOptions.Parse(args);
        if (Options.SelfCheck) return SelfCheck();

        var paths = MixdeckPaths.Default;
        paths.EnsureCreated();
        InstallCrashHandler(paths);

        var gate = Acquire(paths.Root, Options.AfterCrash || args.Contains("--restart", StringComparer.OrdinalIgnoreCase));
        if (!gate.IsFirst)
        {
            gate.SignalShow();
            gate.Dispose();
            return 0;
        }

        Gate = gate;
        Host = DesktopHost.Create(Options, paths);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int SelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-self-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new MixdeckPaths(root);
            var host = DesktopHost.Create(new DesktopOptions(false, true, true, false), paths);
            host.StartAsync().GetAwaiter().GetResult();
            _ = host.Surfaces.Audio.Snapshot;
            _ = host.Mixer.State;
            _ = host.Daw.State;
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (IOException) { }
        }
    }

    private static InstanceGate Acquire(string directory, bool retry)
    {
        var gate = InstanceGate.Acquire(directory);
        for (var attempt = 0; retry && !gate.IsFirst && attempt < 20; attempt++)
        {
            gate.Dispose();
            Thread.Sleep(500);
            gate = InstanceGate.Acquire(directory);
        }
        return gate;
    }

    private static void InstallCrashHandler(MixdeckPaths paths)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.IsTerminating && args.ExceptionObject is Exception exception)
                ReportCrash(paths, exception);
        };
    }

    private static void ReportCrash(MixdeckPaths paths, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var now = DateTimeOffset.Now;
            var body = new StringBuilder()
                .AppendLine($"Mixdeck {Assembly.GetEntryAssembly()?.GetName().Version} · {now:O}")
                .AppendLine($"{Environment.OSVersion} · .NET {Environment.Version}")
                .AppendLine()
                .AppendLine(exception.ToString());
            File.WriteAllText(Path.Combine(paths.LogsDirectory, CrashPolicy.ReportFileName(now)), body.ToString());
            foreach (var old in CrashPolicy.ReportsToDelete(Directory.EnumerateFiles(paths.LogsDirectory, CrashPolicy.ReportPrefix + "*")))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            var marker = Path.Combine(paths.LogsDirectory, CrashMarker);
            DateTimeOffset? previous = File.Exists(marker) && DateTimeOffset.TryParse(File.ReadAllText(marker), out var at) ? at : null;
            File.WriteAllText(marker, now.ToString("O"));
            if (CrashPolicy.ShouldRestart(now, previous) && Environment.ProcessPath is { } executable)
            {
                Process.Start(new ProcessStartInfo(executable, "--minimized --after-crash") { UseShellExecute = false });
            }
        }
        catch (Exception) { }
    }
}
