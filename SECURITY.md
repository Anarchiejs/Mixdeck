# Security

Mixdeck is maintained by Anarchy ([@Anarchiejs](https://github.com/Anarchiejs)).

## Reporting a vulnerability

Open a private GitHub security advisory on [Anarchiejs/Mixdeck](https://github.com/Anarchiejs/Mixdeck/security/advisories/new). Do not file a public issue for a vulnerability, and do not paste a Discord application secret, token, or `discord.bin` contents.

If private advisories are unavailable, open an issue that describes the impact without exploit steps, secrets, or personal paths.

## What this project stores

- Settings, profiles, logs, and crash reports stay in `%LOCALAPPDATA%\Mixdeck` (or `MIXDECK_DATA_DIR`). They are not uploaded.
- A Discord application secret and OAuth tokens, when the user turns sync on, are protected with Windows DPAPI for the current user (`discord.bin`). The log stores Discord error codes only, never the secret or the token.
- Update checks, when enabled on an installed build, request GitHub Releases for this repository. They do not send a Mixdeck account, because there is none.

## Scope

The undocumented Windows audio and tray COM interfaces can fail closed on a future Windows build. A failure of those interfaces is a compatibility bug, not by itself a vulnerability. Report it if Mixdeck changes a device the user did not select, keeps running after uninstall, or writes outside `%LOCALAPPDATA%\Mixdeck` and `%LOCALAPPDATA%\MixdeckApp`.
