# Contributing

Mixdeck runs on Windows and macOS. The WinUI app and the Win32 services stay Windows-only. Maintainer: Anarchy ([@Anarchiejs](https://github.com/Anarchiejs)).

## Build

- .NET SDK version in `global.json` (`10.0.403`, `latestFeature` roll-forward). `dotnet` on `PATH` must be an SDK.
- Windows: Windows 10 version 19041 or later, or Windows 11. A per-user SDK belongs in `%LOCALAPPDATA%\Microsoft\dotnet`. Voicemeeter is required to exercise the real client. Unit tests use the simulated API and do not need it.
- macOS: test `Mixdeck.Portable.slnx`. The Windows solution does not restore on a Mac, because it needs the Windows targeting pack.

```powershell
dotnet test Mixdeck.slnx -c Release -nologo
```

```bash
dotnet test Mixdeck.Portable.slnx -c Release -nologo
```

UI tests are opt-in and are not run in GitHub Actions:

```powershell
$env:MIXDECK_UI_TESTS = '1'
dotnet test tests/Mixdeck.UiTests -c Release
```

## Rules

- Do not commit secrets, `.env` files, tokens, certificate material, or absolute paths from your Windows profile.
- Do not commit `.cursor/`, `.claude/`, `.orchestra/`, or `.grok/` folders.
- User-facing strings go in both `src/Mixdeck.App/Strings/en-US/Resources.resw` and `fr-FR/Resources.resw`. The localization test fails when a key exists in only one language.
- `Directory.Packages.props` is the only place package versions change. Keep `FluentAssertions` on the 7.x Apache-2.0 line. 8.x is under a commercial Xceed license.
- The Voicemeeter remote DLL is not redistributed.

## Releases

1. Add `## [X.Y.Z]` to `CHANGELOG.md` with the notes that should appear on GitHub Releases.
2. Tag `vX.Y.Z` and push it. `.github/workflows/release.yml` packs the Windows build with Velopack, then adds the macOS package to the same release.

Signing is optional and documented in `docs/SIGNATURE.md`.
