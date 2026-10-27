using Mixdeck.App.Localization;
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
    private double leftPeak = -60, rightPeak = -60;
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
        double t = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        double left = -60 + 70 * (.5 + .5 * Math.Sin(t * 2.3));
        double right = -60 + 70 * (.5 + .5 * Math.Sin(t * 1.7 + .8));
        leftPeak = Math.Max(left, leftPeak - .12); rightPeak = Math.Max(right, rightPeak - .12);
        SetMeter(HorizontalMeter); SetMeter(VerticalMeter); SetMeter(MonoMeter);
        void SetMeter(VuMeter meter)
        {
            meter.LeftDb = left; meter.RightDb = right;
            meter.LeftPeakDb = leftPeak; meter.RightPeakDb = rightPeak;
            meter.IsClipped = leftPeak >= 0 || rightPeak >= 0;
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
