using Microsoft.UI.Xaml;

namespace Mixdeck.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new Window { Title = "Mixdeck" };
        window.Activate();
    }
}
