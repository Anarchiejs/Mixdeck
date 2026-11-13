using Mixdeck.App.Localization;
using Mixdeck.Core.Audio;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mixdeck.App.Controls.Gallery;

public sealed partial class ControlsGalleryWindow : Window
{
    private readonly DispatcherQueueTimer timer;
    private readonly long started = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly ObservableCollection<GalleryProfile> profiles = [new(Strings.Get("CS_008"), "\uE8A5", null), new(Strings.Get("CS_009"), "\uE716", "#3FB950"), new(Strings.Get("CS_010"), "\uE8D6", "#C586C0")];
    private readonly MeterBallistics leftBallistic = new();
    private readonly MeterBallistics rightBallistic = new();
    private long lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
    public ControlsGalleryWindow()
    {
        InitializeComponent(); Title = Mixdeck.App.Localization.Strings.Get("ControlsGalleryWindow001.Title");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(850, 850));
        Root.RequestedTheme = ElementTheme.Light;
        Profiles.ItemsSource = profiles; profiles[0].IsActive = true; Profiles.SelectedItem = profiles[0];
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1d / 60); timer.Tick += Tick; timer.Start();
        Closed += (_, _) => { timer.Stop(); timer.Tick -= Tick; };
    }
    private void Tick(DispatcherQueueTimer sender, object args)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(lastTick, now);
        lastTick = now;
        double t = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        double left = -60 + 70 * (.5 + .5 * Math.Sin(t * 2.3));
        double right = -60 + 70 * (.5 + .5 * Math.Sin(t * 1.7 + .8));
        leftBallistic.Update((float)Math.Pow(10, left / 20), elapsed);
        rightBallistic.Update((float)Math.Pow(10, right / 20), elapsed);
        SetMeter(HorizontalMeter); SetMeter(VerticalMeter); SetMeter(MonoMeter);
        void SetMeter(VuMeter meter)
        {
            meter.LeftDb = leftBallistic.LevelDb; meter.RightDb = rightBallistic.LevelDb;
            meter.LeftPeakDb = leftBallistic.PeakHoldDb; meter.RightPeakDb = rightBallistic.PeakHoldDb;
            meter.IsClipped = leftBallistic.PeakHoldDb >= 0 || rightBallistic.PeakHoldDb >= 0;
        }
    }
    private void ThemeChanged(object sender, RoutedEventArgs e) => Root.RequestedTheme = ((ToggleSwitch)sender).IsOn ? ElementTheme.Dark : ElementTheme.Light;
    private void AnimationChanged(object sender, RoutedEventArgs e)
    {
        if (timer is null) return;
        if (((ToggleSwitch)sender).IsOn) timer.Start(); else timer.Stop();
    }
    private void ProfileInvoked(ProfilePillList sender, ProfilePillInvokedEventArgs args)
    {
        foreach (var profile in profiles) profile.IsActive = ReferenceEquals(profile, args.Item);
        sender.SelectedItem = args.Item;
    }
    private void NameCommitted(ChannelHeader sender, ChannelNameCommittedEventArgs args) => CommittedName.Text = Strings.Get("CS_011") + args.Name;

    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class GalleryProfile(string name, string icon, string? color) : INotifyPropertyChanged
    {
        private bool active;
        public string Name { get; } = name;
        public string Icon { get; } = icon;
        public string? Color { get; } = color;
        public bool IsActive { get => active; set { if (active == value) return; active = value; PropertyChanged?.Invoke(this, new(nameof(IsActive))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
