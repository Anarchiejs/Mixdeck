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
- Icône toujours visible près de l'horloge (une seule fois : si vous la rangez derrière la flèche ^, elle y reste), clic milieu pour couper le micro, info-bulle détaillée.
- Molette sur une ligne du panneau pour régler son volume.
- Volume par application dans le panneau et choix de la sortie d'une application (« Envoyer vers… », API de Windows non documentée : désactivé automatiquement si elle disparaît).
- Page Appareils : appareils des entrées et sorties physiques (micros de Windows proposés quand Voicemeeter n'en liste aucun), préréglages du micro, redémarrage du moteur audio, fenêtre de Voicemeeter cachée quand Mixdeck le lance.
- Atténuation automatique d'une entrée quand une autre a du signal (seuil, baisse, attaque, maintien, relâche réglables).
- Console refaite : les tranches passent à la ligne (plus de défilement horizontal), en-tête lisible (nom et type sur toute la largeur), Muet et Solo côte à côte, panoramique G/D sur une ligne, boutons d'envoi A1…B3 lisibles, égaliseur repliable avec la valeur à côté de chaque bande, icônes entières dans le sélecteur.
- La console n'affiche que les entrées et sorties reliées à un périphérique (plus les entrées et sorties virtuelles) ; le bouton + à côté de « Entrées » ou « Sorties » rajoute une tranche masquée, et « Masquer de la console » dans le menu d'une tranche la retire.
- Correction : les vumètres de la console restaient figés à −60 dB après avoir réduit puis restauré la fenêtre.
- La molette ne règle un fader de la console qu'après un clic dessus : en passant la souris, la page défile sans changer de gain.
- Interrupteur « M'écouter » dans le panneau et la page Appareils : envoie le micro vers A1 (le casque) pour s'entendre.
- Badge permanent « micro coupé » et son de confirmation (facultatifs).
- Raccourci « Appliquer un profil » et profil appliqué au démarrage.
- Journal des plantages (`logs\crash-*.txt`) et relance automatique unique ; l'entrée de démarrage avec Windows est retirée à la désinstallation.
- Tests d'interface FlaUI en CI ; signature Azure Trusted Signing facultative à la publication (`docs/SIGNATURE.md`).
- Prise en charge de Voicemeeter Standard et Potato (disposition détectée à la connexion ; démo `--demo=standard` / `--demo=potato`).
- Interface en anglais : langue de Windows par défaut, ou choix Français / English dans Réglages.
- Installation en un clic depuis les sources (`Installer Mixdeck.cmd`) : SDK .NET et Voicemeeter installés au besoin, paquet Velopack construit puis installé ou mis à jour ; options `-SkipTests` et `-OutputDir` dans `build/pack.ps1`.
- `MIXDECK_DATA_DIR` pour choisir le dossier des données ; arguments de développement `--flyout` et `--page=<nom>`.
- Pilotage de FL Studio par loopMIDI et un script FL (`fl/Mixdeck/device_Mixdeck.py`) : page « FL Studio » (état de la liaison et aide, pistes, volume, muet, sélection, effets de la piste, slots d'effets avec le nom du plugin ; liste des pistes mise à jour d'elle-même), choix rapide des effets du micro dans le panneau (une seule piste FL avec plugins allumée à la fois, par son interrupteur marche/arrêt), état de FL dans les profils, raccourcis « basculer un slot » et « couper une piste », FL simulé en mode démo, outil `tools/Mixdeck.FlProbe` ; `Installer Mixdeck.cmd` installe loopMIDI s'il manque, crée les deux ports, copie le script et règle *Options > MIDI* de FL (FL fermé).
- Muet synchronisé avec Discord (RPC local, application Discord de l'utilisateur, secret et jetons chiffrés par DPAPI).
- Choix de la sortie et de l'entrée Windows par défaut (panneau rapide et page Appareils), suivi des changements faits ailleurs.
- Un profil appliqué avant que FL soit connecté (règle au lancement de FL) applique son état FL dès la connexion.
- Nouveau logo « Vumètre M » : cinq barres de niveau suspendues qui dessinent un M, mot « mixdeck » dessiné sur mesure ; icônes de l'appli, de la zone de notification (bleue, rouge quand le micro est coupé) et de l'installateur régénérées par `design/logo/build.py`.
- Sortie de « M'écouter » réglable (page Appareils) ; la molette du panneau rapide ne règle un volume qu'après un clic sur le curseur.

- Intégration continue GitHub Actions : restauration, compilation Release et tests sur `windows-latest` et dépôt des résultats de tests.
- Publication Velopack sur les étiquettes `v*` (paquet `MixdeckApp`, installé à part des réglages, deltas à partir de la release précédente, notes tirées de ce journal).
- Script local `build/pack.ps1` et manifeste de l'outil `vpk` 1.2.161.
- Documentation, licence MIT et conventions d'éditeur.

### Corrigé

- Les règles déclenchées pendant que Voicemeeter est fermé sont appliquées dès qu'il revient.
- Un fondu de profil ne retarde plus la coupure du micro ni les règles suivantes ; un nouveau profil interrompt le fondu en cours.
- Les réglages restaurés depuis la copie de secours sont conservés même si le fichier principal ne peut pas être réécrit.
- Plus léger au repos : la console n'est créée qu'à la première ouverture et ses pages sont déchargées à la fermeture (≈ 115 Mo au démarrage au lieu de 208 Mo).
- Fermer la console n'arrête plus l'application ; une console réduite par Windows au lancement est remise à l'écran.
- Quitter Mixdeck pendant une atténuation automatique ne laisse plus la musique baissée ; après une reconnexion, la musique n'est plus baissée deux fois.
- Un fichier de réglages incomplet (sections absentes ou nulles) n'empêche plus le démarrage.
- Textes harmonisés au vouvoiement, en-têtes du routage sans doublon, boutons de la page Profils côte à côte, erreur affichée si une personnalisation n'est pas enregistrée.

[Non publié]: https://github.com/Anarchiejs/Mixdeck/commits/main
