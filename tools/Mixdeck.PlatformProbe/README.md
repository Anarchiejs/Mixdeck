# Windows platform probe

From the repository root:

```powershell
dotnet run --project tools/Mixdeck.PlatformProbe -- --seconds 20
```

The probe prints the taskbar and the DPI, then foreground changes and processes that start or exit. Hold F13 to check push-to-talk (one press and one release despite auto-repeat). Ctrl+Alt+M checks the ordinary hotkey. The probe does not change the automatic-startup registration.

Sleep, resume, display, and theme notifications are printed too. Events arrive on background threads. Subscribers should return quickly and move visual updates to the UI thread.

`dotnet` must be an SDK on `PATH`, or the per-user SDK in `%LOCALAPPDATA%\Microsoft\dotnet`.

The projects `tests/Mixdeck.Platform.Tests` and `tools/Mixdeck.PlatformProbe` are already in `Mixdeck.slnx`.

AutoStart tests write only under a unique `HKCU\Software\Mixdeck.Tests` subkey and delete it afterwards. They do not write the real Run value.

A five-second run checks startup and shutdown, geometry, DPI, and real process notifications. Keyboard transitions, multiple monitors, an accessible foreground window, sleep and resume, and theme changes need a person at the machine. The probe does not inject keys and does not trigger sleep.
