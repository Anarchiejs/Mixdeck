# Control gallery

The gallery is a development window. The shell opens it with `--gallery`.

To build and launch it on its own, from the repository root, after a normal build is not required:

```powershell
dotnet build src/Mixdeck.App/Mixdeck.App.csproj -nologo -v q -p:BuildProjectReferences=false -p:StartupObject=Mixdeck.App.Controls.Gallery.GalleryLauncher "-p:DefineConstants=MIXDECK_GALLERY%3BDISABLE_XAML_GENERATED_MAIN"
```

Run the `Mixdeck.exe` produced under `src/Mixdeck.App/bin`. The alternate entry point is compiled only with `MIXDECK_GALLERY`. `--smoke` closes the gallery after three seconds. `MIXDECK_GALLERY_LOG` can name a file for handled exceptions. Build again without those constants to restore the shell entry point.

`dotnet` must be an SDK on `PATH`, or the per-user SDK in `%LOCALAPPDATA%\Microsoft\dotnet`.

Visual check: light and dark theme, Windows high contrast, checked, hover, pressed, focused, and disabled states, profile changes with click and Left/Right/Home/End/Enter/Space, a view-model refusal that leaves the selection unchanged, rename (double-click or F2, Enter/Esc), fader keyboard and wheel with and without Shift, double-click back to 0 dB. Move the window across monitors with different DPI and check the meters with Narrator. Close and reopen the gallery to confirm CanvasControl and the timer are released.

`ProfilePillList` items are plain objects. Bindings use `Name`, `Icon`, `Color`, and `IsActive`. `ItemInvoked` requests a change. Only `SelectedItem` sets the displayed selection.
