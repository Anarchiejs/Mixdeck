using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mixdeck.App.Controls.Gallery;

/// <summary>Point d'entrée facultatif pour vérifier la galerie sans modifier le shell.</summary>
public static class GalleryLauncher
{
#if MIXDECK_GALLERY
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new GalleryApplication(args.Contains("--smoke"));
            });
        }
        catch (Exception exception)
        {
            if (Environment.GetEnvironmentVariable("MIXDECK_GALLERY_LOG") is string path)
                System.IO.File.WriteAllText(path, exception.ToString());
            return 1;
        }
        return Environment.ExitCode;
    }
#endif

    private sealed class GalleryApplication : Application
    {
        private readonly bool smoke;
        private ControlsGalleryWindow? window;
        private DispatcherQueueTimer? timer;
        public GalleryApplication(bool smoke)
        {
            this.smoke = smoke;
            UnhandledException += (_, args) =>
            {
                Console.Error.WriteLine(args.Exception);
                if (smoke && Environment.GetEnvironmentVariable("MIXDECK_GALLERY_LOG") is string path)
                    System.IO.File.WriteAllText(path, args.Exception.ToString());
                Environment.ExitCode = 1;
            };
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Controls/Controls.xaml") });
        }
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            window = new ControlsGalleryWindow();
            window.Activate();
            if (!smoke) return;
            timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (_, _) => { timer.Stop(); window.Close(); Exit(); };
            timer.Start();
        }
    }
}
