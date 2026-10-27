# Sonde de la plateforme Windows

Depuis la racine du worktree :

```powershell
& %USERPROFILE%\.dotnet\dotnet.exe run --project tools/Mixdeck.PlatformProbe -- --seconds 20
```

La sonde affiche la barre des tâches et le DPI, puis les changements de premier
plan et les processus lancés/arrêtés. Maintenir F13 vérifie le push-to-talk
(un appui et un relâchement malgré l'auto-répétition). Ctrl+Alt+M vérifie le
raccourci classique. La sonde ne modifie pas le démarrage automatique.

Les notifications de veille, reprise, affichage et thème sont aussi affichées.
Les événements sont reçus sur des threads d'arrière-plan ; les abonnés doivent
rendre la main rapidement et transférer les mises à jour visuelles au thread UI.

## Vérification dans l'environnement restreint

Les paquets nécessaires sont déjà présents dans le cache local. Aucune nouvelle
dépendance ni téléchargement n'est nécessaire :

```powershell
$env:RestoreSources = '%USERPROFILE%\.nuget\packages'
$env:NuGetAudit = 'false'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
& %USERPROFILE%\.dotnet\dotnet.exe build Mixdeck.slnx -nologo -v q -m:1 -nodeReuse:false
& %USERPROFILE%\.dotnet\dotnet.exe test tests/Mixdeck.Platform.Tests -nologo
& %USERPROFILE%\.dotnet\dotnet.exe run --project tools/Mixdeck.PlatformProbe -- --seconds 5
```

La désactivation des serveurs de compilation réutilisés évite les refus d'accès
aux sorties de compilation dus à un processus lancé dans un autre contexte.

## OPEN / BLOCKERS

- L'intégrateur doit ajouter `tests/Mixdeck.Platform.Tests/Mixdeck.Platform.Tests.csproj`
  et `tools/Mixdeck.PlatformProbe/Mixdeck.PlatformProbe.csproj` à `Mixdeck.slnx`.
  La solution est hors du périmètre de modification L3.
- Les tests AutoStart écrivent exclusivement dans une sous-clé unique de
  `HKCU\Software\Mixdeck.Tests`, supprimée à la fin de chaque test. L'environnement
  restreint refuse la création de cette clé : les six tests de registre doivent
  être relancés dans un contexte autorisant cette écriture. Ils ne sont ni ignorés
  ni remplacés par une simulation. Aucune écriture dans la vraie valeur Run.
- La sonde de cinq secondes valide le démarrage et l'arrêt des services, la
  géométrie/DPI et des notifications de processus réels. Les transitions clavier,
  le multi-écran, le premier plan accessible, la veille/reprise et les changements
  de thème nécessitent une vérification interactive complémentaire. La sonde
  n'injecte aucune touche et ne déclenche pas de veille.
