# Deadlimit Open-Source Readiness Plan

Status: **IN PROGRESS — repository remains private**

Last updated: 2026-09-05

This is the live implementation plan for preparing `github.com/downlimit/Deadlimit`
for public use and outside contributions. Update this document in the same pull
request whenever scope, evidence, risk, or completion status changes.

## Fixed decisions

- License: MIT.
- Copyright notice: `Copyright (c) 2026 Oleg Knyazev and Deadlimit contributors`.
- Contributions: independent forks are allowed and upstream pull requests are encouraged.
- Contribution attestation: Developer Certificate of Origin 1.1 (DCO), using signed-off commits.
- Repository owner and initial sole maintainer: `downlimit`.
- Primary documentation language: English; Russian documentation is secondary.
- Funding: no donations or sponsorship.
- Audience: free, best-effort tooling for Deadlock modding enthusiasts.
- Public user delivery: self-contained Windows x64 portable releases with a release-based updater.
- Developer delivery: Git clone with a separate `main`/developer update path.
- Public visibility and the first public release require explicit owner approval after the final audit.

## Status legend

- `[x]` complete and evidenced.
- `[~]` in progress or provisionally complete pending final review.
- `[ ]` not started.
- `[!]` owner decision or external/legal verification required.

## Phase 0 — Baseline and provenance audit

- [x] Confirm the repository is private and `main` is clean before readiness work.
- [x] Inventory tracked binary/game-resource extensions.
- [x] Confirm that tracked binaries are limited to project-owned icon files.
- [x] Confirm there are no tracked VPK, compiled Source 2 resources, DMX, FBX, DLL, or EXE files.
- [x] Inspect Wall Worm integration: it reads the export-history INI value and invokes the Autodesk FBX exporter; no Wall Worm source or binary was found.
- [x] Inspect Valve integration: the repository contains compatibility code and resource-schema names; retail resources are read/decompiled from the user's local installation at runtime and are not tracked.
- [x] Verify the two direct NuGet dependencies: KeyValues2 0.8.0 and ValveResourceFormat 20.0.6980 are MIT-licensed.
- [x] Inventory and verify every transitive NuGet dependency and required notice. The portable packager includes each exact nuspec, package-provided notice, resolved metadata, and full standard text for SPDX-only MIT/BSD packages.
- [x] Scan the full Git history for secrets, personal files, and prohibited game assets without printing secret values. The final 822-commit scan found zero prohibited asset paths and zero configured high-confidence credential signatures.
- [x] Audit all network download/install paths and record their owners, checksums, and trust boundaries in `NETWORK_TRUST_AUDIT.md`.
- [x] Review the opt-in CSDK setup path that automates DepotDownloader and local VPK extraction. Portable releases disable unverified CSDK/DepotDownloader/DeadlockTools automation at UI and service layers; existing tools remain selectable. The Developer channel retains explicit opt-in access.
- [x] Add a CI policy that rejects prohibited game archives, extracted game-tree paths, compiled retail resources, third-party executables/archives, and unexpected files larger than 2 MiB.

Phase acceptance: a written audit has no unresolved red finding; every yellow
finding has an explicit mitigation or owner-accepted limitation.

Current result: **accepted with documented yellow findings**. The repository and
history contain no detected prohibited content. Mutable, unauthenticated
toolchain automation is disabled in portable releases and remains available only
as an explicit Developer-channel operation.

## Phase 1 — Licensing and community contract

- [x] Add the MIT `LICENSE` file.
- [x] Add `THIRD_PARTY_NOTICES.md` with the resolved direct and transitive dependency inventory; exact release notice payload remains part of Phase 3.
- [x] Add `CONTRIBUTING.md` with fork/branch/test/PR workflow and DCO sign-off.
- [x] Add the DCO 1.1 text as `DCO`.
- [x] Add `CODE_OF_CONDUCT.md`.
- [x] Add `SECURITY.md` with private vulnerability reporting instructions.
- [x] Add `SUPPORT.md` defining best-effort support and no SLA.
- [x] Add `.github/CODEOWNERS` with `downlimit` as the initial owner.
- [x] Add issue templates for bugs and feature requests.
- [x] Add a pull-request template with validation and provenance checkboxes.
- [x] Add trademark, affiliation, third-party-tool, and user-supplied-content disclaimers.

