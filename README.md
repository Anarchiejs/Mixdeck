<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="design/logo/final/mixdeck-lockup-white.svg">
    <img src="design/logo/final/mixdeck-lockup.svg" alt="Mixdeck" width="420">
  </picture>
</p>

# Mixdeck

[Français](README.fr.md)

Mixdeck is a native Windows tray app (C# / .NET 10, WinUI 3, unpackaged, x64) that controls [Voicemeeter](https://vb-audio.com/Voicemeeter/) from the taskbar. It is for people who mix a microphone, calls, games, music, and FL Studio on one Windows PC.

**Status.** Pre-release. The assembly version in `Directory.Build.props` is **0.1.0**. Nothing has been published to GitHub Releases yet. The app builds, starts, and drives Voicemeeter. Installers produced before [Azure Trusted Signing](docs/SIGNATURE.md) is configured are **unsigned**, so Windows SmartScreen warns on first launch.

Mixdeck is not affiliated with VB-Audio, Image-Line, or Discord.

## Features

- Tray icon next to the clock: left click opens the flyout, middle click mutes the microphone, tooltip shows mute, A1 volume, and the current profile.
- Flyout anchored to the taskbar. The wheel changes the volume of the row under the pointer after that row has been clicked.
- Per-application volume (Discord, a game, a browser, …) and "Send to…" to choose an application's output.
- Devices page: Voicemeeter microphone, headphones, and speakers, microphone presets (gate, compressor, clearer voice), audio-engine restart.
- Automatic ducking: music drops when someone speaks on Discord.
- Optional always-on mute badge and confirmation sound.
- "Hear myself" sends the microphone to a bus, usually the headphones.
- Mixer console: input and output strips that wrap, only strips tied to a device (plus virtual strips), collapsible EQ.
- Routing matrix, profiles with a fade, per-application rules.
- Global hotkeys, push-to-talk, a hotkey per profile, and a profile applied at startup.
- FL Studio page: pick a mixer track, set volume and mute, enable or bypass track effects and each effect slot (with the plugin name). A profile can store FL state. A hotkey can toggle a slot.
- Microphone mute kept in sync with Discord, both ways (Settings, see below).
- Default Windows output from the flyout, and default output and input from the Devices page.
- Local crash log and a single automatic restart.
- On-screen display.
- Install and updates with [Velopack](https://velopack.io/). Delta packages are published to GitHub Releases.

## Screenshots

Screenshots are not in the repository yet. They have to be captured on Windows. When they exist, put them in [`docs/images/`](docs/images/) and link them from this section.

## Requirements

- Windows 10 version 19041 or later, or Windows 11. x64 only. The app project targets `10.0.19041.0`.
- Voicemeeter Standard, Banana, or Potato, already installed. Mixdeck loads VB-Audio's remote library (`VoicemeeterRemote64.dll`) from that install. The DLL is not shipped with Mixdeck.
- To build: the .NET 10 SDK. `global.json` pins `10.0.403` with `latestFeature` roll-forward.

Optional:

- FL Studio, plus [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html), for the FL Studio page.
- A Discord application that you own, for mute sync. Discord must be running locally.

## Privacy

Mixdeck has no telemetry account and does not upload logs or crash reports.

| What | Where it goes |
| --- | --- |
| Settings, profiles, logs, crash reports | `%LOCALAPPDATA%\Mixdeck` on this PC. Logs rotate daily, 14 files, 10 MB each. A crash file includes the exception and the last 50 log lines. Nothing is sent. |
| Installed program | `%LOCALAPPDATA%\MixdeckApp`. Uninstall does not delete `%LOCALAPPDATA%\Mixdeck`. |
| Start with Windows | On by default for an installed build. The entry is `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. A build launched from `bin\` does not register itself. |
| Global hotkeys | Registered with Windows while Mixdeck is running. |
| Rules | The foreground executable name and the names of running processes. Kept in memory and in your rules file. Not sent anywhere. |
| Per-app volume | Windows session display names, process names, and volumes for the default output. |
| Default devices and "Send to…" | Undocumented Windows COM APIs (`IPolicyConfig`, `IAudioPolicyConfig`, Explorer tray notify). They can stop working after a Windows update. Mixdeck then disables the feature or reports the error. |
| Discord | Only if you turn sync on. Mixdeck asks Discord for the broad `rpc` scope (the narrower voice scopes are limited to Discord partners). The application secret and tokens are stored with DPAPI for the current Windows user in `discord.bin`. The only call to discord.com is the OAuth token endpoint. Mute changes use Discord's local RPC pipe. |
| FL Studio | Local MIDI only, through the two loopMIDI ports. The installer can write FL's MIDI settings and copy the device script into your Documents folder. |
| Updates | An installed build checks GitHub Releases at startup when Settings > Check for updates is on (the default). That is a request to `https://github.com/Anarchiejs/Mixdeck`. Turn the switch off to stop the automatic check. The About page can still check when you click the button. Development builds are not installed, so they do not check. |

`MIXDECK_DATA_DIR` points the data root somewhere else (tests and staging).

## Install

### From source, one click

Double-click **`Installer Mixdeck.cmd`** at the repository root. It does not ask for administrator rights. `build/install.ps1`:

1. Looks for a .NET 10 SDK (10.0.403 or newer). If there is none, it installs one for the current user under `%LOCALAPPDATA%\Microsoft\dotnet` with the official `dotnet-install.ps1` script.
2. Checks that Voicemeeter is installed. If not, it offers to install Voicemeeter Banana with `winget`.
3. Builds a self-contained Release package with Velopack, without the tests.
4. Installs Mixdeck under `%LOCALAPPDATA%\MixdeckApp`, with Start menu and Desktop shortcuts and an entry in Settings > Apps. If Mixdeck is already installed, it closes it and applies a Velopack update. Settings are kept. Mixdeck then starts.

The installed version is `<Directory.Build.props version>-local.<date>`. The first version published on GitHub (`0.1.0`) replaces it through the automatic update. After a `git pull`, run the script again.

`-RunTests` runs the unit tests before installing. `-NoPrompt` asks no questions. On failure, the Velopack log is `%LOCALAPPDATA%\velopack\velopack.log`.

### From a release

Published builds are attached to [GitHub Releases](https://github.com/Anarchiejs/Mixdeck/releases) by `.github/workflows/release.yml` when a `v*` tag is pushed.

No release exists until a tag is created. The installer is the Velopack one (package id `MixdeckApp`). Later updates are delta packages built from the previous release.

## Build from source

Clone the repository, then from the root:

```powershell
dotnet restore Mixdeck.slnx
dotnet build Mixdeck.slnx -c Release -nologo
dotnet test Mixdeck.slnx -c Release -nologo
```

The unit tests (`tests/Mixdeck.Core.Tests`, `tests/Mixdeck.Voicemeeter.Tests`, `tests/Mixdeck.Platform.Tests`, `tests/Mixdeck.FlStudio.Tests`) do not launch Voicemeeter. The client is tested against a simulated API.

`dotnet` must be a .NET SDK, not a runtime-only install. `build/pack.ps1` uses `dotnet` from `PATH` when that install contains an `sdk` folder, then `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`.

Local package (tests, self-contained `win-x64` publish, then `vpk pack` into `artifacts/`):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack.ps1 -Version 0.1.0
```

The same sequence without Velopack:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack.ps1 -Version 0.1.0 -SkipPack
```

`vpk pack` requires `src/Mixdeck.App/Assets/Mixdeck.ico`. `artifacts/` and `publish/` are gitignored.

The `vpk` tool is pinned in `.config/dotnet-tools.json` at `1.2.161`, the same version as the `Velopack` NuGet package.

UI tests (`tests/Mixdeck.UiTests`) use FlaUI and need an interactive Windows desktop. They do not run in GitHub Actions. Locally:

```powershell
$env:MIXDECK_UI_TESTS = '1'
dotnet test tests/Mixdeck.UiTests -c Release
```

## Demo mode

`--demo` replaces Voicemeeter with a simulated engine (labels, gains, animated meters). Use it to try Mixdeck on a machine without Voicemeeter.

```powershell
dotnet run --project src/Mixdeck.App -- --demo
```

Other arguments: `--minimized` (start with Windows, nothing shown), `--gallery` (control gallery, for development), `--demo=standard`, `--demo=banana`, `--demo=potato`.

Console tools: `tools/Mixdeck.Probe` (Voicemeeter state and text meters, `--sim` for the simulation), `tools/Mixdeck.PlatformProbe` (foreground, processes, hotkeys, taskbar), `tools/Mixdeck.PrivateApiProbe` (undocumented audio-policy and tray COM, for development).

## FL Studio

Mixdeck talks to FL Studio through two virtual MIDI ports and a device script that runs inside FL. FL Studio has no other external control surface that Mixdeck uses.

`Installer Mixdeck.cmd` can do the setup:

- install [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html) when it is missing (`winget`; Windows may ask for administrator approval), start it with Windows, and create the ports `Mixdeck to FL` and `FL to Mixdeck`;
- copy [`fl/Mixdeck/device_Mixdeck.py`](fl/Mixdeck/device_Mixdeck.py) to `Documents\Image-Line\FL Studio\Settings\Hardware\Mixdeck\`;
- write FL's MIDI settings (*Options > MIDI*): input `Mixdeck to FL` enabled, controller type **Mixdeck**, port 10 (or the next free port); output `FL to Mixdeck` on the same port; no port on the `Mixdeck to FL` output. FL rewrites these settings when it exits, so the installer waits until FL is closed. FL must have been launched at least once.

By hand, the same three steps. Do not assign a port to the `Mixdeck to FL` output: the script's replies would loop back into its own input, and FL sometimes restores that when the script is reloaded.

The FL Studio page then shows Connected and lists the tracks in use (Master, renamed tracks, or tracks with effects).

Limits: FL must be open with the script active. A plugin can be enabled or bypassed in its slot, not loaded from Mixdeck. For a profile when FL starts: save a profile while FL is connected, then add a rule "FL64.exe starts → this profile" on the Rules page. After a script update, FL keeps the old script in memory: set the controller type to "(generic controller)" and back to "Mixdeck".

Console diagnostic: `tools/Mixdeck.FlProbe` (`list`, `ping`, `get <track>`, `mute`, `vol`, `slot`, `watch`, …).

## Discord

Mixdeck's microphone mute and Discord's mute stay the same: muting one mutes the other. Discord only allows this control for Discord applications you own or that list you as a tester:

1. On [discord.com/developers/applications](https://discord.com/developers/applications), create an application.
2. Under *OAuth2*, copy the client id and the client secret, and add `http://localhost` to *Redirects*.
3. In Mixdeck, *Settings > Discord*: turn sync on, paste the id and the secret, click "Authorize in Discord", and accept the prompt.

The secret and the tokens are encrypted for your Windows user (DPAPI) in `discord.bin`. They are never written to `settings.json` or the logs. The requested scope is `rpc`. When Discord starts, Mixdeck's mute state wins.

## Logs and files

`MixdeckPaths` puts files in `%LOCALAPPDATA%\Mixdeck`. Settings > "Open the log folder" opens that directory.

| Path | Role |
| --- | --- |
| `%LOCALAPPDATA%\Mixdeck\logs` | Logs and crash reports |
| `%LOCALAPPDATA%\Mixdeck\settings.json` | Settings (`settings.json.bak` is the backup) |
| `%LOCALAPPDATA%\Mixdeck\profiles` | Profiles |
| `%LOCALAPPDATA%\Mixdeck\discord.bin` | Discord secret and tokens, DPAPI |

## Architecture

Five projects, solution `Mixdeck.slnx`:

| Project | Role |
| --- | --- |
| `src/Mixdeck.Core` | Model, contracts, and settings. `net10.0`, no Windows dependency. |
| `src/Mixdeck.Voicemeeter` | Voicemeeter client. `net10.0-windows`, x64. |
| `src/Mixdeck.Platform` | Win32 services (hotkeys, processes, foreground, startup, taskbar, audio sessions). x64. |
| `src/Mixdeck.FlStudio` | FL Studio client: SysEx protocol (`FlProtocol`, mirrored by `fl/Mixdeck/device_Mixdeck.py`), Windows MIDI ports, simulated FL for demo mode. |
| `src/Mixdeck.App` | Unpackaged WinUI 3 UI. Windows App SDK is bundled. Assembly name `Mixdeck`. |

The UI talks to Voicemeeter through `IMixer` and to FL Studio through `IDaw`. A command queue runs on a dedicated thread. The state read back from the device is what the UI shows.

Third-party notices: [NOTICE](NOTICE). Security reports: [SECURITY.md](SECURITY.md). Building and contributing: [CONTRIBUTING.md](CONTRIBUTING.md).

## Publish a version

1. Add a `## [X.Y.Z]` section to `CHANGELOG.md`.
2. Push the tag `vX.Y.Z`.

The release workflow publishes a self-contained `win-x64` build, restores `vpk`, downloads the previous GitHub release for deltas, packs, and uploads the result to GitHub Releases with that changelog section. The token is `secrets.GITHUB_TOKEN` (`contents: write`). No secret is written into the repository. Signing is described in [docs/SIGNATURE.md](docs/SIGNATURE.md).

## License

[MIT](LICENSE). Copyright (c) 2026 Anarchy (@Anarchiejs).
