# Journal des modifications

Toutes les modifications notables de Mixdeck sont consignées dans ce fichier.

Le format s'inspire de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),
et ce projet adhère au [versionnage sémantique](https://semver.org/lang/fr/).

## [Non publié]

### Ajouté

- Client Voicemeeter Banana : appels sur un thread dédié, file de commandes fusionnées, relecture sur changement, niveaux à 60 Hz seulement quand un vumètre est visible, reconnexion automatique, moteur simulé (`--demo`).
- Cœur : réglages JSON à écriture atomique avec copie de secours, profils avec fondu des gains, moteur de règles par application, balistique des vumètres.
- Services Windows : raccourcis globaux et push-to-talk, détection des applications lancées et au premier plan, démarrage avec Windows, position de la barre des tâches, instance unique.
- Interface WinUI 3 : panneau rapide ancré à l'horloge, console (Console, Routage, Profils, Règles, Réglages, À propos), icône qui passe au rouge quand le micro est coupé, OSD.
- `MIXDECK_DATA_DIR` pour choisir le dossier des données ; arguments de développement `--flyout` et `--page=<nom>`.

- Intégration continue GitHub Actions : restauration, compilation Release et tests sur `windows-latest` et dépôt des résultats de tests.
- Publication Velopack sur les étiquettes `v*` (paquet `MixdeckApp`, installé à part des réglages, deltas à partir de la release précédente, notes tirées de ce journal).
- Script local `build/pack.ps1` et manifeste de l'outil `vpk` 1.2.161.
- Documentation, licence MIT et conventions d'éditeur.

### Corrigé

- Les règles déclenchées pendant que Voicemeeter est fermé sont appliquées dès qu'il revient.
- Un fondu de profil ne retarde plus la coupure du micro ni les règles suivantes ; un nouveau profil interrompt le fondu en cours.
- Les réglages restaurés depuis la copie de secours sont conservés même si le fichier principal ne peut pas être réécrit.
- Textes harmonisés au vouvoiement, en-têtes du routage sans doublon, boutons de la page Profils côte à côte, erreur affichée si une personnalisation n'est pas enregistrée.

[Non publié]: https://github.com/Anarchiejs/Mixdeck/commits/main
