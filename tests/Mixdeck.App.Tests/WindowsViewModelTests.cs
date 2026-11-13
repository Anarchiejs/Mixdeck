namespace Mixdeck.App.Tests;

/// <summary>The WinUI view models need a window. These tests lock the decisions they call.</summary>
public sealed class WindowsViewModelTests
{
    [Fact]
    public void Hotkey_save_rejects_a_conflicting_chord()
    {
        var source = Read("src/Mixdeck.App/ViewModels/Pages/SettingsPageViewModel.cs");
        source.Should().Contain("HotkeyConflicts.Find");
        source.Should().Contain("Settings_HotkeyConflict");
    }

    [Fact]
    public void Rule_editor_builds_through_the_shared_draft()
    {
        var source = Read("src/Mixdeck.App/ViewModels/Pages/RulesPageViewModel.cs");
        source.Should().Contain("RuleDraft.Build");
    }

    [Fact]
    public void App_list_filters_with_the_shared_matcher()
    {
        var source = Read("src/Mixdeck.App/ViewModels/AppVolume/AppVolumeListViewModel.cs");
        source.Should().Contain("AppSessionFilter.Matches");
    }

    private static string Read(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
