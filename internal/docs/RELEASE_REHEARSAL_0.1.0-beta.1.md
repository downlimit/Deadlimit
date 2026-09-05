# Private portable rehearsal: 0.1.0-beta.1

Status: **SUPERSEDED PACKAGE PASS — PURE-PORTABLE REHEARSAL REQUIRED**

- Rehearsal date: 2026-09-05
- Source commit: `89ae79be7ee53014b44b8b2156b16a19c2c0ac25`
- Private workflow run: [Portable release rehearsal 33925867426](https://github.com/downlimit/Deadlimit/actions/runs/33925867426)

This report records a private artifact rehearsal. It created no Git tag or
GitHub Release and did not change repository visibility.

The recorded artifact predates the pure-portable storage/update correction.
Its evidence remains historical; the public candidate requires a new rehearsal
whose ZIP creates no system shortcuts or AppData state and preserves in-folder
`UserData` across update and rollback.

## Passed checks

- GitHub-hosted Windows runner checkout, .NET 10 restore, and transaction smoke.
- Self-contained `win-x64` publish and portable ZIP creation.
- Installation of the produced ZIP into an isolated temporary location.
- Packaged `DeadlimitManager.exe --release-policy-smoke` verified that
  untrusted external-tool automation is blocked, then `--startup-smoke`
  verified normal portable startup.
- Private artifact upload through the current Node 24-based official actions.
- Published ZIP SHA-256 matches the downloaded artifact.
- Published updater SHA-256 matches the downloaded updater.
- All 362 entries declared by `release-manifest.json` match their byte counts
  and SHA-256 values.
- The installed updater activated the first real rehearsal package
  (`3A09D89A...E0DD3E5D9`), upgraded it transactionally to the second package
  (`1F3560DE...C24EE912`), preserved the first package under `.previous`, and
  restored it successfully through the installed rollback entry point.
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
| ZIP bytes | `82,726,499` |
| ZIP SHA-256 | `A6AED23230648CDAE5A59BAF05FAD2768723E14ADA5B49506142ECB9607FF45A` |
| Uncompressed bytes | `233,073,093` |
| Updater SHA-256 | `E9EE312B2A65EBD99D1AD22A1C98283F6D80782C11FE38982BD6FF89A86CA9D9` |
| Package version | `0.1.0-beta.1` |
| Runtime | `win-x64`, self-contained |

These hashes identify this private rehearsal artifact only. A later public
release must be rebuilt from its final tag and will have different hashes.

## Remaining no-go items

- Run a clean Windows 11 user-machine test outside the GitHub-hosted runner.
- Exercise automatic GitHub Releases channel selection after a private or
  public release exists; direct package activation, update, and rollback are
  already verified with two real rehearsal ZIPs.
- Refresh the exact compatibility snapshot at release time; current local
  versions, binary fingerprints, and Deadlock depot manifests are recorded in
  `COMPATIBILITY.md`.
- Receive explicit owner approval for public visibility and the first release.

Until these items are resolved or explicitly accepted at the documented owner
gate, the repository stays private and `v0.1.0-beta.1` must not be published.
