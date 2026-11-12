<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="design/logo/final/mixdeck-lockup-white.svg">
    <img src="design/logo/final/mixdeck-lockup.svg" alt="Mixdeck" width="420">
  </picture>
</p>

# Mixdeck

[English](README.md)

Mixdeck est une application Windows native (C# / .NET 10, WinUI 3, non empaquetée, x64) qui pilote [Voicemeeter](https://vb-audio.com/Voicemeeter/) depuis la barre des tâches. Elle s'adresse à qui mélange micro, appels, jeux, musique et FL Studio sur un PC Windows.

**État.** Préversion. La version d'assembly dans `Directory.Build.props` est **0.1.0**. Aucune publication GitHub Releases n'existe encore. L'application compile, démarre et pilote Voicemeeter. Tant que la [signature Azure Trusted Signing](docs/SIGNATURE.md) n'est pas configurée, l'installateur n'est **pas signé** : SmartScreen avertit au premier lancement.

Mixdeck n'est pas affilié à VB-Audio ni à Image-Line.

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

- Windows 10 version 19041 ou plus récente, ou Windows 11. x64 seulement. La plateforme minimale du projet d'application est `10.0.19041.0`.
- Voicemeeter Standard, Banana ou Potato, déjà installé. Mixdeck charge `VoicemeeterRemote64.dll` depuis cette installation. La DLL n'est pas livrée avec Mixdeck.
- Pour compiler : le SDK .NET 10. `global.json` fixe `10.0.403` avec un report `latestFeature`.

Facultatif :

- FL Studio et [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html) pour la page FL Studio.

## Confidentialité

Mixdeck n'a pas de compte de télémétrie et n'envoie ni journaux ni rapports de plantage.

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

À la racine du dépôt :

```powershell
dotnet restore Mixdeck.slnx
dotnet build Mixdeck.slnx -c Release -nologo
dotnet test Mixdeck.slnx -c Release -nologo
```

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

| Chemin | Rôle |
| --- | --- |
| `%LOCALAPPDATA%\Mixdeck\logs` | Journaux et plantages |
| `%LOCALAPPDATA%\Mixdeck\settings.json` | Réglages (`settings.json.bak` en secours) |
| `%LOCALAPPDATA%\Mixdeck\profiles` | Profils |

## Architecture

| Projet | Rôle |
| --- | --- |
| `src/Mixdeck.Core` | Modèle, contrats, réglages. `net10.0`, sans dépendance Windows. |
| `src/Mixdeck.Voicemeeter` | Client Voicemeeter. x64. |
| `src/Mixdeck.Platform` | Services Win32. x64. |
| `src/Mixdeck.FlStudio` | Client FL Studio. |
| `src/Mixdeck.App` | Interface WinUI 3 non empaquetée. |

Mentions tierces : [NOTICE](NOTICE). Sécurité : [SECURITY.md](SECURITY.md). Contribution : [CONTRIBUTING.md](CONTRIBUTING.md).

## Publier

1. Ajouter une section `## [X.Y.Z]` dans `CHANGELOG.md`.
2. Pousser l'étiquette `vX.Y.Z`.

## Licence

[MIT](LICENSE). Copyright (c) 2026 Anarchy (@Anarchiejs).
