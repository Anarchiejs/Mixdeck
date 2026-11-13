# Contribuer

Mixdeck tourne sous Windows et macOS. L'application WinUI et les services Win32 restent réservés à Windows. Mainteneur : Anarchy ([@Anarchiejs](https://github.com/Anarchiejs)).

## Compiler

- Version du SDK .NET dans `global.json` (`10.0.403`, report `latestFeature`). `dotnet` dans le `PATH` doit être un SDK.
- Windows : Windows 10 version 19041 ou plus récent, ou Windows 11. Un SDK par utilisateur se place dans `%LOCALAPPDATA%\Microsoft\dotnet`. Voicemeeter est nécessaire pour exercer le vrai client. Les tests unitaires utilisent l'API simulée et n'en ont pas besoin.
- macOS : tester `Mixdeck.Portable.slnx`. La solution Windows ne se restaure pas sur un Mac, parce qu'il lui faut le pack de ciblage Windows.
- Il n'y a pas de build ARM64. `VoicemeeterRemote64.dll` est en x64.

```powershell
dotnet test Mixdeck.slnx -c Release -nologo
```

```bash
dotnet test Mixdeck.Portable.slnx -c Release -nologo
```

Les tests d'interface sont facultatifs et ne tournent pas dans GitHub Actions :

```powershell
$env:MIXDECK_UI_TESTS = '1'
dotnet test tests/Mixdeck.UiTests -c Release
```

## Règles

- Ne pas commiter de secrets, de fichiers `.env`, de jetons, de certificats, ni de chemins absolus du profil Windows.
- Ne pas commiter les dossiers locaux d'éditeur ou d'outil.
- Les textes visibles vont dans `src/Mixdeck.App/Strings/en-US/Resources.resw` et dans `fr-FR/Resources.resw`. Le test de localisation échoue si une clé n'existe que dans une langue.
- `Directory.Packages.props` est le seul endroit où les versions de paquets changent. Garder `FluentAssertions` sur la ligne 7.x Apache-2.0. La 8.x est sous licence commerciale Xceed.
- La DLL distante de Voicemeeter n'est pas redistribuée.
- Les fichiers `packages.lock.json` sont versionnés pour les projets qui se restaurent ici. La restauration Windows les produit sur Windows.

## Publications

1. Ajouter `## [X.Y.Z]` dans `CHANGELOG.md`, avec les notes qui doivent apparaître sur GitHub Releases.
2. Étiqueter `vX.Y.Z` et pousser l'étiquette. `.github/workflows/release.yml` empaquette la version Windows avec Velopack, puis ajoute le paquet macOS à la même publication. Les tests passent avant l'empaquetage.

La signature est facultative et décrite dans `docs/SIGNATURE.md`.
