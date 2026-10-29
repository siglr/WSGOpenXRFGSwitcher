## v0.1.5 — visible startup errors

- Failed one-shot application opens the switcher visibly before its existing
  centered error dialog, overriding tray/minimized startup for that attempt.
  Its failed result is saved before acknowledgement and its exit code stays
  nonzero. The saved Apply-on-launch preference is not retried in this window.
- Closing a failed command's recovery window exits without provider cleanup;
  successful one-shot commands retain their existing silent apply-and-exit flow.
  Startup initialization runs once even when restoring the taskbar recreates
  the native window handle.
- 543 core checks, all 41 updater checks, 33 native-dialog checks and 31 isolated
  startup UI checks passed. The UI fixture starts hidden and covers external
  changes, missing installations, busy games, pending recovery and all tray aliases.
- The signed v0.1.5 package and all 18 installed managed file hashes verified.
  Updated C:\MSFS Tools\WSG OpenXR FG Switcher for user testing. The installed
  application was left closed; no game, provider, runtime or user settings were
  changed during validation. The user approved public deployment after local testing.
- Published v0.1.5 on GitHub and WeSimGlide. GitHub's five asset digests and the
  live ZIP, checksum, HTML and both catalog feeds match the staged files. The
  version feed was uploaded last and exposes the signed v0.1.5 package identity:
  1,230,363 bytes, SHA-256
  `bdcc4964df3f8c46cc13bce63c99a58678e0cf000faa0aacd7217bd10bb5c235`.
  Website checks passed and the public page displays 0.1.5. Defender's ZIP scan
  returned exit code 0 and no threats. .htaccess was not changed.

## Provider catalog 2026102801 — VectorXR v0.19.3

- Reviewed the v0.19.2-to-v0.19.3 tagged settings-schema, native parser, app
  normalization, single-instance and tray changes. Configuration remains version
  3; WSG's managed FG fields retain their values and meanings. New prediction,
  performance-overlay and row-order settings remain under VectorXR's control.
- The official 4,113,088-byte installer matches GitHub's SHA-256
  `1dd7b10950e145d702ee383b53b8b5db112c9a5aec323daf8ff6d12077aef5bc`.
  Its Authenticode signature is valid and the publisher is DienerTech LLC.
- 545 isolated core/catalog checks passed. Tests cover the new advanced settings,
  coexistence, selected-profile precedence and byte-for-byte rollback. Actual
  released WSG 0.1.2 and 0.1.4 assemblies accept their signed feed updates,
  preserve all published package identities, expose the v0.19.3 download and
  preserve its advanced settings without rebuilding the client.
- VectorXR's new hidden tray process remains detectable by WSG's existing
  process-name guard. Users must choose Quit VectorXR from its tray before WSG
  changes its configuration. The README documents this behavior.
- This is a catalog-only update; WSG remains v0.1.4. No installer, upstream
  provider, game or live activation was run, and no runtime or user settings
  were changed. Real headset testing remains pending.
- Both public catalog URLs match the staged files, verify with the existing
  release key and expose v0.19.3 at revision 2026102801. The app version feed
  remains byte-identical at v0.1.4. Only the two catalog feeds were uploaded;
  .htaccess and the app distribution were not changed.

## v0.1.4 — djules OFXR 0.2.14.3 (V577)

- Reviewed the tagged native settings reader/writer, 4X controls and x86
  registration lifecycle. The official 1,939,125-byte ZIP matches GitHub's digest:
  `bbd1cef21bb317cd68abe010e4d7301056cd2829681786c4c637ef041f62cc20`.
  All 18 package files are pinned in signed catalog revision 2026102701.
- 4X writes frame_multiplier=4, triple_frame_gen=1 and deep_pipeline=1.
  Returning to 2X/3X clears the explicit override. HUD fix is independent of
  Hybrid and off by default. Existing presets retain their settings.
- Layer verification checks the signed x64/x86/Vulkan inventories individually,
  including both OpenXR registrations in the shared HKCU registry view. Duplicate,
  stale, modified and unreviewed cache DLLs are refused.
- Real-package extraction, identification, repair and removal passed in isolated
  folders. Settings, launch scripts, switching, rollback, applied snapshots and
  the actual released v0.1.3 client's version gate passed. V577 requires WSG 0.1.4;
  older clients retain 0.2.14.2 as their compatible download.
- 540 core/package checks, 158 settings UI checks and all 41 updater checks
  passed. Both themes, multiplier/version changes, HUD independence, resize and
  disclosure layout were checked. The signed release package and all 18 managed
  file hashes verified. Defender scans of the release folder and ZIP were clean.
- The upstream tray, x64 layer and Vulkan DLL have valid Julien Choulet signatures;
  the upstream x86 DLL is unsigned and pinned by its reviewed package hash.
  No upstream binary, MSFS or live provider was launched during validation.
- Published v0.1.4 on GitHub and WeSimGlide. The public ZIP (1,227,203 bytes),
  checksum, both signed catalog feeds and download page match the staged files.
  Both catalog signatures verify at revision 2026102701. Version metadata was
  uploaded last and the normal updater URL returns the signed v0.1.4 identity,
  SHA-256 `0c010fd793e09f4e752eab600dda9871a33d168644ada451ed009f7a1e18057b`.
  The public page displays 0.1.4; .htaccess was not changed.

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
- Published v0.1.3 on GitHub and WeSimGlide. The live ZIP, checksum, both
  signed catalog feeds, version metadata, download page, stylesheet and screenshot
  match the staged files. The version feed was uploaded last after package
  verification. The public page displays 0.1.3; .htaccess was not changed.
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