Phase acceptance: GitHub detects the MIT license and a new contributor can
understand their rights, obligations, validation steps, and review path from root documentation.

## Phase 2 — Public documentation and repository shape

- [x] Rewrite the root `README.md` as the English product landing page.
- [x] Add `README.ru.md` as the secondary Russian guide.
- [x] Document a five-minute clone-based quick start and label the portable user path as pending.
- [x] Document the contributor/developer setup using .NET SDK 10.
- [x] Add `COMPATIBILITY.md` with the exact audited workstation snapshot: Windows/Max/Painter versions, Wall Worm build, CSDK binary fingerprints, Deadlock build/depot manifests, DeadlockTools release/commit/fingerprint, and Shade research status. Upstream archive authentication remains a separate trust gate.
- [x] Add `CHANGELOG.md` and adopt semantic versioning starting at `0.1.0-beta.1`.
- [x] Retire obsolete `DeadlimitAggregator*` entry points after compatibility review; keep only the neutral `Deadlimit.cmd` shim for older local shortcuts.
- [x] Remove maintainer-workstation path defaults from runtime code and public installation paths; derive clone/portable roots and keep Steam discovery explicit in Settings.
- [x] Expand `.gitignore`, `.gitattributes`, and `.editorconfig` for public development without renormalizing unrelated source files in this change.
- [x] Clearly separate current focus, experimental Shade, and unsupported/planned Blender and platforms.

Initial supported/tested matrix:

- Windows 11 x64: tested and supported.
- 3ds Max 2025: tested and supported.
- Wall Worm 7: supported only for the exact build recorded in release notes.
- Reduced CSDK 12: supported only for the exact setup generation recorded in release notes.
- Current Deadlock Steam build: tested snapshot recorded per release.
- .NET SDK 10: developer requirement.
- Windows 10: untested.
- Linux and macOS: unsupported.
- Deadlimit Shade: experimental.
- Blender: unsupported/planned.

Phase acceptance: a first-time user and a first-time contributor can follow
separate instructions without knowing the maintainer's workstation layout.

## Phase 3 — Delivery and updater split

- [x] Keep the current Git updater as the developer channel and label it accordingly in the public README.
- [x] Publish a self-contained `win-x64` portable ZIP that users extract into a writable folder; no bootstrap installer is required.
- [x] Build and update-smoke the portable ZIP in the private rehearsal workflow; public publication remains gated.
- [x] Generate a SHA-256 checksum alongside every rehearsal ZIP and require it during install/update.
- [x] Keep portable settings and caches under local `UserData`; create no automatic shortcuts, registry entries, or files outside the extracted folder.
- [x] Keep artist projects outside the replaceable application payload in their user-selected folders.
- [x] Implement a stable in-folder updater backed by GitHub Releases rather than `origin/main`.
- [x] Make release updates transactional, preserve `UserData`, restore the current payload after a failed activation, and keep one recoverable version under local `Backup`.
- [x] Keep an explicit Developer/main channel for contributors.
- [x] Test first install, no-op update, successful update, bad-checksum/traversal/broken-package preservation, and rollback with isolated synthetic packages.
- [x] Document the expected Windows SmartScreen warning for unsigned early releases in both public guides.

Phase acceptance: a non-Git user can extract, run, update, remove, and carry the portable folder without system residue,
while a contributor can clone and work on `main` without mixing the two channels.

## Phase 4 — CI, security, and repository policy

- [~] Keep existing Windows `build` and `smoke` jobs on pull requests; mark them required when public branch protection becomes available.
- [!] Add CodeQL for C# when the repository is public or private GitHub Advanced Security is available; do not add a knowingly unavailable required check.
- [x] Add Dependabot for NuGet and GitHub Actions.
- [~] Add dependency review and license-policy checks. Exact package license evidence and release-manifest verification pass; GitHub dependency review waits for public availability or GitHub Advanced Security.
- [x] Add repository-owned DCO enforcement for every pull-request commit.
- [x] Add release-package smoke tests, manifest/license checks, and checksum verification.
- [x] Set current workflows to read-only repository contents permissions.
- [x] Confirm fork pull requests never receive publication secrets: PR workflows use read-only contents permissions, reference no secrets, and no `pull_request_target` workflow exists; release publication is tag-only.
- [ ] After the repository becomes public, protect `main`:
  - require pull requests;
  - require `build` and `smoke`;
  - require conversation resolution;
  - require one owner approval;
  - block force pushes and branch deletion;
  - enable squash merge and automatic branch deletion.

