using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Mixdeck.UiTests;

/// <summary>N'est lancé que si MIXDECK_UI_TESTS=1 : il faut une session de bureau (la CI GitHub Windows en a une).</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MIXDECK_UI_TESTS") != "1")
        {
            Skip = "Tests d'interface : définir MIXDECK_UI_TESTS=1 (session de bureau requise).";
        }
    }
}

/// <summary>Lance le vrai Mixdeck.exe en mode démo, avec un dossier de données jetable, et le pilote par UI Automation.</summary>
public sealed class ShellUiTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly string _data = Path.Combine(Path.GetTempPath(), "mixdeck-ui-" + Guid.NewGuid().ToString("N"));
    private readonly UIA3Automation _automation = new();
    private Application? _app;

    private static string ExePath()
    {
        if (Environment.GetEnvironmentVariable("MIXDECK_EXE") is { Length: > 0 } custom) return custom;
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Mixdeck.slnx"))) root = Path.GetDirectoryName(root) ?? throw new FileNotFoundException("Mixdeck.slnx introuvable");
        var exe = Directory.EnumerateFiles(Path.Combine(root, "src", "Mixdeck.App", "bin"), "Mixdeck.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return exe ?? throw new FileNotFoundException("Compilez Mixdeck.App avant les tests d'interface.");
    }

    private Application Launch(params string[] args)
    {
        var start = new ProcessStartInfo(ExePath(), string.Join(' ', args.Prepend("--demo"))) { UseShellExecute = false };
        start.Environment["MIXDECK_DATA_DIR"] = _data;
        _app = Application.Launch(start);
        return _app;
    }

    private Window MainWindow() =>
        Retry.WhileNull(() => _app!.GetAllTopLevelWindows(_automation).FirstOrDefault(w => w.Name == "Mixdeck" && FindByName(w, "Console") is not null),
            Timeout, throwOnTimeout: true).Result!;

    private AutomationElement? FindByName(AutomationElement root, string name) =>
        root.FindFirstDescendant(new PropertyCondition(_automation.PropertyLibrary.Element.Name, name));

    [UiFact]
    public void FirstRunOpensConsoleAndEveryPageShowsItsTitle()
    {
        Launch();
        var window = MainWindow();
        foreach (var page in new[] { "Routage", "Appareils", "FL Studio", "Profils", "Règles", "Réglages", "À propos", "Console" })
        {
            var item = Retry.WhileNull(() => window.FindFirstDescendant(c => c.ByName(page).And(c.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))),
                Timeout, throwOnTimeout: true).Result!;
            // Sélection par UI Automation : un clic souris tombe à côté si une autre fenêtre passe devant.
            if (item.Patterns.SelectionItem.TryGetPattern(out var selection)) selection.Select(); else item.Click();
            // Le titre de la page reprend le nom de l'entrée du menu.
            var title = Retry.WhileNull(() => window.FindAllDescendants(c => c.ByName(page)).FirstOrDefault(e => e.ControlType == FlaUI.Core.Definitions.ControlType.Text),
                Timeout).Result;
            Assert.True(title is not null, $"Titre « {page} » introuvable après navigation.");
        }
    }

    [UiFact]
    public void ClosingConsoleKeepsMixdeckRunning()
    {
        var app = Launch();
        MainWindow().Close();
        Thread.Sleep(3000);
        Assert.False(app.HasExited, "Fermer la console ne doit pas arrêter Mixdeck.");
    }

    [UiFact]
    public void PageOptionOpensRequestedPage()
    {
        // Le panneau rapide se ferme dès qu'il perd le focus (au profit du lanceur de tests) : on vérifie plutôt --page.
        Directory.CreateDirectory(_data);
        File.WriteAllText(Path.Combine(_data, "settings.json"), "{\"schemaVersion\":1}");
        Launch("--page=Devices");
        var window = MainWindow();
        var title = Retry.WhileNull(() => window.FindAllDescendants(c => c.ByName("Appareils audio")).FirstOrDefault(), Timeout).Result;
        Assert.True(title is not null, "La page Appareils ne s'est pas ouverte.");
    }

    public void Dispose()
    {
        try
        {
            if (_app is { HasExited: false }) _app.Kill();
        }
        catch (InvalidOperationException)
        {
        }

        _app?.Dispose();
        _automation.Dispose();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
