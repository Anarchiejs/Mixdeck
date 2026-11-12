# Contributing

Mixdeck is a Windows-only x64 app. Maintainer: Anarchy ([@Anarchiejs](https://github.com/Anarchiejs)).

## Build

- Windows 10 version 19041 or later, or Windows 11.
- .NET SDK version in `global.json` (`10.0.403`, `latestFeature` roll-forward). `dotnet` on `PATH` must be an SDK. A per-user SDK belongs in `%LOCALAPPDATA%\Microsoft\dotnet`.
- Voicemeeter is required to exercise the real client. Unit tests use the simulated API and do not need it.

```powershell
dotnet test Mixdeck.slnx -c Release -nologo
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
2. Tag `vX.Y.Z` and push it. `.github/workflows/release.yml` packs with Velopack.

Signing is optional and documented in `docs/SIGNATURE.md`.
