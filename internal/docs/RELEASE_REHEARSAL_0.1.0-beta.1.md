# Private portable rehearsal: 0.1.0-beta.1

Status: **PACKAGE MECHANICS PASS — PUBLIC RELEASE NO-GO**

- Rehearsal date: 2026-09-05
- Source commit: `3b339dcf02ee3552033a7e52af01eac2e031d08c`
- Private workflow run: [Portable release rehearsal 33922258940](https://github.com/downlimit/Deadlimit/actions/runs/33922258940)

This report records a private artifact rehearsal. It created no Git tag or
GitHub Release and did not change repository visibility.

## Passed checks

- GitHub-hosted Windows runner checkout, .NET 10 restore, and transaction smoke.
- Self-contained `win-x64` publish and portable ZIP creation.
- Installation of the produced ZIP into an isolated temporary location.
- Packaged `DeadlimitManager.exe --startup-smoke` through the portable test.
- Private artifact upload through the current Node 24-based official actions.
- Published ZIP SHA-256 matches the downloaded artifact.
- Published updater SHA-256 matches the downloaded updater.
- All 362 entries declared by `release-manifest.json` match their byte counts
  and SHA-256 values.
- The ZIP contains 363 entries, including the manifest, with zero detected DMX,
  FBX, MAX, VPK, compiled Source 2, or extracted retail/game-tree entries.
- The ZIP contains the project license, third-party notices, and 57 files under
  the dependency license evidence tree.

## Artifact evidence

| Item | Value |
| --- | --- |
| ZIP | `Deadlimit-win-x64.zip` |
| ZIP bytes | `82,724,527` |
| ZIP SHA-256 | `1F3560DE9645E44DEEBEACF2F02E4186F2603F88A5AD8019457EBBD2C24EE912` |
| Uncompressed bytes | `233,068,024` |
| Updater SHA-256 | `E9EE312B2A65EBD99D1AD22A1C98283F6D80782C11FE38982BD6FF89A86CA9D9` |
| Package version | `0.1.0-beta.1` |
| Runtime | `win-x64`, self-contained |

These hashes identify this private rehearsal artifact only. A later public
release must be rebuilt from its final tag and will have different hashes.

## Remaining no-go items

- Run a clean Windows 11 user-machine test outside the GitHub-hosted runner.
- Test an update from a previous real rehearsal installation; synthetic
  install/update/rollback tests already pass.
- Refresh the exact compatibility snapshot at release time; current local
  versions, binary fingerprints, and Deadlock depot manifests are recorded in
  `COMPATIBILITY.md`.
- Resolve the unauthenticated mutable CSDK, DepotDownloader, and DeadlockTools
  download paths by pinning reviewed versions/checksums or disabling their
  automatic installers in the public portable build.
- Complete the final source/history secret and provenance rescan.
- Receive explicit owner approval for public visibility and the first release.

Until these items are resolved or explicitly accepted at the documented owner
gate, the repository stays private and `v0.1.0-beta.1` must not be published.
