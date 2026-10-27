# Sonde L14 : routage audio et zone de notification

Cette sonde utilise le même code que `AppAudioRouter` et `TrayNotifyInterop`.
Elle n'ajoute aucun paquet. Les sorties JSON vont sur stdout ; les HRESULT, IID,
valeurs natives et restaurations vont sur stderr. Codes : 0 = opérations et relectures
réussies, 1 = syntaxe incorrecte, 2 = API absente/opération refusée, 3 = restauration non confirmée.
Un code 0 ne prouve pas à lui seul le déplacement audible d'un flux ni la visibilité effective de l'icône.

## Commandes à lancer par Claude

Dans la session de bureau de **user**, sans élévation, avec Mixdeck installé déjà lancé :

```powershell
Set-Location '%USERPROFILE%\Documents\Mixdeck'
$env:DOTNET_ROOT = '%USERPROFILE%\.dotnet'
# Vérification hors ligne : uniquement les paquets déjà présents, sans audit réseau NuGet.
$env:NuGetAudit = 'false'
$env:RestoreSources = '%USERPROFILE%\.nuget\packages'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:UseSharedCompilation = 'false'
& %USERPROFILE%\.dotnet\dotnet.exe build Mixdeck.slnx -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build refusé.' }
& %USERPROFILE%\.dotnet\dotnet.exe test Mixdeck.slnx -nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests à corriger avant intégration.' }
& .\tools\Mixdeck.PrivateApiProbe\Run-UserProbes.ps1
```

Le script choisit l'unique sortie nommée « Voicemeeter AUX Input… ». Sinon passez
`-EndpointId '<identifiant exact de route-list>'`. Il conserve les traces dans
`tools/Mixdeck.PrivateApiProbe/artifacts/user-probes/session-*.txt`.
Il ne lance pas la version installée de Mixdeck et ne redémarre jamais l'Explorateur.

Séquence équivalente pour observer les essais individuellement :

```powershell
$probe = '.\tools\Mixdeck.PrivateApiProbe\bin\Debug\net10.0-windows\win-x64\Mixdeck.PrivateApiProbe.exe'
& $probe qi-negative
& $probe route-list
# Identifiant AUX observé dans ce PC le 27/10/2026 ; vérifiez-le dans route-list.
& $probe route-roundtrip '{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}'
& $probe tray-get '%USERPROFILE%\AppData\Local\MixdeckApp\current\Mixdeck.exe'
& $probe tray-roundtrip '%USERPROFILE%\AppData\Local\MixdeckApp\current\Mixdeck.exe'
```

`route-roundtrip` démarre un processus enfant caché de la sonde qui joue `chimes.wav`
en boucle. Il sauvegarde les deux rôles, route cet enfant, relit et attend 10 secondes
pour votre constat auditif. Son `finally` restaure chaque rôle à sa valeur initiale,
y compris s'ils différaient, vérifie, puis arrête uniquement l'enfant. Si la lecture
initiale échoue, il ne modifie aucune préférence. `tray-roundtrip` sauvegarde toutes
les icônes actives de l'exécutable exact, les affiche 8 secondes, les range 8 secondes,
puis restaure les préférences 0/1/2 initiales et les relit. Ctrl+C déclenche aussi la
restauration ; ne tuez pas la sonde et ne fermez pas brutalement son terminal.

La sonde refuse aussi une cible déjà utilisée par le lecteur (en résolvant la sortie
Windows par défaut de chaque rôle si nécessaire). Choisissez alors une autre sortie
avec `-EndpointId` pour que l'essai démontre réellement un changement.

Conservez le code de sortie, les traces « Restauration … confirmée=True » et vos
observations avant/pendant/après. Si le son ou l'icône ne bouge pas malgré S_OK,
signalez-le comme échec de l'essai réel. Si le code vaut 3, les valeurs originales
figurent dans la trace ; rétablissez-les dans les paramètres Windows et signalez le blocage.

## Commandes élémentaires

```text
route-list
route-get <pid>
route-set <pid> <endpointId|default>
tray-promote <chemin absolu de l'exécutable> on|off
```

Elles sont intentionnellement persistantes : `route-set <pid> default` remet ce PID
à la sortie par défaut pour eMultimedia et eConsole. Il ne touche pas eCommunications
et n'appelle jamais `ClearAllPersistedApplicationDefaultEndpoints`. `tray-promote`
ne modifie que les entrées actives correspondant au chemin complet ; `off` demande
la préférence 1 et `on` la préférence 2. Utilisez les roundtrips pour les essais
afin de restaurer aussi les préférences initiales non standard.

`route-get` distingue explicitement un échec (`Success=false`) du défaut Windows
(`Success=true`, deux valeurs nulles). Les rôles sont lus séparément : ERole est une
énumération, pas un ensemble de drapeaux. Un PID sans session audio ou inaccessible
peut être refusé par Windows ; ce refus n'est pas converti en préférence par défaut.

## Intégration laissée à Claude

- Après validation de l'essai audio : dans `ServiceRegistration`, ajouter
  `services.AddSingleton<IAppAudioRouter, Mixdeck.Platform.Audio.AudioPolicy.AppAudioRouter>();`.
  Les contrats sont synchrones : exécuter les appels depuis un thread de travail
  (`Task.Run` côté interface), puis repasser par `IUiDispatcher` pour l'affichage.
- Après validation visuelle du tray : raccorder `TrayNotifyInterop.TryPromoteNow`
  au service de promotion existant, hors thread UI, en respectant `TrayIconPromoted`
  et le choix de l'utilisateur. Un succès de QI/relecture seul ne valide pas ce raccordement.
- Si le nouvel Explorateur refuse l'IID D133CE13… ou si son ancienne liste d'icônes
  ne pilote plus le tray visible : conserver l'échec et la procédure Windows manuelle.
  Aucun IID Windows 11 supplémentaire n'a été établi. Aucun appel conjectural n'est tenté.

## Provenance des ABI

- [EarTrumpet, interfaces audio](https://github.com/File-New-Project/EarTrumpet/tree/f492b8fa6459d9c5b442ad07ea28d70e9db7f493/EarTrumpet/Interop/MMDeviceAPI)
  et [seuil de build](https://github.com/File-New-Project/EarTrumpet/blob/f492b8fa6459d9c5b442ad07ea28d70e9db7f493/EarTrumpet/Extensions/OperatingSystemExtensions.cs).
  Le seuil nommé « 21H2 » est **21390**, pas 19044. Les objets utilisent IInspectable
  (six slots de base), puis 19 méthodes avant Set/Get/ClearAll. Les HSTRING sont
  créés/lus/libérés explicitement pour .NET 10.
- [Chromium, tray](https://github.com/chromium/chromium/blob/main/chrome/browser/ui/views/status_icons/status_tray_state_changer_win.cc)
  et son en-tête voisin : IID, structure NOTIFYITEM et signature `UnregisterCallback(ULONG*)`.
  Le cookie est `uint`, même en x64. Les chaînes du callback sont copiées avant son retour.
- Les GUID, sources, limites de versions et conditions d'échec sont aussi documentés
  en tête des fichiers d'interop. Les résultats du bac à sable figurent dans [RESULTS.md](RESULTS.md).
