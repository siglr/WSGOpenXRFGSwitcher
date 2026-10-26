## v0.1.3 — tig3rmast3r OFXR beta

- The official 0.3.0-beta-v663 ZIP (14,003,103 bytes) matches GitHub's SHA-256:
  `56496815d5e164fb4b6f127bc680d279d55a7eecabd66d37f08351eba33b85b3`.
  Its complete 34-file inventory is signed in catalog revision 2026102501.
- 537 core and real-package checks passed. Fixtures cover extraction,
  identification, repair, removal and interrupted-removal recovery; all 25
  switching directions; per-game and per-route persistence; preservation of
  advanced settings; native startup writes, rollback and external-edit guards.
- The actual released v0.1.2 core accepts the legacy signed feed. The v2 feed
  and separate cache add Tig3r without changing previously published package
  identities. Offline use, future compatible catalog-only releases, invalid
  signatures and rollback rejection passed.
- 136 settings UI checks passed, including all five cards, both themes, resize,
  disclosures, native route controls, Applied status and startup selection.
  The background fixture passed 12 checks; Exit fixtures passed 14 checks.
- All 41 updater checks passed. The official app-only distribution has automatic
  updates enabled; all 18 managed files, release signature and ZIP hash verified.
  Website checks passed at 1440, 768 and 390 pixels, including download metadata,
  disclosure behavior and failure fallbacks. Defender scans found no threats.
- Validation uses isolated settings and fake hosts. No upstream binary or MSFS
  was launched, and no live provider, runtime registration or user preset changed.
  Real headset testing of this upstream beta remains pending.

## Provider catalog 2026102301 — djules OFXR 0.2.14.2

- Catalog-only update for existing WSG 0.1.1+ clients; app version remains 0.1.2.
- The official 0.2.14.2 (V557) ZIP matches GitHub's published SHA-256:
  `d5f36673251b6c9ae3152fc7696de6022dca667ea79ce6a57add5a211b6649c9`.
  Its tray and both layer DLLs have valid Julien Choulet Authenticode signatures.
- The default INI is byte-identical to 0.2.14.1, the package layout is unchanged,
  and the tagged tray changes preserve WSG's existing settings contract.
- All 429 core and upstream-package checks passed, including identification,
  installation, repair, paths with spaces and staging cleanup for the real ZIP
  in isolated fixtures. Existing package identities remain unchanged.
- No upstream binary or game was launched, and no installed provider, runtime
  registration or user preset was changed. Headset validation remains pending.

## v0.1.2 — overlay settings layout

- 100 isolated UI checks passed. FPS overlay Off/position stays in the main
  settings row; its size and display toggles have their own labelled block in
  More options, separate from Hybrid. Flow resolution uses an aligned inline row.
- Both themes, overlay Off/on, saved option retention, Hybrid independence,
  older provider versions, tab changes and resize/collapse layout were checked.
- 397 core checks and all 41 updater checks passed. The signed release package,
  archive checksum and all 18 managed file hashes verified.
- Disclosure rows retain their explicit order when hidden native controls return.
  Disabled overlay controls leave the section and Size labels readable.
- Validation used isolated preview fixtures. No game, installed provider,
  runtime registration or user preset was changed.

## v0.1.1 — djules and independent provider updates

- 397 core checks and 41 updater checks passed. Optional read-only upstream
  inventory/install-fixture checks passed too (424 checks total in that run).
- The real djules 0.2.14.1 ZIP matches GitHub's SHA-256. Its tray and both layer
  DLLs have valid Julien Choulet Authenticode signatures. No upstream binary ran.
- Hybrid, depth search, quarter-resolution flow and overlay configuration keys
  were checked against tagged upstream source. Version restrictions, fork
  isolation, switching back and transaction rollback passed isolated fixtures.
- Provider-only signed catalog updates add and install a new version without a
  WSG build. Offline launch-script use, missing/corrupted metadata, invalid
  signatures, rollback, changed package identities, minimum app versions and
  hidden/busy deferral passed. No periodic network polling was added in the tray.
- 62 isolated UI checks cover new controls, version selection, update notices,
  tab isolation, resize/collapse layout and existing application interactions.
- Real headset testing for 0.2.14.1 remains pending. No game, installed provider,
  runtime registration or user preset was changed during validation.

# Validation — v0.1.0 preview

Automated checks and actual headset acceptance are tracked separately.
No provider combination is labelled headset-tested in this release.

## Automated checks

- build.ps1: 349 core checks and 41 updater checks. Isolated fixtures cover
  provider switching and settings isolation, interrupted operations, removal,
  external changes, safe package extraction, configuration handling, idle/startup
  behavior and cleanup. Updater checks cover authenticity, package integrity,
  staging, rollback and process interruption.
- Native dialog fixtures: 33 checks for parent centering, nested/hidden owners,
  screen edges, modality and button results. Run
  `dotnet run --project tests/Tests.csproj -c Release -- --dialogs`.
- tools/verify-package.ps1: signed product identity, release metadata, archive
  size/hash and all 18 managed file hashes.
- tools/check-web.cjs: local Edge review at 1440, 768 and 390 pixels, navigation,
  collapsible help, images, overflow, download/hash agreement and metadata failure
  fallbacks. Requires Playwright and the shared tools website sources.

Tests do not launch MSFS or require a headset. Fixture output is kept locally;
private machine paths and session logs are excluded from the public repository.
See ANTIVIRUS-REVIEW.md for the exact published package scan.

## Hardware acceptance still pending

Validate MSFS 2024 and another OpenXR game with each reviewed provider and Off.
Record game/provider versions, runtime, headset, GPU/driver, settings and observed
results. Test repeated transitions, exit cleanup and actual frame generation.
Test VectorXR coexistence separately with each OFXR fork; record all enabled
modules and Toolkit settings. Untested combinations remain experimental.

## Release update channel

Published packages enable automatic signed updates; ordinary source builds do not.
The release key is separate from the other WSG products and protected outside Git.
Verify the packaged update flag, public-key signatures and live feed after upload.
Installation must wait while games or provider operations are active. Keep future
update simulations isolated from the user's installed providers and game state.
