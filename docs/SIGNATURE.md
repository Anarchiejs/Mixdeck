# Sign Mixdeck (remove the SmartScreen warning)

Without a signature, Windows shows "Windows protected your PC" the first time the installer runs.
The release workflow (`.github/workflows/release.yml`) can sign with **Azure Trusted Signing** (about 10 USD per month,
Basic offer). Until it is configured, the workflow produces an unsigned package.

## One-time setup (maintainer's Azure account)

1. In the Azure portal, create a **Trusted Signing Account** (a nearby region, Basic offer).
   Note its **endpoint** (for example `https://weu.codesigning.azure.net/`) and its **name**.
2. Complete **identity validation** ("Individual" for a solo developer). It can take a few days.
3. Create a **Public Trust** certificate profile and note its name.
4. Create an **app registration** (Microsoft Entra ID > App registrations) with a client secret, and
   grant it the **Trusted Signing Certificate Profile Signer** role on the signing account.
5. In the GitHub repository (Settings > Secrets and variables > Actions):
   - secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`;
   - variables: `AZURE_SIGNING` = `true`, `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT`, `AZURE_SIGNING_PROFILE`.

Do not commit those values. On the next `v*` tag, `vpk pack --azureTrustedSignFile` signs `Mixdeck.exe`, the DLLs, and the installer.

## Check

Right-click `Mixdeck-win-Setup.exe` > Properties > **Digital Signatures**: the validated name must appear.
SmartScreen can still warn for a few days while the certificate builds reputation.

## macOS (Developer ID and notarization)

The macOS package is unsigned unless the release environment has Apple credentials. `build/notarize.sh` is called at the end of `build/pack.sh`. With no identity it prints that the package is unsigned and exits 0. It does not invent a signature.

Set these environment variables on the Mac that packages the app:

- `APPLE_SIGNING_IDENTITY`: the Developer ID Application identity, for example `Developer ID Application: Name (TEAMID)`.
- `APPLE_NOTARY_KEY`: path to an App Store Connect API key (`.p8`).
- `APPLE_NOTARY_KEY_ID`: the key id.
- `APPLE_NOTARY_ISSUER`: the issuer id.

`codesign` then signs `artifacts/Mixdeck.app`. When the three notary variables are set, `notarytool` submits a zip and `stapler` attaches the ticket. Do not commit the `.p8` key or the identity. This repository has no Apple certificate, so a build from here stays unsigned.
