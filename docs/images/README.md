# Screenshots

Put screenshots that the README links to in this folder (`docs/images/…`).

No screenshot is checked in yet. Capture them on Windows 10 or 11 with Mixdeck running. Demo mode is enough and avoids personal device names:

```powershell
dotnet run --project src/Mixdeck.App -c Release -- --demo --flyout
dotnet run --project src/Mixdeck.App -c Release -- --demo --page=Mixer
dotnet run --project src/Mixdeck.App -c Release -- --demo --page=Rules
dotnet run --project src/Mixdeck.App -c Release -- --demo --page=Settings
dotnet run --project src/Mixdeck.App -c Release -- --demo --page=About
```

Expected file names: `flyout.png`, `mixer.png`, `rules.png`, `settings.png`, `about.png`.

Do not include a Windows user name, a machine name, or a personal audio-device name in the image. This environment cannot capture the WinUI window, so the files are not generated here.
