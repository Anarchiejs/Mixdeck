using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Mixdeck.Core.Settings;
using Mixdeck.Desktop;

namespace Mixdeck.Desktop.Tests;

public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class HeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public sealed class ShellWindowHeadlessTests
{
    [Fact]
    public async Task Main_window_renders_navigation_and_pages()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
        await session.Dispatch(RunAsync, CancellationToken.None);
    }

    private static async Task<int> RunAsync()
    {
        DesktopText.Apply("en-US");
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-ui-" + Guid.NewGuid().ToString("N"));
        var host = DesktopHost.Create(new DesktopOptions(false, true, true, false), new MixdeckPaths(root));
        var window = new ShellWindow(host);
        try
        {
            await host.StartAsync();
            window.Show();
            var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            frame!.Size.Width.Should().BeGreaterThan(0);
            frame.Size.Height.Should().BeGreaterThan(0);

            window.Title.Should().Be("Mixdeck");
            window.IsVisible.Should().BeTrue();

            var nav = window.GetVisualDescendants().OfType<Button>().Where(button => button.Name?.StartsWith("nav-", StringComparison.Ordinal) == true).ToList();
            nav.Should().HaveCount(4);
            foreach (var button in nav)
            {
                button.IsVisible.Should().BeTrue();
                button.Bounds.Height.Should().BeGreaterThan(0);
                button.Bounds.Width.Should().BeGreaterThan(0);
                button.Content.Should().NotBeNull();
                button.Content!.ToString().Should().NotBeNullOrWhiteSpace();
            }

            var outputs = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "audio-outputs");
            var inputs = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "audio-inputs");
            outputs.Text.Should().Be("Playback");
            inputs.Text.Should().Be("Recording");
            outputs.IsVisible.Should().BeTrue();
            inputs.IsVisible.Should().BeTrue();
            outputs.Bounds.Height.Should().BeGreaterThan(0);
            inputs.Bounds.Width.Should().BeGreaterThan(0);

            var studio = nav.Single(button => button.Name == "nav-studio");
            var point = studio.TranslatePoint(new Point(studio.Bounds.Width / 2, studio.Bounds.Height / 2), window);
            point.Should().NotBeNull();
            window.MouseDown(point!.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);

            window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).Should().Contain("Voicemeeter").And.Contain("FL Studio");
            window.GetVisualDescendants().OfType<TextBlock>().Should().NotContain(block => block.Name == "audio-outputs");
            var studioFrame = window.CaptureRenderedFrame();
            studioFrame.Should().NotBeNull();
            studioFrame!.Size.Width.Should().BeGreaterThan(0);
        }
        finally
        {
            window.AllowClose();
            window.Close();
            await host.DisposeAsync();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
        return 0;
    }
}
