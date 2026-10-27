using System.Globalization;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
namespace Mixdeck.Core.Automation;
public sealed class ActionDispatcher(IMixer mixer, IProfileService profiles, ISettingsStore settings, IOsdService osd,
    ILogger<ActionDispatcher> logger, AutomationCoordinator automation)
{
    public event EventHandler? FlyoutRequested;
    public async Task HandleAsync(HotkeyEventArgs e)
    {
        try
        {
            var binding = e.Binding;
            if (!e.IsPressed && binding.Action != HotkeyAction.PushToTalk) return;
            var config = settings.Current;
            switch (binding.Action)
            {
                case HotkeyAction.ToggleMic:
                case HotkeyAction.PushToTalk:
                    var strip = mixer.Snapshot.Strips.FirstOrDefault(s => s.Index == config.MicStripIndex);
                    if (strip is null) return;
                    var muted = binding.Action == HotkeyAction.ToggleMic ? !strip.Mute : !e.IsPressed;
                    mixer.SetFloat(VmParam.StripMute(strip.Index), VmParam.FromBool(muted));
                    if (config.ShowOsd) osd.Show(muted ? "Micro coupé" : "Micro activé", muted ? "\uF781" : "\uE720", muted);
                    break;
                case HotkeyAction.NextProfile:
                case HotkeyAction.PreviousProfile:
                    var list = profiles.Profiles;
                    if (list.Count == 0) return;
                    var activeIndex = list.ToList().FindIndex(p => p.Id == profiles.Active?.Id);
                    var nextIndex = activeIndex < 0 ? 0 : (activeIndex + (binding.Action == HotkeyAction.NextProfile ? 1 : -1) + list.Count) % list.Count;
                    await automation.ApplyManualProfileAsync(list[nextIndex].Id);
                    if (config.ShowOsd && profiles.Active is { } active) osd.Show("Profil : " + active.Name, active.Icon);
                    break;
                case HotkeyAction.ApplyProfile:
                    if (binding.ProfileId is not { } id || !profiles.Profiles.Any(p => p.Id == id))
                    {
                        logger.LogWarning("Profil de raccourci introuvable : {ProfileId}", binding.ProfileId);
                        return;
                    }
                    await automation.ApplyManualProfileAsync(id);
                    if (config.ShowOsd && profiles.Active is { } applied && applied.Id == id)
                        osd.Show("Profil : " + applied.Name, applied.Icon);
                    break;
                case HotkeyAction.VolumeUp:
                case HotkeyAction.VolumeDown:
                    var bus = mixer.Snapshot.Buses.FirstOrDefault(b => b.Index == binding.TargetBus);
                    if (bus is null || !float.IsFinite(binding.StepDb)) return;
                    var gain = mixer.Layout.ClampGain(bus.Gain + (binding.Action == HotkeyAction.VolumeUp ? 1 : -1) * Math.Abs(binding.StepDb));
                    mixer.SetFloat(VmParam.BusGain(bus.Index), gain);
                    if (config.ShowOsd) osd.Show($"{bus.Name} : {gain.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")).Replace("-", "−")} dB", "\uE767");
                    break;
                case HotkeyAction.OpenFlyout: FlyoutRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Échec du raccourci"); }
    }
}
