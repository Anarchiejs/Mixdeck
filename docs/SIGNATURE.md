# Signer Mixdeck (supprimer l'avertissement SmartScreen)

Sans signature, Windows affiche « Windows a protégé votre ordinateur » au premier lancement de l'installateur.
La publication (`.github/workflows/release.yml`) sait signer avec **Azure Trusted Signing** (environ 10 $ par mois,
offre Basic). Tant que rien n'est configuré, elle produit un paquet non signé, comme avant.

## Ce qu'il faut faire une fois (compte Azure de l'auteur)

1. Dans le portail Azure, créer une ressource **Trusted Signing Account** (région proche, offre Basic).
   Noter son **point de terminaison** (ex. `https://weu.codesigning.azure.net/`) et son **nom**.
2. Faire la **validation d'identité** (Identity validation, « Individual » pour un développeur seul). Elle peut prendre
   quelques jours.
3. Créer un **profil de certificat** « Public Trust » et noter son nom.
4. Créer une **inscription d'application** (Microsoft Entra ID > Inscriptions d'applications) avec un secret client, et
   lui donner le rôle **Trusted Signing Certificate Profile Signer** sur le compte de signature.
5. Dans le dépôt GitHub (Settings > Secrets and variables > Actions) :
   - secrets : `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` ;
   - variables : `AZURE_SIGNING` = `true`, `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT`, `AZURE_SIGNING_PROFILE`.

À la prochaine étiquette `v*`, `vpk pack --azureTrustedSignFile` signe `Mixdeck.exe`, les DLL et l'installateur.

## Vérifier

Clic droit sur `Mixdeck-win-Setup.exe` > Propriétés > **Signatures numériques** : le nom validé doit apparaître.
SmartScreen peut encore prévenir pendant quelques jours, le temps que la réputation du certificat s'établisse.
