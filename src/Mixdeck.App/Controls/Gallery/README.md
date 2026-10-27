# Galerie L5

Fusionner après les ressources WinUI et le thème Mixdeck dans `App.xaml` :

```xml
<ResourceDictionary Source="ms-appx:///Controls/Controls.xaml" />
```

L'intégrateur peut conserver une instance de `ControlsGalleryWindow`, puis appeler
`Activate()` lorsque le shell reçoit `--gallery`. Le shell est hors du périmètre L5.

Pour lancer la galerie indépendamment du shell, après une compilation normale :

```powershell
& %USERPROFILE%\.dotnet\dotnet.exe build src/Mixdeck.App/Mixdeck.App.csproj -nologo -v q -p:BuildProjectReferences=false -p:StartupObject=Mixdeck.App.Controls.Gallery.GalleryLauncher '-p:DefineConstants=MIXDECK_GALLERY%3BDISABLE_XAML_GENERATED_MAIN'
& .\src\Mixdeck.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Mixdeck.exe
```

Le lanceur alternatif est compilé uniquement avec `MIXDECK_GALLERY`. Son option
`--smoke` ferme la galerie après trois secondes. `MIXDECK_GALLERY_LOG` peut désigner
un fichier de diagnostic pour les exceptions gérées. Recompiler normalement pour
restaurer le point d'entrée du shell.

Recette visuelle : basculer clair/sombre, activer le contraste élevé Windows,
vérifier les états coché/survol/pression/focus/désactivé, changer de profil au clic
et avec Gauche/Droite/Début/Fin/Entrée/Espace, vérifier qu'un refus du ViewModel
laisse la sélection inchangée, renommer le canal (double-clic/F2, Entrée/Échap),
tester le fader au clavier et à la molette avec/sans Maj, puis double-cliquer pour
revenir à 0 dB. Déplacer la fenêtre entre écrans de DPI différents et vérifier les
niveaux avec Narrateur. Fermer/réouvrir la galerie pour vérifier la libération des
CanvasControl et de la minuterie.

Les éléments de `ProfilePillList` restent des objets libres ; seuls `Name`, `Icon`,
`Color` et `IsActive` sont utilisés par les liaisons. `ItemInvoked` demande un
changement ; seul `SelectedItem` fixe la sélection affichée.
