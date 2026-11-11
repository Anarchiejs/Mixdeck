# Private API probe

Development tool for the same code as `AppAudioRouter` and `TrayNotifyInterop`. It adds no package. JSON goes to stdout. HRESULTs, IIDs, native values, and restorations go to stderr.

Exit codes: 0 = the operation and the read-back succeeded, 1 = bad syntax, 2 = API missing or refused, 3 = restoration not confirmed. Exit code 0 does not by itself prove that a stream moved audibly or that the tray icon is visible.

These COM interfaces are undocumented. They can fail on a future Windows version. Run the probe only on a machine you own. Do not paste endpoint ids, executable paths, or user-profile paths into the repository.

Build from the repository root:

```powershell
dotnet build tools/Mixdeck.PrivateApiProbe/Mixdeck.PrivateApiProbe.csproj -nologo
```

The executable is under `tools/Mixdeck.PrivateApiProbe/bin`. Commands:

```text
qi-negative
route-list
route-get <pid>
route-set <pid> <endpointId|default>
route-roundtrip <endpointId>
tray-get <full path of an executable>
tray-roundtrip <full path of an executable>
tray-promote <full path of an executable> on|off
```

`route-set <pid> default` puts that process back on the default output for the multimedia and console roles. It does not touch the communications role and it never calls `ClearAllPersistedApplicationDefaultEndpoints`. `tray-promote` only changes active entries for that exact path. `off` asks for preference 1 and `on` asks for preference 2.

`route-get` distinguishes a failure (`Success=false`) from the Windows default (`Success=true`, both values null). A process with no audio session can be refused by Windows. That refusal is not turned into a default preference.

ABI notes live at the top of the interop sources. See also [NOTICE](../../NOTICE).
