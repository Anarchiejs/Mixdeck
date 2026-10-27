using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Mixdeck.App.Controls;

public sealed class VuMeter : Control
{
    private Grid? host;
    private CanvasControl? canvas;
    private readonly CanvasSolidColorBrush?[] brushes = new CanvasSolidColorBrush?[5];
    private CanvasTextFormat? textFormat;
    // Partagé par tous les vumètres : relire le dictionnaire XAML à chaque instance coûte cher.
    private static ResourceDictionary? sharedTokens;
    // Lecture seule de l'état du contraste élevé (la propriété fonctionne, contrairement à l'événement).
    private static readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility = new();
    private static ResourceDictionary tokens => sharedTokens ??= new() { Source = new Uri("ms-appx:///Themes/Mixdeck.xaml") };
    private static readonly double[] Ticks = [-48, -24, -12, -6, 0, 6];
    private static readonly string[] Labels = ["−48", "−24", "−12", "−6", "0", "+6"];

    public VuMeter()
    {
        DefaultStyleKey = typeof(VuMeter);
        ControlResources.Load(this);
        AutomationProperties.SetName(this, "Niveau audio");
        // AccessibilitySettings.HighContrastChanged n'existe pas pour une appli de bureau WinUI (0x80070490) :
        // le passage en contraste élevé change le thème des ressources et déclenche ActualThemeChanged.
        Loaded += (_, _) => AttachCanvas();
        Unloaded += (_, _) => DetachCanvas();
        ActualThemeChanged += (_, _) => { ReleaseResources(); canvas?.Invalidate(); };
    }

