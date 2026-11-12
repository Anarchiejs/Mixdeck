# Security

Mixdeck is maintained by Anarchy ([@Anarchiejs](https://github.com/Anarchiejs)).

## Reporting a vulnerability

Open a private GitHub security advisory on [Anarchiejs/Mixdeck](https://github.com/Anarchiejs/Mixdeck/security/advisories/new). Do not file a public issue for a vulnerability, and do not paste a secret, a token, or a crash log that contains personal paths.

If private advisories are unavailable, open an issue that describes the impact without exploit steps, secrets, or personal paths.

## What this project stores

- Settings, profiles, logs, and crash reports stay in `%LOCALAPPDATA%\Mixdeck` (or `MIXDECK_DATA_DIR`). They are not uploaded.
- Update checks, when enabled on an installed build, request GitHub Releases for this repository. They do not send a Mixdeck account, because there is none.

## Scope

The undocumented Windows audio and tray COM interfaces can fail closed on a future Windows build. A failure of those interfaces is a compatibility bug, not by itself a vulnerability. Report it if Mixdeck changes a device the user did not select, keeps running after uninstall, or writes outside `%LOCALAPPDATA%\Mixdeck` and `%LOCALAPPDATA%\MixdeckApp`.
