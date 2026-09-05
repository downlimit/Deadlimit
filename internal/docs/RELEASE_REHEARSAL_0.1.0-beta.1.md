# Private portable rehearsal: 0.1.0-beta.1

Status: **PACKAGE MECHANICS PASS — PUBLIC RELEASE NO-GO**

- Rehearsal date: 2026-09-05
- Source commit: `8845f54b4ca1cd3022e429008ee503278203ab64`
- Private workflow run: [Portable release rehearsal 33961628049](https://github.com/downlimit/Deadlimit/actions/runs/33961628049)

This report records a private artifact rehearsal. It created no Git tag or
GitHub Release and did not change repository visibility.

## Passed checks

- GitHub-hosted Windows runner checkout, .NET 10 restore, and transaction smoke.
- Self-contained `win-x64` publish and portable ZIP creation.
- Direct extraction of the produced ZIP into an isolated writable folder.
- Packaged `DeadlimitManager.exe --release-policy-smoke` verified that
  portable settings resolve under in-folder `UserData` and untrusted
  external-tool automation is blocked; `--startup-smoke` verified startup.
- Private artifact upload through the current Node 24-based official actions.
- Published ZIP SHA-256 matches the downloaded artifact.
- All 362 entries declared by `release-manifest.json` match their byte counts
  and SHA-256 values.
- The ZIP contains no `UserData`, `Backup`, `Backup.next`, or bootstrap
  installer. Its updater source contains no Start-menu/Desktop shortcut logic.
- Synthetic package tests verified checksum/traversal/broken-package rejection,
  transactional in-place update, preservation of local `UserData`, recovery,
  local `Backup`, and rollback.
- The downloaded real ZIP passed manifest, release-policy, startup, and
  in-folder updater no-op checks; locally created `UserData` survived.
- The ZIP contains 363 entries, including the manifest, with zero detected DMX,
  FBX, MAX, VPK, compiled Source 2, or extracted retail/game-tree entries.
- The ZIP contains the project license, third-party notices, and 57 files under
  the dependency license evidence tree.
- The final private history pass scanned all 822 reachable commits and found
  zero prohibited game/authoring asset paths and zero configured
  high-confidence credential signatures. Candidate values were suppressed from
  audit output.

## Artifact evidence

| Item | Value |
| --- | --- |
| ZIP | `Deadlimit-win-x64.zip` |
| ZIP bytes | `82,728,285` |
| ZIP SHA-256 | `9A26C432455712257538F22DA4D8929A9B34878B508CBE719D1E2584E09DBD3A` |
| Uncompressed bytes | `233,078,911` |
| In-package updater SHA-256 | `D55122D092F67C8E50334760918A85B3BEB710AA896CE3068423C4C305003D64` |
| Package version | `0.1.0-beta.1` |
| Runtime | `win-x64`, self-contained |

These hashes identify this private rehearsal artifact only. A later public
release must be rebuilt from its final tag and will have different hashes.

## Remaining no-go items

- Run a clean Windows 11 user-machine test outside the GitHub-hosted runner.
- Exercise automatic GitHub Releases channel selection after a private or
  public release exists. Also rehearse an actual old-to-new transition between
  two pure-portable release ZIPs; the same transaction is covered synthetically.
- Refresh the exact compatibility snapshot at release time; current local
  versions, binary fingerprints, and Deadlock depot manifests are recorded in
  `COMPATIBILITY.md`.
- Receive explicit owner approval for public visibility and the first release.

Until these items are resolved or explicitly accepted at the documented owner
gate, the repository stays private and `v0.1.0-beta.1` must not be published.
