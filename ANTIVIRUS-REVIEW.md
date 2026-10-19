# Antivirus review — v0.1.0 public preview

The final release directory and ZIP passed Microsoft Defender custom scans on
October 19, 2026: exit 0, no threats. Engine 1.1.26090.9; signatures 1.461.106.0.
Antivirus and real-time protection were enabled. No security settings were changed
and no samples were uploaded to an external scanning service.

ZIP SHA-256:
`2e1de1c5ab51c6ae1663f6d6d5a8f97311394a40397a2407fb0ffcfd28bd0aa3`

The signed release metadata, archive and all 18 managed files verified. The
packaged automatic-update flag was confirmed enabled and its release metadata
accepted by the packaged verification code. Ordinary source builds disable updates.

## Code and distribution review

- Ordinary framework-dependent .NET 10 files; no packing, obfuscation, runtime
  self-extraction, downloaded scripts or WSG game-process injection.
- Official provider downloads checked against a signed catalog and exact hashes;
  safe archive extraction. VectorXR also requires the expected publisher signature.
- Explicit process paths and working directories. OFXR receives graceful close
  requests. Installation identity uses reviewed file inventories, not folder names.
- Process/module inspection protects active games from provider changes. Background
  discovery stops when minimized or in the tray; reopening refreshes state.
- The narrowly scoped elevated helper validates VectorXR registrations and signed
  libraries. It accepts no arbitrary registry paths or executable commands.
- Fixed HTTPS update URLs, signed metadata/inventories, verified package hashes,
  staged updates and recovery. Games and pending provider operations defer restart.
- No antivirus exclusions, protection disabling, services or scheduled tasks.
- Standard dialog centering uses a bounded UI-thread callback, with no hooks.

WSG binaries are not Authenticode-signed. RSA package signatures provide update
integrity, not Windows publisher reputation. Trusted publisher signing remains
future work. A clean local scan cannot guarantee that no antivirus or SmartScreen
warning will occur. This is not a multi-vendor clearance or complete behavioral
analysis. Provider binaries are separate upstream products.

[Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
