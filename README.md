<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="design/logo/final/mixdeck-lockup-white.svg">
    <img src="design/logo/final/mixdeck-lockup.svg" alt="Mixdeck" width="420">
  </picture>
</p>

# Mixdeck

Mixdeck est une application Windows native (C# / .NET 10, WinUI 3, non empaquetée, x64) qui pilote [Voicemeeter Banana](https://vb-audio.com/Voicemeeter/) depuis la barre des tâches.

Le projet est en cours de construction vers la v1.0 : l'appli compile, démarre et pilote Voicemeeter ; les pages de la console sont en cours de finition.

## Fonctionnalités

- Icône toujours visible près de l'horloge : clic gauche = panneau, clic milieu = couper le micro, info-bulle avec l'état du micro, le volume A1 et le profil.
- Panneau rapide ancré à la barre des tâches, molette = volume de la ligne survolée.
- Volume par application (Discord, jeu, navigateur…) et « Envoyer vers… » pour choisir la sortie d'une application.
- Page Appareils : micro, casque et enceintes de Voicemeeter, préréglages du micro (porte de bruit, compresseur, voix plus claire), redémarrage du moteur audio.
- Atténuation automatique : la musique baisse quand quelqu'un parle sur Discord.
- Badge permanent et son de confirmation quand le micro est coupé.
- « M'écouter » (panneau et page Appareils) : le micro est envoyé dans A1, généralement le casque, pour s'entendre parler.
- Console de mixage : tranches d'entrées et de sorties qui passent à la ligne, seules les tranches reliées à un périphérique sont affichées (bouton + pour les autres), égaliseur repliable ; la molette ne règle un fader qu'après un clic dessus.
- Matrice de routage.
- Profils rappelés avec un fondu.
- Règles par application.
- Raccourcis globaux, push-to-talk, raccourci par profil et profil appliqué au démarrage.
- Pilotage de FL Studio (page « FL Studio ») : choisir la piste du mixeur, régler volume et muet, activer ou couper les effets d'une piste et chaque slot d'effet (avec le nom du plugin) ; la liste des pistes suit d'elle-même les changements faits dans FL ; un profil peut inclure l'état de FL, un raccourci peut basculer un slot (ex. « voix robot »). Le panneau rapide propose les pistes FL qui portent des plugins : en allumer une (l'interrupteur vert de la piste dans le mixeur de FL) coupe les autres, rappuyer dessus la coupe.
- Muet synchronisé avec Discord, dans les deux sens (page Réglages, voir « Discord » plus bas).
- Sortie et entrée Windows par défaut choisies depuis le panneau (sortie) ou la page Appareils (sortie et entrée).
- Journal des plantages et relance automatique unique.
- Affichage à l'écran (OSD).
- Installation et mises à jour par [Velopack](https://velopack.io/), avec paquets delta publiés sur GitHub Releases.

## Captures d'écran

Les captures se déposent dans [`docs/images/`](docs/images/). Aucune capture n'est versionnée pour l'instant.

## Prérequis

- Windows 10 version 19041 ou plus récente, ou Windows 11. La plateforme minimale du projet d'application est `10.0.19041.0`.
- Voicemeeter Banana installé. Mixdeck s'appuie sur la bibliothèque distante de VB-Audio (`VoicemeeterRemote64.dll`). Cette DLL n'est pas redistribuée avec Mixdeck.
- Pour compiler : le SDK .NET 10. `global.json` fixe la version `10.0.403` avec un report `latestFeature`.

## Installation

### En un clic, depuis les sources

Double-cliquer sur **`Installer Mixdeck.cmd`** à la racine du dépôt. Aucun droit administrateur n'est demandé. Le script `build/install.ps1` :

1. cherche un SDK .NET 10 (10.0.403 ou plus récent) ; s'il n'y en a pas, il l'installe pour l'utilisateur dans `%LOCALAPPDATA%\Microsoft\dotnet` avec le script officiel `dot.net/v1/dotnet-install.ps1` ;
2. vérifie que Voicemeeter est installé ; sinon, il propose de l'installer avec `winget` (Voicemeeter Banana) ;
3. compile une version Release autonome et l'empaquette avec Velopack, sans les tests ;
4. installe Mixdeck comme une vraie version dans `%LOCALAPPDATA%\MixdeckApp`, avec des raccourcis dans le menu Démarrer et sur le Bureau et une entrée dans Paramètres > Applications. Si Mixdeck est déjà installé, il le ferme et applique une mise à jour Velopack. Les réglages sont conservés. Mixdeck est ensuite lancé.

La version installée est `<version de Directory.Build.props>-local.<date>` : chaque nouvel essai est plus récent que le précédent, et la première version publiée sur GitHub (`0.1.0`) le remplacera par la mise à jour automatique. Pour mettre à jour, il suffit de relancer le script après un `git pull`.

Options : `-RunTests` lance aussi les tests avant d'installer, et `-NoPrompt` ne pose aucune question. En cas d'échec, le journal de Velopack est dans `%LOCALAPPDATA%\velopack\velopack.log`.

### Depuis une publication

Les versions publiées sont attachées aux [publications GitHub](https://github.com/Anarchiejs/Mixdeck/releases) du dépôt `Anarchiejs/Mixdeck`, créées par le workflow `.github/workflows/release.yml` lorsqu'une étiquette `v*` est poussée.

Aucune publication n'existe tant qu'une étiquette n'a pas été créée. L'installateur est celui produit par Velopack (identifiant de paquet `MixdeckApp`, installé dans `%LOCALAPPDATA%\MixdeckApp`). Les réglages et profils vivent à part, dans `%LOCALAPPDATA%\Mixdeck` : une désinstallation ne les efface pas. Les mises à jour suivantes sont des paquets delta calculés à partir de la publication précédente.

## Compiler depuis les sources

Cloner le dépôt, puis, à la racine :

```powershell
dotnet restore Mixdeck.slnx
dotnet build Mixdeck.slnx -c Release -nologo
dotnet test Mixdeck.slnx -c Release -nologo
```

Les tests (`tests/Mixdeck.Core.Tests`, `tests/Mixdeck.Voicemeeter.Tests`, `tests/Mixdeck.Platform.Tests`) ne lancent pas Voicemeeter : le client est testé contre une API simulée.

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

L'argument `--demo` remplace Voicemeeter par un moteur simulé (libellés, gains, vumètres animés) : utile pour essayer Mixdeck ou le tester sur une machine sans Voicemeeter.

```powershell
dotnet run --project src/Mixdeck.App -- --demo
```

Autres arguments : `--minimized` (démarrage avec Windows, rien n'est affiché) et `--gallery` (galerie des contrôles, pour le développement).

Deux outils en console aident au diagnostic : `tools/Mixdeck.Probe` (état de Voicemeeter et vumètres en texte, `--sim` pour la simulation) et `tools/Mixdeck.PlatformProbe` (premier plan, processus, raccourcis, barre des tâches).
## FL Studio

Mixdeck parle à FL Studio par deux ports MIDI virtuels et un script (« device script ») qui tourne dans FL. FL Studio n'a pas d'autre interface externe.

`Installer Mixdeck.cmd` fait tout le réglage :
- installe [loopMIDI](https://www.tobias-erichsen.de/software/loopmidi.html) s'il manque (winget ; Windows peut demander l'accord administrateur), le lance avec Windows et y crée les ports `Mixdeck to FL` et `FL to Mixdeck` ;
- copie [`fl/Mixdeck/device_Mixdeck.py`](fl/Mixdeck/device_Mixdeck.py) dans `Documents\Image-Line\FL Studio\Settings\Hardware\Mixdeck\` ;
- écrit les réglages MIDI de FL (*Options > MIDI*) : entrée `Mixdeck to FL` activée, *Controller type* **Mixdeck**, port 10 (ou le suivant s'il est pris) ; sortie `FL to Mixdeck` sur le même port ; aucun port sur la sortie `Mixdeck to FL`. FL réécrit ces réglages en quittant : l'installateur attend qu'il soit fermé. Il faut avoir lancé FL au moins une fois.

À la main, c'est la même chose : deux ports loopMIDI à ces noms, le script dans le dossier ci-dessus, puis ces réglages dans *Options > MIDI*. Sans port sur la sortie `Mixdeck to FL` : sinon les réponses du script reviennent dans sa propre entrée, et FL l'y remet parfois quand on recharge le script.

Ensuite, la page « FL Studio » de Mixdeck passe à « Connecté » et liste les pistes utilisées (Master, pistes renommées ou avec effets).

Limites : FL doit être ouvert avec le script actif ; un plugin s'active ou se coupe dans son slot mais ne se charge pas depuis Mixdeck. Pour un profil au lancement de FL : enregistrez un profil pendant que FL est connecté (il garde l'état des pistes), puis créez une règle « FL64.exe démarre → ce profil » dans la page Règles ; l'état FL est appliqué dès que le script répond. Après une mise à jour du script, FL garde l'ancien en mémoire : passez le *Controller type* à « (generic controller) » puis revenez à « Mixdeck ».

Diagnostic en console : `tools/Mixdeck.FlProbe` (`list`, `ping`, `get <piste>`, `mute`, `vol`, `slot`, `watch`…).

## Discord

Le muet du micro de Mixdeck et celui de Discord restent identiques : couper d'un côté coupe l'autre. Discord n'autorise
ce pilotage qu'aux applications Discord dont on est propriétaire ou testeur, d'où une étape unique :

1. Sur [discord.com/developers/applications](https://discord.com/developers/applications), créez une application (« New Application »).
2. Dans *OAuth2*, copiez le *Client ID* et le *Client Secret*, et ajoutez `http://localhost` dans *Redirects*.
3. Dans Mixdeck, *Réglages > Discord* : activez la synchronisation, collez l'ID et le secret, cliquez « Autoriser dans Discord » et acceptez dans Discord.

Le secret et les jetons sont chiffrés pour votre session Windows (DPAPI) dans `discord.bin`, jamais dans `settings.json` ni les journaux.
Quand Discord s'ouvre, l'état de Mixdeck fait foi.

## Journaux et fichiers

`MixdeckPaths` place la racine des fichiers dans `%LOCALAPPDATA%\Mixdeck`. La variable d'environnement `MIXDECK_DATA_DIR` permet d'utiliser un autre dossier (tests, recette). Le bouton « Ouvrir le dossier des journaux » des réglages y mène directement.

| Chemin | Rôle |
| --- | --- |
| `%LOCALAPPDATA%\Mixdeck\logs` | Journaux |
| `%LOCALAPPDATA%\Mixdeck\settings.json` | Réglages (`settings.json.bak` en secours) |
| `%LOCALAPPDATA%\Mixdeck\profiles` | Profils |

## Architecture

Cinq projets, solution `Mixdeck.slnx` :

| Projet | Rôle |
| --- | --- |
| `src/Mixdeck.Core` | Modèle, contrats et réglages. `net10.0`, sans dépendance Windows. |
| `src/Mixdeck.Voicemeeter` | Client Voicemeeter. `net10.0-windows`, x64. |
| `src/Mixdeck.Platform` | Services Win32 (raccourcis, processus, premier plan, démarrage, barre des tâches). x64. |
| `src/Mixdeck.FlStudio` | Client FL Studio : protocole SysEx (`FlProtocol`, en miroir de `fl/Mixdeck/device_Mixdeck.py`), ports MIDI Windows, FL simulé pour la démo. |
| `src/Mixdeck.App` | Interface WinUI 3 non empaquetée. Windows App SDK embarqué. Nom d'assembly `Mixdeck`. |

L'interface parle à Voicemeeter par `IMixer` et à FL Studio par `IDaw`. Une file de commandes est appliquée sur un thread dédié ; l'état relu devient la source de vérité affichée.

## Publier une version

1. Ajouter dans `CHANGELOG.md` une section `## [X.Y.Z]` décrivant la version.
2. Pousser l'étiquette `vX.Y.Z`.

Le workflow de publication compile en autonome (`win-x64`), restaure `vpk`, télécharge la release GitHub précédente pour les deltas, empaquette, puis envoie le résultat sur GitHub Releases avec les notes de cette section. Le jeton est `secrets.GITHUB_TOKEN` (permission `contents: write`). Aucun secret n'est écrit dans le dépôt.

## Licence

[MIT](LICENSE). Copyright (c) 2026 Les contributeurs de Mixdeck.