Phase acceptance: unreviewed or failing code cannot reach `main`, and fork CI
cannot access release credentials.

## Current validation evidence

Validated locally on Windows 11 and in private CI through 2026-09-05:

- [x] `dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore` — 0 warnings, 0 errors.
- [x] `internal/tests/open-source-content-policy-smoke.ps1` — 144 repository files accepted.
- [x] `internal/tests/prepare-behavior-smoke.ps1` — prepare plus nested extraction/resource-copy contracts passed.
- [x] `internal/tests/metal-material-preset-smoke.ps1`.
- [x] `internal/tests/texture-naming-alias-smoke.ps1`.
- [x] `internal/tests/ui-localization-smoke.ps1`.
- [x] `internal/tests/updater-dirty-worktree-smoke.ps1`.
- [x] `internal/tests/launch-game-fastpath-smoke.ps1`.
- [x] Manager `--startup-smoke`.
- [x] Updater root-resolution contract.
- [x] Root launcher refresh and two-shortcut presentation contract.
- [x] Portable updater lifecycle, checksum rejection, traversal rejection, rollback, and single-file bootstrap parse/trust contracts.
- [x] Portable path-default and retired-entry-point contract.
- [x] Local self-contained `0.1.0-beta.1` rehearsal: 362 files, full license metadata/text payload, checksum verification, temporary install, and portable executable startup smoke.
- [x] `git diff --check`.
- [x] GitHub pull-request workflows on PR #95 initial head `89f491e`: `build`, `dco`, and `smoke` passed.
- [x] GitHub pull-request workflows on portable-channel PR #99 head `7553955`: `build`, `dco`, and `smoke` passed; merged as `94dac0e`.
- [x] GitHub pull-request workflows on portability PR #101: `build`, `dco`, and `smoke` passed; merged as `a8f8077`.
- [x] GitHub pull-request workflows on actions-update PR #102 passed with current official action majors; merged as `3b339dc`.
- [x] Private rehearsal run `33922258940`: self-contained package build, isolated install/startup smoke, and private artifact upload passed.
- [x] Downloaded rehearsal artifact audit: ZIP/updater checksums match, all 362 manifest entries verify, 363 ZIP entries contain zero detected prohibited game/authoring assets, and dependency license evidence is present.
- [x] Portable-policy PR #107 passed `build`, `dco`, and `smoke`; merged as `89ae79b`.
- [x] Private rehearsal run `33925867426` from `89ae79b`: packaged release-policy/startup smokes passed, and the downloaded ZIP again verified all 362 manifest items with zero prohibited or undeclared entries.
- [x] Pure-portable PR #109 passed `build`, `dco`, and `smoke`; merged as `8845f54`. Private rehearsal run `33961628049` then passed extraction, startup, manifest, in-folder updater, `UserData` preservation, and private artifact upload. The downloaded 82,728,285-byte ZIP independently matched SHA-256 `9A26C432...E09DBD3A` and all 362 manifest items.

## Phase 5 — Private release rehearsal and public launch

- [x] Build `0.1.0-beta.1` in the private repository and record exact artifact evidence in `RELEASE_REHEARSAL_0.1.0-beta.1.md`.
- [ ] Test installation on a clean Windows 11 environment without maintainer paths.
- [x] Test updater activation between synthetic packages, preservation of local `UserData`, local `Backup`, failed-update recovery, and rollback. The real pure-portable ZIP passed extraction/startup and same-package updater checks; an old-to-new real ZIP transition and automatic GitHub Releases selection remain publication-gated.
- [x] Re-run provenance, secret, and packaged-file audits: 822 commits produced zero prohibited asset-path hits and zero high-confidence credential-signature hits; the private portable ZIP passed the manifest and packaged-content audit recorded in the rehearsal report.
- [~] Produce a final go/no-go report for the owner. The current rehearsal report is NO-GO until the clean-machine, GitHub Releases selection, release-time compatibility refresh, and explicit owner gates are resolved.
- [!] Receive explicit owner approval to change visibility.
- [ ] Change `PRIVATE` to `PUBLIC`.
- [ ] Apply branch protection immediately after visibility changes.
- [ ] Publish `v0.1.0-beta.1` and seed a small set of `good first issue` tasks.

