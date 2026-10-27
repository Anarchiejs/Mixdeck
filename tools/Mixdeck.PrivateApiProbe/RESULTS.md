# Vérification L14 — 27 octobre 2026

Statut : implémentation et sonde compilées, tests propres au lot verts. La validation
globale et les deux essais dans la session utilisateur restent ouverts.

## Compilation et tests

Après les variables d'environnement de README.md (cache NuGet local, audit réseau
désactivé, serveurs de compilation non réutilisés), les commandes de la fiche ont été lancées :

```text
%USERPROFILE%\.dotnet\dotnet.exe build Mixdeck.slnx -nologo -v q
La génération a réussi. 0 Avertissement(s), 0 Erreur(s).

%USERPROFILE%\.dotnet\dotnet.exe test Mixdeck.slnx -nologo
Core :        25 réussis / 25
Voicemeeter : 22 réussis / 22
Platform :    86 réussis, 10 échecs / 96
Total :       133 réussis, 10 échecs / 143, 0 ignoré
```

Les options `--logger trx` et `--results-directory tools/Mixdeck.PrivateApiProbe/artifacts/tests`
ont été ajoutées à l'exécution finale pour conserver les détails. Les dix échecs sont
des `UnauthorizedAccessException` lors de `Registry.CurrentUser.CreateSubKey` dans
`HKCU\Software\Mixdeck.Tests\…` : 6 cas d'`AutoStartTests` et 4 de `TrayIconPromoterTests`.
Ces fichiers préexistants ne sont pas autorisés par L14 et n'ont pas été modifiés.
Il faut relancer la suite dans la session utilisateur ; elle n'est pas « tout vert » ici.

La dernière vérification, après le contrôle de sortie différente ajouté à la sonde,
confirme les mêmes résultats à 15:17 : build 0/0, 133/143 au global et 49/49 pour L14.
Journaux complets : `artifacts/build-final.txt`, `artifacts/tests-final.txt` et
`artifacts/tests-final/L14-final_*.trx`.

```text
dotnet test tests/Mixdeck.Platform.Tests/Mixdeck.Platform.Tests.csproj --no-build --no-restore -nologo
  --filter "FullyQualifiedName~AppAudioRouterTests|FullyQualifiedName~TrayNotifyTests"
49 réussis / 49, 0 échec, 0 ignoré.
```

Couverture : frontières de build/IID, emballage/décodage des identifiants, rejet des
formats inconnus et capture, défaut Windows, lecture des deux rôles, échec partiel,
restauration, HRESULT sans persistance, API absente, refus temporaire, chemins exacts,
identité des icônes, disposition x64 et préférences inconnues du tray.

Les premières tentatives ont rencontré NU1900/NU1301 (source Internet inaccessible)
et CS2012 (serveurs de compilation réutilisés avec des droits incompatibles).
La configuration temporaire décrite dans README.md a permis le build sans modifier
les contrats, les références de paquets ou les propriétés partagées du dépôt.

## Traces natives dans le bac à sable

Extraits réellement obtenus le 27/10/2026 vers 15:06–15:07, Windows 10.0.22631.0, x64.
Ces résultats de lecture/activation ne remplacent pas les essais audio et visuel de Claude.

```text
qi-negative
CoCreateInstance MMDeviceEnumerator IUnknown: HRESULT=0x00000000
QueryInterface IID volontairement absent={f4729118-647d-4efe-9b77-370c3f7c1d87}, HRESULT=0x80004002, pointeur=0x0
Aucune méthode privée appelée sur cette interface.

route-list
AudioPolicy Windows=10.0.22631.0, IID={ab3d4648-e242-459f-b02f-541c70306324}
RoGetActivationFactory: HRESULT=0x00000000
QueryInterface {ab3d4648-e242-459f-b02f-541c70306324}: HRESULT=0x00000000
Audio IsSupported=True
MMDeviceEnumerator IID={a95664d2-9614-4f35-a746-de8db63617e6}: HRESULT=0x00000000
EnumAudioEndpoints eRender ACTIVE: HRESULT=0x00000000
IMMDeviceCollection.GetCount: HRESULT=0x00000000
10 sorties énumérées, noms conviviaux lus avec HRESULT=0x00000000.
Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)
{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}

route-get 30896 (PowerShell de la sonde, sans session audio de test)
GetPersistedDefaultAudioEndpoint pid=30896 eRender Multimedia: HRESULT=0x80070057
Success=false, Multimedia=null, Console=null
Le refus n'est pas présenté comme un retour au défaut ; aucune écriture.

tray-promote %USERPROFILE%\AppData\Local\MixdeckApp\current\Mixdeck.exe on
Tray Windows=10.0.22631.0, CLSID={25dead04-1eac-4911-9e3a-ad0a4ab560fd}
CoCreateInstance TrayNotify IUnknown: HRESULT=0x80070005
Tray IsSupported=false ou opération refusée : HRESULT=0x80070005
PreferenceConfirmed=false
```

L'activation du serveur local de l'Explorateur est refusée : aucun appel de méthode
de tray n'a été possible. Aucun changement de sortie, de registre ou de visibilité
n'a été effectué pendant ces sondes ; aucun Explorateur n'a été redémarré.

## OPEN / BLOCKERS

- Les 10 tests HKCU exigent une session où leurs clés de test sont accessibles.
- Claude doit lancer `Run-UserProbes.ps1`, confirmer le déplacement audible et
  visuel, joindre les HRESULT et les restaurations. L'efficacité du tray Windows 11
  reste inconnue ; aucune méthode ou IID supplémentaire n'est supposé.
- Enregistrement DI et raccordement au promoteur existant : changements exacts dans
  README.md, à effectuer par Claude après validation, hors périmètre autorisé L14.
