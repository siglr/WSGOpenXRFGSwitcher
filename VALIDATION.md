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