    private static DependencyProperty Level(string name, object initial) => DependencyProperty.Register(
        name, initial.GetType(), typeof(VuMeter), new PropertyMetadata(initial, Changed));
    public static readonly DependencyProperty LeftDbProperty = Level(nameof(LeftDb), -60d);
    public static readonly DependencyProperty RightDbProperty = Level(nameof(RightDb), -60d);
    public static readonly DependencyProperty LeftPeakDbProperty = Level(nameof(LeftPeakDb), -60d);
    public static readonly DependencyProperty RightPeakDbProperty = Level(nameof(RightPeakDb), -60d);
    public static readonly DependencyProperty IsClippedProperty = Level(nameof(IsClipped), false);
    public static readonly DependencyProperty IsStereoProperty = Level(nameof(IsStereo), true);
    public static readonly DependencyProperty IsMutedProperty = Level(nameof(IsMuted), false);
    public static readonly DependencyProperty ShowScaleProperty = Level(nameof(ShowScale), false);
    public static readonly DependencyProperty OrientationProperty = Level(nameof(Orientation), Orientation.Vertical);
    public double LeftDb { get => (double)GetValue(LeftDbProperty); set => SetValue(LeftDbProperty, value); }
    public double RightDb { get => (double)GetValue(RightDbProperty); set => SetValue(RightDbProperty, value); }
    public double LeftPeakDb { get => (double)GetValue(LeftPeakDbProperty); set => SetValue(LeftPeakDbProperty, value); }
    public double RightPeakDb { get => (double)GetValue(RightPeakDbProperty); set => SetValue(RightPeakDbProperty, value); }
    public bool IsClipped { get => (bool)GetValue(IsClippedProperty); set => SetValue(IsClippedProperty, value); }
    public bool IsStereo { get => (bool)GetValue(IsStereoProperty); set => SetValue(IsStereoProperty, value); }
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public bool ShowScale { get => (bool)GetValue(ShowScaleProperty); set => SetValue(ShowScaleProperty, value); }
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((VuMeter)sender).canvas?.Invalidate();
    protected override void OnApplyTemplate()
    {
        DetachCanvas();
        base.OnApplyTemplate();
        host = GetTemplateChild("PART_CanvasHost") as Grid;
        canvas = GetTemplateChild("PART_Canvas") as CanvasControl;
        if (canvas is not null) ConnectCanvas(); else AttachCanvas();
    }
    private void AttachCanvas()
    {
        if (host is null || canvas is not null) return;
        canvas = new CanvasControl { IsHitTestVisible = false };
        ConnectCanvas();
        host.Children.Add(canvas);
    }
    private void ConnectCanvas()
    {
        if (canvas is null) return;
        canvas.CreateResources += (_, _) => { ReleaseResources(); };
        canvas.Draw += Draw;
    }
    private void DetachCanvas()
    {
        ReleaseResources();
        if (canvas is null) return;
        canvas.Draw -= Draw;
        host?.Children.Remove(canvas);
        canvas.RemoveFromVisualTree();
        canvas = null;
    }
    private void ReleaseResources()
    {
        for (int i = 0; i < brushes.Length; i++) { brushes[i]?.Dispose(); brushes[i] = null; }
        textFormat?.Dispose(); textFormat = null;
    }
    private Color ThemeColor(string key)
    {
        string theme = accessibility.HighContrast ? "HighContrast" : ActualTheme == ElementTheme.Light ? "Light" : "Default";
        return (Color)((ResourceDictionary)tokens.ThemeDictionaries[theme])[key];
    }
    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (brushes[0] is null)
        {
            string[] keys = ["MixdeckMeterTrackColor", "MixdeckMeterLowColor", "MixdeckMeterMidColor", "MixdeckMeterClipColor"];
            for (int i = 0; i < keys.Length; i++) brushes[i] = new CanvasSolidColorBrush(sender, ThemeColor(keys[i]));
            var foreground = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            brushes[4] = new CanvasSolidColorBrush(sender, foreground);
            textFormat = new CanvasTextFormat { FontFamily = "Segoe UI Variable", FontSize = 10 };
        }
        float width = (float)sender.ActualWidth, height = (float)sender.ActualHeight;
        bool vertical = Orientation == Orientation.Vertical;
        float length = vertical ? height : width;
        float cross = Math.Max(0, (vertical ? width : height) - (ShowScale ? (vertical ? 26 : 16) : 0));
        int count = IsStereo ? 2 : 1;
        float gap = count == 2 ? Math.Min(2, cross / 5) : 0;
        float thickness = Math.Max(0, (cross - gap) / count);
        for (int channel = 0; channel < count; channel++)
        {
            float offset = channel * (thickness + gap);
            Segment(args.DrawingSession, vertical, length, thickness, offset, 0, 1, brushes[0]!);
            double value = MeterScale.DbToPosition(channel == 0 ? LeftDb : RightDb);
            double yellow = MeterScale.DbToPosition(-12), red = MeterScale.DbToPosition(0);
            Segment(args.DrawingSession, vertical, length, thickness, offset, 0, Math.Min(value, yellow), brushes[IsMuted ? 4 : 1]!);
            if (value > yellow) Segment(args.DrawingSession, vertical, length, thickness, offset, yellow, Math.Min(value, red), brushes[IsMuted ? 4 : 2]!);
            if (value > red) Segment(args.DrawingSession, vertical, length, thickness, offset, red, value, brushes[IsMuted ? 4 : 3]!);
            double peak = MeterScale.DbToPosition(channel == 0 ? LeftPeakDb : RightPeakDb);
            if (vertical) args.DrawingSession.DrawLine(offset, (float)((1 - peak) * length), offset + thickness, (float)((1 - peak) * length), brushes[4]!, 1);
            else args.DrawingSession.DrawLine((float)(peak * length), offset, (float)(peak * length), offset + thickness, brushes[4]!, 1);
            if (IsClipped) Segment(args.DrawingSession, vertical, length, thickness, offset, .98, 1, brushes[3]!);
        }
        if (ShowScale)
            for (int i = 0; i < Ticks.Length; i++)
            {
                float p = (float)MeterScale.DbToPosition(Ticks[i]);
                args.DrawingSession.DrawText(Labels[i], vertical ? cross + 3 : Math.Clamp(p * width - 8, 0, Math.Max(0, width - 20)),
                    vertical ? Math.Clamp((1 - p) * height - 6, 0, Math.Max(0, height - 12)) : cross, brushes[4]!, textFormat);
            }
    }
    private static void Segment(CanvasDrawingSession session, bool vertical, float length, float thickness, float offset, double start, double end, CanvasSolidColorBrush brush)
    {
        if (end <= start) return;
        if (vertical) session.FillRectangle(offset, (float)((1 - end) * length), thickness, (float)((end - start) * length), brush);
        else session.FillRectangle((float)(start * length), offset, (float)((end - start) * length), thickness, brush);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new MeterPeer(this);
    private sealed class MeterPeer(VuMeter owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => nameof(VuMeter);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
        protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Value ? this : base.GetPatternCore(pattern);
        public bool IsReadOnly => true;
        public string Value => owner.IsStereo ? $"Gauche {ControlResources.Db(owner.LeftDb)}, droite {ControlResources.Db(owner.RightDb)}" : ControlResources.Db(owner.LeftDb);
        public void SetValue(string value) => throw new InvalidOperationException("Le niveau est en lecture seule.");
    }
}
