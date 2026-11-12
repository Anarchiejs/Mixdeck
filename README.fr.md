<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="design/logo/final/mixdeck-lockup-white.svg">
    <img src="design/logo/final/mixdeck-lockup.svg" alt="Mixdeck" width="420">
  </picture>
</p>

# Mixdeck

[English](README.md)

Mixdeck pilote l'audio que vous utilisez. Sous Windows, c'est une application de barre des tâches (C# / .NET 10, WinUI 3, non empaquetée, x64) pour [Voicemeeter](https://vb-audio.com/Voicemeeter/). Sous Linux et macOS, le même cœur tourne dans une fenêtre Avalonia et parle aux périphériques du système. Voicemeeter lui-même reste réservé à Windows.

**État.** Préversion. La version d'assembly dans `Directory.Build.props` est **0.1.0**. Aucune publication GitHub Releases n'existe encore. L'application compile, démarre et pilote Voicemeeter. Tant que la [signature Azure Trusted Signing](docs/SIGNATURE.md) n'est pas configurée, l'installateur n'est **pas signé** : SmartScreen avertit au premier lancement.

Mixdeck n'est pas affilié à VB-Audio ni à Image-Line.

## Plateformes

| | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Application | Barre des tâches WinUI 3 (`Mixdeck.App`) | Application Avalonia (`Mixdeck.Desktop`) | Application Avalonia (`Mixdeck.Desktop`) |
| Mixeur Voicemeeter, profils, règles, atténuation, préréglages du micro | Oui | Indisponible. `--demo` lance le vrai simulateur, affiché comme démonstration | Indisponible. Même interrupteur |
| Périphériques, périphérique par défaut, volume, sourdine | Oui | Oui, via `pactl` (PulseAudio ou pipewire-pulse) | Oui, Core Audio |
| Volume par application | Oui | Oui, entrées de sortie Pulse | Indisponible. Core Audio n'a pas de volume public par application |
| Envoyer une application vers une autre sortie | Oui, API COM non documentée | Oui, `pactl move-sink-input` | Indisponible |
| Raccourcis globaux et push-to-talk | Oui | X11 seulement. Sous Wayland, la page dit qu'ils sont indisponibles | Oui, Carbon `RegisterEventHotKey` |
| Application au premier plan | Oui | X11 `_NET_ACTIVE_WINDOW` seulement | Oui, `NSWorkspace` |
| Règles par processus | Oui | Oui, `/proc` | Oui |
| FL Studio | loopMIDI | Indisponible. FL Studio n'a pas de version Linux | Core MIDI : un port IAC du nom réglé, ou les ports virtuels « Mixdeck to FL » et « FL to Mixdeck » |
| Démarrage à l'ouverture de session | Clé Run, versions installées seulement | Autostart XDG, versions installées seulement | LaunchAgent, versions installées seulement |
| Installation et mises à jour | Installeur Velopack (`build/pack.ps1`) | AppImage et une archive `.tar.gz` (`build/pack.sh`) | Paquet Velopack construit sur un Mac, plus un `.app` |

Une copie lancée depuis `bin/` ne s'inscrit pas au démarrage, sur aucun système. Les données restent sur la machine : `%LOCALAPPDATA%\Mixdeck` sous Windows, `$XDG_DATA_HOME/Mixdeck` ou `~/.local/share/Mixdeck` sous Linux, `~/Library/Application Support/Mixdeck` sous macOS. `MIXDECK_DATA_DIR` remplace cette racine.

## Fonctionnalités

- Icône près de l'horloge : clic gauche = panneau, clic milieu = couper le micro, info-bulle avec l'état du micro, le volume A1 et le profil.
- Panneau ancré à la barre des tâches. La molette règle le volume de la ligne après un clic sur cette ligne.
- Volume par application et « Envoyer vers… » pour choisir la sortie d'une application.
- Page Appareils : micro, casque et enceintes de Voicemeeter, préréglages du micro, redémarrage du moteur audio.
- Atténuation automatique : une entrée baisse quand une autre a du signal.
- Badge et son de confirmation facultatifs quand le micro est coupé.
- « M'écouter » envoie le micro vers un bus, en général le casque.
- Console : tranches qui passent à la ligne, seulement celles reliées à un périphérique (et les tranches virtuelles), égaliseur repliable.
- Matrice de routage, profils avec fondu, règles par application.
- Raccourcis globaux, push-to-talk, raccourci par profil, profil au démarrage.
- Page FL Studio : piste, volume, muet, effets de piste et slots (avec le nom du plugin). Un profil peut garder l'état de FL.
- Sortie Windows par défaut depuis le panneau, sortie et entrée depuis la page Appareils.
- Journal local des plantages et une seule relance automatique.
- Affichage à l'écran (OSD).
- Installation et mises à jour par [Velopack](https://velopack.io/).

## Captures d'écran

Aucune capture n'est versionnée. Il faut les prendre sous Windows. Déposez-les ensuite dans [`docs/images/`](docs/images/) et citez-les ici.

## Prérequis

Toutes les plateformes : le SDK .NET 10 pour compiler. `global.json` fixe `10.0.403` avec un report `latestFeature`. Les versions publiées sont autonomes.

Windows :

- Windows 10 version 19041 ou plus récente, ou Windows 11. x64 seulement. Le projet WinUI cible `10.0.19041.0`.
- Voicemeeter Standard, Banana ou Potato, déjà installé. Mixdeck charge `VoicemeeterRemote64.dll` depuis cette installation. La DLL n'est pas livrée avec Mixdeck.
- Facultatif : FL Studio et [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html).

Linux :

- `pactl`, fourni par PulseAudio ou `pipewire-pulse`. Sans serveur audio, la page Audio le dit et n'invente pas de périphériques.
- Une session X11 pour les raccourcis globaux et l'application au premier plan. Sous Wayland, ces deux fonctions restent coupées ; le volume des périphériques fonctionne.
- FL Studio n'est pas disponible.

macOS :

- macOS 12 ou plus récent. Core Audio fait partie du système.
- Facultatif : FL Studio. Mixdeck utilise Core MIDI et n'installe pas loopMIDI.

## Confidentialité

Mixdeck n'a pas de compte de télémétrie et n'envoie ni journaux ni rapports de plantage. Linux et macOS gardent les mêmes fichiers dans le dossier indiqué sous Plateformes. L'entrée de démarrage n'est écrite que pour un paquet installé.

| Quoi | Où |
| --- | --- |
| Réglages, profils, journaux, plantages | `%LOCALAPPDATA%\Mixdeck` sur ce PC. Journaux quotidiens, 14 fichiers, 10 Mo chacun. Un plantage contient l'exception et les 50 dernières lignes de journal. Rien n'est envoyé. |
| Programme installé | `%LOCALAPPDATA%\MixdeckApp`. La désinstallation ne supprime pas `%LOCALAPPDATA%\Mixdeck`. |
| Démarrage avec Windows | Activé par défaut pour une version installée. L'entrée est dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Un exe lancé depuis `bin\` ne s'inscrit pas. |
| Raccourcis globaux | Enregistrés auprès de Windows tant que Mixdeck tourne. |
| Règles | Nom de l'exécutable au premier plan et noms des processus lancés. En mémoire et dans vos règles. Jamais envoyés. |
| Volume par application | Noms affichés des sessions Windows, noms de processus et volumes de la sortie par défaut. |
| Périphériques par défaut et « Envoyer vers… » | API COM non documentées de Windows (`IPolicyConfig`, `IAudioPolicyConfig`, zone de notification de l'Explorateur). Elles peuvent cesser de fonctionner après une mise à jour de Windows. Mixdeck désactive alors la fonction ou affiche l'erreur. |
| FL Studio | MIDI local, par les deux ports loopMIDI. L'installateur peut écrire les réglages MIDI de FL et copier le script dans Documents. |
| Mises à jour | Une version installée interroge GitHub Releases au démarrage si Réglages > Rechercher les mises à jour est activé (c'est le cas par défaut). C'est une requête vers `https://github.com/Anarchiejs/Mixdeck`. Coupez l'interrupteur pour arrêter la recherche automatique. La page À propos peut encore chercher quand vous cliquez sur le bouton. Les builds de développement ne sont pas installés : ils ne cherchent pas. |

`MIXDECK_DATA_DIR` remplace la racine des données (tests, recette).

## Installation

### Depuis les sources, en un clic

Double-cliquez sur **`Installer Mixdeck.cmd`** à la racine. Aucun droit administrateur n'est demandé. `build/install.ps1` :

1. cherche un SDK .NET 10 (10.0.403 ou plus récent) ; sinon il l'installe pour l'utilisateur dans `%LOCALAPPDATA%\Microsoft\dotnet` ;
2. vérifie que Voicemeeter est installé ; sinon il propose de l'installer avec `winget` (Voicemeeter Banana) ;
3. compile une version Release autonome et l'empaquette avec Velopack, sans les tests ;
4. installe Mixdeck dans `%LOCALAPPDATA%\MixdeckApp`, avec des raccourcis Menu Démarrer et Bureau. Si Mixdeck est déjà installé, il le ferme et applique une mise à jour. Les réglages sont conservés.

La version installée est `<version de Directory.Build.props>-local.<date>`. La première version publiée (`0.1.0`) la remplace par la mise à jour automatique.

`-RunTests` lance les tests unitaires avant l'installation. `-NoPrompt` ne pose aucune question. En cas d'échec, le journal Velopack est `%LOCALAPPDATA%\velopack\velopack.log`.

### Depuis une publication

Les versions publiées sont attachées aux [publications GitHub](https://github.com/Anarchiejs/Mixdeck/releases) par `.github/workflows/release.yml` lorsqu'une étiquette `v*` est poussée. Aucune publication n'existe tant qu'une étiquette n'a pas été créée.

## Compiler

À la racine du dépôt.

Windows (application WinUI, services Windows et projets portables) :

```powershell
dotnet restore Mixdeck.slnx
dotnet build Mixdeck.slnx -c Release -nologo
dotnet test Mixdeck.slnx -c Release -nologo
```

Linux et macOS (`Mixdeck.Portable.slnx` regroupe les projets qui se compilent sans le kit de ciblage Windows) :

```bash
dotnet test Mixdeck.Portable.slnx -c Release -nologo
dotnet run --project src/Mixdeck.Desktop -- --self-check
bash build/pack.sh --version 0.1.0
bash build/install.sh
```

`build/pack.sh` accepte `--skip-tests`, `--skip-pack`, `--require-vpk`, `--version` et `--rid` (`linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`). L'AppImage Linux a besoin de `mksquashfs` (`squashfs-tools`). `vpk [osx] pack` s'exécute sur un Mac. Le script écrit quand même `artifacts/Mixdeck.app` et une archive. `build/install.sh` installe pour l'utilisateur courant, sans mot de passe administrateur.

Les tests unitaires ne lancent pas Voicemeeter : le client est testé contre une API simulée.

`dotnet` doit être un SDK, pas seulement un runtime. `build/pack.ps1` prend `dotnet` du `PATH` quand cette installation contient un dossier `sdk`, sinon `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack.ps1 -Version 0.1.0
```

Les tests d'interface (`tests/Mixdeck.UiTests`, FlaUI) demandent une session de bureau interactive. Ils ne font pas partie de la CI GitHub. En local :

```powershell
$env:MIXDECK_UI_TESTS = '1'
dotnet test tests/Mixdeck.UiTests -c Release
```

## Mode démonstration

```powershell
dotnet run --project src/Mixdeck.App -- --demo
```

Sous Linux et macOS, le même interrupteur pilote le simulateur dans l'application Avalonia. Un lancement normal ne fait pas semblant que Voicemeeter est installé.

```bash
dotnet run --project src/Mixdeck.Desktop -- --demo
```

Autres arguments : `--minimized`, `--gallery`, `--demo=standard`, `--demo=banana`, `--demo=potato`.

Outils console : `tools/Mixdeck.Probe`, `tools/Mixdeck.PlatformProbe`, `tools/Mixdeck.PrivateApiProbe` (API COM non documentées, pour le développement).

## FL Studio

Mixdeck parle à FL Studio par deux ports MIDI virtuels et le script [`fl/Mixdeck/device_Mixdeck.py`](fl/Mixdeck/device_Mixdeck.py).

`Installer Mixdeck.cmd` peut :

- installer [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html) s'il manque (`winget` ; Windows peut demander l'accord administrateur), le lancer avec Windows et créer les ports `Mixdeck to FL` et `FL to Mixdeck` ;
- copier le script dans `Documents\Image-Line\FL Studio\Settings\Hardware\Mixdeck\` ;
- écrire les réglages MIDI de FL (*Options > MIDI*) pendant que FL est fermé : entrée `Mixdeck to FL` activée, type **Mixdeck**, port 10 (ou le suivant libre) ; sortie `FL to Mixdeck` sur le même port ; aucun port sur la sortie `Mixdeck to FL`.

Sans port sur la sortie `Mixdeck to FL`, sinon les réponses du script reviennent dans sa propre entrée.

Limites : FL doit être ouvert avec le script actif. Un plugin s'active ou se coupe dans son slot, il ne se charge pas depuis Mixdeck. Pour un profil au lancement de FL : enregistrez un profil pendant que FL est connecté, puis une règle « FL64.exe démarre → ce profil ».

Diagnostic : `tools/Mixdeck.FlProbe`.

## Journaux

Les fichiers sont dans `%LOCALAPPDATA%\Mixdeck` sous Windows, `$XDG_DATA_HOME/Mixdeck` ou `~/.local/share/Mixdeck` sous Linux, et `~/Library/Application Support/Mixdeck` sous macOS.

| Chemin | Rôle |
| --- | --- |
| `<données>/logs` | Journaux et plantages |
| `<données>/settings.json` | Réglages (`settings.json.bak` en secours) |
| `<données>/profiles` | Profils |

## Architecture

`Mixdeck.slnx` est la solution Windows. `Mixdeck.Portable.slnx` est celle que la CI Linux et macOS compile.

| Projet | Rôle |
| --- | --- |
| `src/Mixdeck.Core` | Modèle, contrats, réglages. `net10.0`, sans dépendance Windows. |
| `src/Mixdeck.Voicemeeter` | Client Voicemeeter et simulateur. `net10.0`. La DLL native n'est chargée que sous Windows. |
| `src/Mixdeck.Platform` | Services Win32. Windows seulement. |
| `src/Mixdeck.Platform.Desktop` | Audio, raccourcis, premier plan, processus, démarrage et instance unique pour Linux et macOS. |
| `src/Mixdeck.FlStudio` | Protocole SysEx. Windows : winmm. macOS : Core MIDI. Linux : intégration indisponible. |
| `src/Mixdeck.App` | Interface WinUI 3. C'est le produit Windows. |
| `src/Mixdeck.Desktop` | Interface Avalonia pour Linux et macOS. Ce n'est pas ce que `build/pack.ps1` installe. |

Mentions tierces : [NOTICE](NOTICE). Sécurité : [SECURITY.md](SECURITY.md). Contribution : [CONTRIBUTING.md](CONTRIBUTING.md).

## Publier

1. Ajouter une section `## [X.Y.Z]` dans `CHANGELOG.md`.
2. Pousser l'étiquette `vX.Y.Z`.

## Licence

[MIT](LICENSE). Copyright (c) 2026 Anarchy (@Anarchiejs).
