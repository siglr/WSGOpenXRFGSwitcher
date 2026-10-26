# Publishing

## Website preparation

The product page is `web/WSG-OpenXR-FG-Switcher.html`, with `openxr.css`,
`openxr.js` and a labelled preview screenshot. It follows the existing WSG tools
site. The shared tools index, stylesheet and cache policy are maintained in
`C:\WSG Stream Feeder\web`; its index now includes this tool.

Run `./tools/prepare-web.ps1` after packaging to prepare `dist/website`. Use
`-SharedWeb <path>` if the Stream Feeder website source is elsewhere. This verifies
the latest package, refreshes asset cache tags and copies its exact ZIP and signed
metadata to the fixed website names. Repeated runs reuse the same folder.
See `web/UPLOAD-INSTRUCTIONS.txt` for the upload order and files to preserve.
No script publishes anything or changes the app's production update safeguards.

The page currently labels the download as a development preview and states that
headset testing is pending. Update that text only when the release criteria below
are met. Do not imply that the website checksum display verifies the RSA signature.

## Application release

Public source: https://github.com/siglr/WSGOpenXRFGSwitcher.
The first public preview is v0.1.0, with automatic app updates enabled. Headset
testing is pending; do not publish it as headset-tested. Ordinary source builds
continue to disable automatic app updates.

The product's RSA-3072 private key is DPAPI-protected for this Windows user at
`%LOCALAPPDATA%\WSG OpenXR FG Switcher\signing\release-key.dpapi`.
It is never committed or included in a package. Keep a protected Windows profile
backup: the encrypted file alone is not portable recovery. Do not regenerate the
key after publication; installed clients trust the existing public identity.

For subsequent previews, increment `Version` in `Directory.Build.props`, run
`./package.ps1 -OfficialRelease -SigningKey <release-key.dpapi>`, verify and scan
the output, then run `./tools/prepare-web.ps1`. Commit source and release evidence,
tag the exact commit, and create a GitHub release with the ZIP, signed version
JSON and checksum. Publish the same ZIP on GitHub and WeSimGlide. Upload the ZIP
first and the version JSON last. Never replace an already-published version.

Distribution uses a framework-dependent Windows x64 package, like WSG Stream
Feeder. Require .NET 10 Desktop Runtime x64 and link the official Microsoft
download on the download page. The app, layer helper, and updater all use the
shared .NET 10 runtime; only the application's four DLLs are shipped. Package
checks reject bundled runtime DLLs and missing/incorrect runtime requirements.
Before publishing an automatic migration from an older self-contained channel,
verify the shared runtime prerequisite; the development channel remains disabled.

For an official channel:

1. For a new product identity only, use tools/initialize-release-signing.ps1 to
   create and protect a dedicated product RSA-3072 key outside the repository.
   Replace assets/trust-public.pem and src/Updates/ReleasePublicKey.cs with its
   public identity. Re-sign assets/catalog.reviewed.json using build-catalog.ps1.
   Never reuse Stream Feeder's private key.
2. Review every provider asset's official release, SHA-256, file inventory,
   configuration schema, and adapter behavior. Keep previous supported entries.
   Native DLSS availability is release-specific. Do not automatically trust
   GitHub's latest tag as a compatible update.
3. Complete the hardware acceptance matrix in VALIDATION.md. Record versions,
   headset/runtime, game, GPU/driver, and enabled VectorXR modules. Keep untested
   combinations explicitly experimental.
4. Run build.ps1, then package.ps1 -OfficialRelease -SigningKey <private-key>.
   The script refuses a mismatched public identity. All distributed application files
   are inventoried; source builds keep automatic app updates disabled.
5. When available, pass -AuthenticodeCertificateThumbprint and -TimestampServer
   to package.ps1. This signs WSG binaries before inventory signing. Never modify
   files after inventory signing.
   Scan the exact directory and ZIP with antivirus protections enabled and retain
   detection names, engine/signature versions, hashes, and results.
6. Publish the approved ZIP as WSG-OpenXR-FG-Switcher.zip and signed release
   metadata as WSG-OpenXR-FG-Switcher.version.json at
   https://wesimglide.org/otherdata/tools/. Upload the archive before metadata.
   Publishing and credentials are not performed by these scripts.

The updater accepts only its exact HTTPS feed/archive URLs, product identity,
signature, hashes, safe names, and inventory. Application data is outside the
package. The helper executes without elevation; only the separate layer helper
can request elevation, and only for existing VectorXR registration enable flags.

Catalog signatures use the domain prefix WSG-CATALOG-v1. Update signatures use
WSGOpenXrFgSwitcher release v1 / inventory v1. The app embeds a fallback catalog
and accepts newer signed provider catalogs from the fixed product feed.

## Provider updates without a WSG rebuild

Review upstream downloads, exact hashes/inventories and configuration compatibility
in assets/catalog.reviewed.json. Preserve existing entries and package identities.
Set MinimumSwitcherVersion only when new settings or adapter behavior require it.
Keep new combinations experimental until headset-tested. Then run:

    ./tools/prepare-provider-catalog.ps1 -SigningKey <release-key.dpapi> -Revision <increasing-number>

Upload both dist/provider-catalog/WSG-OpenXR-FG-Switcher.providers.json and
WSG-OpenXR-FG-Switcher.providers-v2.json to https://wesimglide.org/otherdata/tools/.
The legacy feed excludes unknown provider identities for WSG 0.1.1/0.1.2; WSG
0.1.3+ uses the v2 feed and provider-catalog-v2.signed.json cache. Both use the
same established signing identity and signature domain. This signs metadata with the established
product key and performs no app build, provider installation or network upload.
Commit the reviewed catalog and both signed envelopes for reproducibility.

The tig3rmast3r v663 settings contract is `tig3r-v663`; compatible later packages
can retain that contract and MinimumSwitcherVersion 0.1.3. Review the tagged
native configuration and route-bank behavior before assigning the contract.
Its update check includes published prereleases; older providers retain their
stable-release checks. Do not place Tig3r entries in the legacy feed, even with
a minimum app version: older clients cannot deserialize the new enum value.
Leave .htaccess untouched during this release's preparation and upload.

WSG 0.1.1+ refreshes this list on visible startup, on request, and when reopened
after an hour. It caches verified metadata for offline use and launch scripts.
No periodic network check runs in the tray. A refresh offers versions without
changing installed mods or saved version selections. Invalid signatures, rollback,
same-revision edits and changed package identities leave the current catalog intact.
