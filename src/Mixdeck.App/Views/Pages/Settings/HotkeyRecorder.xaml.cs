using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mixdeck.Core.Model;
using Mixdeck.Platform.Hotkeys;
using Windows.System;
using Windows.UI.Core;

namespace Mixdeck.App.Views.Pages.Settings;

public sealed partial class HotkeyRecorder : UserControl
{
    private readonly HotkeyAction action;
    private readonly int targetBus;
    private readonly float stepDb;
    private readonly Func<HotkeyBinding, bool> conflicts;
    public HotkeyRecorder(HotkeyAction action, int targetBus, float stepDb, Func<HotkeyBinding, bool> conflicts)
    {
        this.action = action;
        this.targetBus = targetBus;
        this.stepDb = stepDb;
        this.conflicts = conflicts;
        InitializeComponent();
    }
    public HotkeyBinding? Value { get; private set; }
    public event EventHandler? Captured;
    private void OnLoaded(object sender, RoutedEventArgs e) => CaptureButton.Focus(FocusState.Programmatic);
    public void ShowError(string message) { ValidationError.Message = message; ValidationError.IsOpen = true; }
    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) return;
        var modifiers = HotkeyModifiers.None;
        if (Down(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (Down(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (Down(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Win;
        if (e.Key == VirtualKey.Tab && modifiers is HotkeyModifiers.None or HotkeyModifiers.Shift) return;
        e.Handled = true;
        var key = (int)e.Key;
        if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) return;
        Value = null;
        if (!HotkeyFormatter.IsValidCombination(modifiers, key))
            ShowError("Ajoutez un modificateur (Ctrl, Alt, Maj ou Windows). Une touche seule n’est pas un raccourci valide, sauf certaines touches multimédias ou F13 à F24.");
        else
        {
            var binding = new HotkeyBinding(action, modifiers, key, targetBus, stepDb);
            if (conflicts(binding)) ShowError("Cette combinaison est déjà attribuée à une autre action de Mixdeck.");
            else
            {
                Value = binding;
                ValidationError.IsOpen = false;
                CombinationText.Text = HotkeyFormatter.Format(binding);
            }
        }
        Captured?.Invoke(this, EventArgs.Empty);
    }
}
