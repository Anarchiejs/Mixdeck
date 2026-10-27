# Mixdeck

Mixdeck est une application Windows native (C# / .NET 10, WinUI 3, non empaquetée, x64) qui pilote [Voicemeeter Banana](https://vb-audio.com/Voicemeeter/) depuis la barre des tâches.

Le dépôt est en cours de construction. Ce document décrit le produit visé et la façon de le compiler. Il ne décrit que ce que le code ou le plan du dépôt établit.

## Fonctionnalités

- Panneau rapide ancré à la barre des tâches.
- Console de mixage.
- Matrice de routage.
- Profils rappelés avec un fondu.
- Règles par application.
- Raccourcis globaux et push-to-talk.
- Affichage à l'écran (OSD).
- Installation et mises à jour par [Velopack](https://velopack.io/), avec paquets delta publiés sur GitHub Releases.

## Captures d'écran

Les captures se déposent dans [`docs/images/`](docs/images/). Aucune capture n'est versionnée pour l'instant.

## Prérequis

- Windows 10 version 19041 ou plus récente, ou Windows 11. La plateforme minimale du projet d'application est `10.0.19041.0`.
- Voicemeeter Banana installé. Mixdeck s'appuie sur la bibliothèque distante de VB-Audio (`VoicemeeterRemote64.dll`). Cette DLL n'est pas redistribuée avec Mixdeck.
- Pour compiler : le SDK .NET 10. `global.json` fixe la version `10.0.403` avec un report `latestFeature`.

## Installation

Les versions publiées sont attachées aux [publications GitHub](https://github.com/Anarchiejs/Mixdeck/releases) du dépôt `Anarchiejs/Mixdeck`, créées par le workflow `.github/workflows/release.yml` lorsqu'une étiquette `v*` est poussée.

Aucune publication n'existe tant qu'une étiquette n'a pas été créée. L'installateur est celui produit par Velopack (identifiant de paquet `Mixdeck`). Les mises à jour suivantes sont des paquets delta calculés à partir de la publication précédente.

## Compiler depuis les sources

Cloner le dépôt, puis, à la racine :

```powershell
dotnet restore Mixdeck.slnx
dotnet build Mixdeck.slnx -c Release -nologo
dotnet test Mixdeck.slnx -c Release -nologo
```

Les tests (`tests/Mixdeck.Core.Tests`, `tests/Mixdeck.Voicemeeter.Tests`) ne lancent pas Voicemeeter.

Si la commande `dotnet` du `PATH` n'apporte pas de SDK, utiliser le SDK installé pour l'utilisateur. Sur la machine de développement du projet, c'est `%USERPROFILE%\.dotnet\dotnet.exe`. Le script d'empaquetage le cherche à cet emplacement avant le `PATH`.

Empaquetage local (tests, publication autonome `win-x64`, puis `vpk pack` dans `artifacts/`) :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack.ps1 -Version 0.1.0
```

La même séquence sans l'étape Velopack :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/pack.ps1 -Version 0.1.0 -SkipPack
```

`vpk pack` exige l'icône `src/Mixdeck.App/Assets/Mixdeck.ico`. Les dossiers `artifacts/` et `publish/` sont déjà ignorés par le `.gitignore` du dépôt.

L'outil `vpk` est épinglé dans `.config/dotnet-tools.json` à la version `1.2.161`, la même que le paquet NuGet `Velopack`.

## Mode démonstration

L'argument `--demo` est le mode sans Voicemeeter : un moteur simulé. Le plan de lancement du dépôt le prévoit ainsi :

```text
src\Mixdeck.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Mixdeck.exe --demo
```

Le point d'entrée actuel (`src/Mixdeck.App/Program.cs`) reçoit les arguments mais ne les interprète pas encore.

## Journaux et fichiers

`MixdeckPaths` place la racine des fichiers dans `%LOCALAPPDATA%\Mixdeck`.

| Chemin | Rôle |
| --- | --- |
| `%LOCALAPPDATA%\Mixdeck\logs` | Journaux |
| `%LOCALAPPDATA%\Mixdeck\settings.json` | Réglages (`settings.json.bak` en secours) |
| `%LOCALAPPDATA%\Mixdeck\profiles` | Profils |

## Architecture

Quatre projets, solution `Mixdeck.slnx` :

| Projet | Rôle |
| --- | --- |
| `src/Mixdeck.Core` | Modèle, contrats et réglages. `net10.0`, sans dépendance Windows. |
| `src/Mixdeck.Voicemeeter` | Client Voicemeeter. `net10.0-windows`, x64. |
| `src/Mixdeck.Platform` | Services Win32 (raccourcis, processus, premier plan, démarrage, barre des tâches). x64. |
| `src/Mixdeck.App` | Interface WinUI 3 non empaquetée. Windows App SDK embarqué. Nom d'assembly `Mixdeck`. |

L'interface parle à Voicemeeter par `IMixer`. Une file de commandes est appliquée sur un thread dédié ; l'état relu devient la source de vérité affichée.

## Publier une version

1. Ajouter dans `CHANGELOG.md` une section `## [X.Y.Z]` décrivant la version.
2. Pousser l'étiquette `vX.Y.Z`.

Le workflow de publication compile en autonome (`win-x64`), restaure `vpk`, télécharge la release GitHub précédente pour les deltas, empaquette, puis envoie le résultat sur GitHub Releases avec les notes de cette section. Le jeton est `secrets.GITHUB_TOKEN` (permission `contents: write`). Aucun secret n'est écrit dans le dépôt.

## Licence

[MIT](LICENSE). Copyright (c) 2026 Les contributeurs de Mixdeck.
