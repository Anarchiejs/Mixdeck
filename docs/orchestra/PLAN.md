# Mixdeck v1.0 — plan d'orchestration détaillé

Source : `Obsidian-Vault/Projets/Mixdeck/Mixdeck - Detaille.md` (plan A→I). Ce fichier le traduit en lots exécutables.
Racine du dépôt : `%USERPROFILE%\Documents\Mixdeck` — solution `Mixdeck.slnx` (format .slnx, le défaut de .NET 10).

## Objectif
Appli Windows 11 native (C# 14 / .NET 10, WinUI 3, non empaquetée, x64) qui pilote Voicemeeter Banana depuis la
barre des tâches : panneau rapide, console complète, matrice de routage, profils avec fondu, règles par application,
raccourcis globaux + push-to-talk, OSD, reconnexion automatique, journaux, installateur Velopack + mises à jour, CI.

## Environnement (constaté le 2026-10-27)
- SDK .NET **10.0.403** installé par utilisateur dans `%USERPROFILE%\.dotnet` (le `dotnet` de Program Files n'a
  que des runtimes). **Toujours appeler** `%USERPROFILE%\.dotnet\dotnet.exe` (ou mettre ce dossier en tête du PATH
  et `DOTNET_ROOT` = ce dossier).
- Voicemeeter Banana installé : `C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll` (pas de `VoicemeeterRemote.h` sur le disque).
- Paquets (versions centralisées dans `Directory.Packages.props`) : WindowsAppSDK 2.5.1, Win2D 1.4.0,
  H.NotifyIcon.WinUI 2.4.1, CommunityToolkit.Mvvm 8.4.2, Extensions.Hosting 10.0.14, Serilog, Velopack 1.2.161,
  xUnit 2.9.3, FluentAssertions 7.2.0 (dernière version Apache), TimeProvider.Testing 10.11.0.

## Commandes
- Build : `%USERPROFILE%\.dotnet\dotnet.exe build Mixdeck.slnx -nologo -v q`
- Tests : `%USERPROFILE%\.dotnet\dotnet.exe test Mixdeck.slnx -nologo`
- Lancer l'appli : `src\Mixdeck.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Mixdeck.exe` (`--demo` = faux moteur, sans Voicemeeter)

## Architecture et contrats (écrits par Claude, NE PAS MODIFIER sans le demander dans le rapport)
```
src/Mixdeck.Core          net10.0, aucune dépendance Windows
  Model/                  BananaLayout, MixerState (StripState, BusState, MixerSnapshot, StereoLevel, LevelFrame,
                          MixerConnectionState), VmParam (noms de paramètres), Profile, Hotkeys, Rule   ← CONTRATS
  Abstractions/           IMixer, IProfileService, ISettingsStore, IPlatformServices (IHotkeyService,
                          IProcessWatcher, IForegroundWatcher, IAutoStart), IOsdService                 ← CONTRATS
  Settings/               AppSettings, MixdeckPaths                                                      ← CONTRATS
  Settings/SettingsStore.cs, Profiles/, Rules/, Audio/, Automation/, Serialization/                      ← L2
src/Mixdeck.Voicemeeter   net10.0-windows x64 : P/Invoke + VoicemeeterMixer : IMixer + simulation         ← L1
src/Mixdeck.Platform      net10.0-windows x64 : Win32                                                    ← L3
  Taskbar/TaskbarPlacement.cs                                                                             ← CONTRAT
src/Mixdeck.App           WinUI 3, MVVM                                                                    ← Claude (shell) + L5 + L7
tests/Mixdeck.Core.Tests, tests/Mixdeck.Voicemeeter.Tests
```
Flux : l'interface appelle `IMixer.SetFloat("Strip[0].Mute", 1)` → file de commandes → thread dédié applique →
au tour suivant `IsParametersDirty` → relecture → `SnapshotChanged` → ViewModels (via DispatcherQueue) → interface.
Une seule source de vérité : l'état de Voicemeeter.

## Lots

| Lot | Contenu | Agent | Pourquoi | Dossier |
|---|---|---|---|---|
| L0 | Squelette, props, paquets, contrats, thème de base | Claude | Décisions structurantes | main |
| L1 | Client Voicemeeter (P/Invoke, thread dédié, niveaux 60 Hz, reconnexion, simulation) + tests + sonde console | GPT-6.1 Sol (high) | Multi-fichiers, concurrence | worktree `orch/L1` |
| L2 | Cœur : SettingsStore, ProfileService (fondus), RuleEngine, MeterBallistics, AutomationCoordinator, ActionDispatcher, JSON + tests | GPT-6.1 Sol (medium) | Logique pure bien spécifiée | worktree `orch/L2` |
| L3 | Plateforme Win32 : raccourcis + PTT, processus, premier plan, démarrage auto, barre des tâches, instance unique | GPT-6.1 Sol (high) | Interop Win32 délicate | worktree `orch/L3` |
| L4 | Shell WinUI : hôte/DI, icône, panneau ancré (DPI, multi-écran), fenêtre principale + navigation, OSD, thèmes | Claude | Cœur visuel, jugement | main |
| L5 | Contrôles : VuMeter (Win2D), Fader, RouteButton, ProfilePill | GPT-6.1 Sol (medium) | Volume, bien borné | worktree `orch/L5` |
| L6 | Outillage : CI GitHub Actions, script Velopack, README, CHANGELOG, LICENSE, .editorconfig | Grok 4.7 | Terminal/CI | worktree `grok/*` (pont) |
| L7 | Pages + ViewModels : Console, Routage, Profils, Règles, Réglages, À propos | GPT-6.1 Sol | Volume, après L4+L5 | worktree `orch/L7` |
| L8 | Revue adverse : thread Voicemeeter, crochet clavier, fondus, écriture JSON | GPT-6 Astra | Code sensible | lecture seule |
| L9 | Recette sur machine propre (installer, lancer `--demo`, parcours) | Grok Bot | Machine vierge | nécessite un push (accord utilisateur) |

## Table des propriétaires de fichiers
| Fichiers | Propriétaire |
|---|---|
| `Mixdeck.slnx`, `Directory.*.props`, `global.json`, `.gitignore`, tous les `*.csproj` | **Claude seul** — les lots notent leurs besoins (paquet, référence de projet, nouveau projet) dans OPEN |
| `src/Mixdeck.Core/Model/**`, `Abstractions/**`, `Settings/AppSettings.cs`, `Settings/MixdeckPaths.cs` | Claude (contrats) |
| `src/Mixdeck.Platform/Taskbar/TaskbarPlacement.cs` | Claude (contrat) |
| `src/Mixdeck.App/App.xaml*`, `Program.cs`, `Themes/**`, `Services/**`, `Views/Shell/**`, `Views/Flyout/**`, `Views/Osd/**`, `ViewModels/Shell/**`, `ViewModels/Flyout/**` | Claude (L4) |
| `src/Mixdeck.App/Controls/**` | L5 |
| `src/Mixdeck.App/Views/Pages/**`, `src/Mixdeck.App/ViewModels/Pages/**` | L7 |

## Critères d'acceptation v1.0 (section 6 de la note)
- Toutes les fonctionnalités de la section 2 de la note marchent sur Voicemeeter Banana.
- Tests du cœur et du client Voicemeeter verts dans la CI.
- CPU < 1 % au repos (panneau fermé : niveaux non lus), interface fluide à 60 i/s avec les vumètres.
- Installateur + mise à jour automatique testés de bout en bout.
- README, captures, CHANGELOG, licence.

## Règles communes à tous les lots
- Ne lis pas `.env*` ni de fichiers de clés ; valeurs de test uniquement.
- Aucun commit, push, publication. Ne modifie que les fichiers autorisés de ta fiche.
- Commentaires et textes d'interface en **français** ; identifiants de code en anglais.
- Code idiomatique .NET 10 / C# 14 : `LibraryImport` (pas `DllImport`), `TimeProvider` pour le temps (testable),
  `ILogger<T>`, `Nullable` respecté, pas d'avertissement nouveau.