## Known risk register

### Yellow (mitigated for portable) — CSDK setup automation

The opt-in Developer channel can read a third-party CSDK guide, use
DepotDownloader, and extract the user's locally downloaded VPK data. Portable
releases disable this automation until upstream archives have pinned trusted
checksums. Source and release archives carry no Valve content; Developer-channel
operations identify third-party sources and stop on authentication/access
failure.

### Yellow — runtime decompilation of local retail resources

Hero extraction uses ValveResourceFormat against the user's local Deadlock
installation. Generated `0source` content belongs in the user's project and must
never be accepted into this repository, issues, or release archives.

### Yellow — rapidly changing external toolchain

Deadlock, Reduced CSDK, Wall Worm, and resource formats can change without
notice. Compatibility claims must name a tested snapshot and avoid a permanent
promise for unspecified "latest" versions.

### Yellow — unsigned Windows binaries

Early portable releases will likely trigger Windows reputation warnings. The
documentation must explain this accurately; code signing can be reconsidered if
the project gains enough users to justify certificate cost and maintenance.

## Change log

### 2026-09-05

- Merged private PR #99 with the portable packager, checksum-verified installer/updater, rollback path, security tests, release workflows, and updated documentation.
- Confirmed the repository remains private with zero tags and zero GitHub Releases after the merge.
- Replaced maintainer-specific runtime defaults with application-root derivation and explicit Settings-based Steam discovery, and retired the obsolete `DeadlimitAggregator*` launchers.
- Documented the unsigned-beta SmartScreen warning and checksum-verification rule in both public guides.
- Updated official GitHub Actions to their current Node 24-based majors, then passed private rehearsal run `33922258940` without the prior Node 20 warning.
- Recorded the private `0.1.0-beta.1` ZIP/updater hashes, full manifest verification, packaged-content audit, and remaining public-release no-go items.
- Captured the exact 2026-09-05 local compatibility snapshot without recording account identifiers or user content.
- Passed a real rehearsal-to-rehearsal portable update and installed-entry rollback using the two independently checksummed ZIPs.
- Re-scanned all 822 reachable commits without printing candidate values: zero prohibited game/authoring asset paths and zero high-confidence credential-signature hits.
- Disabled unverified CSDK, DepotDownloader, and DeadlockTools install/update automation in packaged portable releases at both UI and service layers; retained manual path selection and the opt-in Developer channel.
- Rebuilt a real self-contained portable package and passed both packaged release-policy and startup smokes, plus the manifest/checksum lifecycle test.
- Passed private rehearsal run `33925867426` from merged commit `89ae79b`; the downloaded 82,726,499-byte ZIP matched its checksum and all 362 manifest items, with zero prohibited or undeclared entries.

### 2026-09-04

- Created the readiness plan.
- Recorded the owner's licensing, governance, distribution, language, support,
  and funding decisions.
- Completed the first current-tree provenance and tracked-binary inventory.
- Confirmed MIT licensing for the two direct NuGet dependencies.
- Recorded the full resolved NuGet dependency/license inventory and the remaining exact-notice packaging gate.
- Scanned all 812 commits: zero prohibited historical asset paths and zero configured secret signatures.
- Added the network/external-execution trust audit; archive authenticity is a release blocker until versions and hashes are pinned.
- Added MIT licensing, DCO contribution rules, community health files, issue/PR templates, ownership, support/security policies, and third-party disclaimers.
- Added a CI content policy for retail resources, extracted paths, executables, archives, and unexpected large files.
- Replaced the root landing page, added the Russian guide, documented the actual ONLINE CSDK recovery contract, and added compatibility/changelog files.
- Added public development formatting/ignore rules, Dependabot, read-only workflow permissions, and a repository-owned DCO check.
- Passed the complete locally available CI/smoke set and recorded the evidence.
- Published private PR #95; its initial `build`, `dco`, and `smoke` checks all passed.
- Began the portable release channel: self-contained packager, SHA-256 and file manifest, GitHub-Releases updater, transactional rollback, synthetic lifecycle smoke, and a private rehearsal workflow.
- Added the single-file bootstrap, updater-asset checksum, static bootstrap trust test, and tag-gated GitHub release workflow. No tag or public release was created.
- Kept repository visibility private pending the final explicit approval gate.
