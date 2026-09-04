# Deadlimit Open-Source Readiness Plan

Status: **IN PROGRESS — repository remains private**

Last updated: 2026-09-04

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
- [x] Scan the full Git history for secrets, personal files, and prohibited game assets without printing secret values. The 812-commit scan found zero prohibited asset paths and zero configured high-confidence or generic credential signatures.
- [x] Audit all network download/install paths and record their owners, checksums, and trust boundaries in `NETWORK_TRUST_AUDIT.md`.
- [!] Review the opt-in CSDK setup path that automates DepotDownloader and local VPK extraction. Deadlimit must not bundle Valve/CSDK content, imply Valve authorization, or bypass access controls.
- [x] Add a CI policy that rejects prohibited game archives, extracted game-tree paths, compiled retail resources, third-party executables/archives, and unexpected files larger than 2 MiB.

Phase acceptance: a written audit has no unresolved red finding; every yellow
finding has an explicit mitigation or owner-accepted limitation.

Current result: **accepted with documented yellow findings**. The repository and
history contain no detected prohibited content. Mutable, unauthenticated
toolchain downloads remain blocked from the first portable release until the
mitigations in `NETWORK_TRUST_AUDIT.md` are implemented or those installers are
disabled.

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
- [~] Add `COMPATIBILITY.md` with the current baseline; exact Wall Worm, CSDK, Deadlock manifest, DeadlockTools, and Shade versions still require release-time capture.
- [x] Add `CHANGELOG.md` and adopt semantic versioning starting at `0.1.0-beta.1`.
- [ ] Move or retire obsolete `DeadlimitAggregator*` entry points after compatibility review.
- [ ] Remove mandatory assumptions about `C:\WorkProjects\Deadlock\Deadlimit` from public installation paths.
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
- [x] Create a single-file `Install-Deadlimit.cmd` bootstrap for users without Git; it fetches and verifies the release updater asset before execution.
- [~] Build a self-contained `win-x64` portable ZIP in the private rehearsal workflow; public publication remains gated.
- [x] Generate a SHA-256 checksum alongside every rehearsal ZIP and require it during install/update.
- [x] Install portable builds under `%LocalAppData%\Programs\Deadlimit` by default.
- [x] Keep existing user settings under `%LocalAppData%\Deadlimit` and artist projects outside the replaceable application directory.
- [x] Implement a stable updater backed by GitHub Releases rather than `origin/main`, with a single-file bootstrap and a local installed updater entry point.
- [x] Make release updates transactional, restore the current install after a failed activation, and preserve one recoverable previous version.
- [ ] Keep an explicit Developer/main channel for contributors.
- [x] Test first install, no-op update, successful update, bad-checksum/traversal/broken-package preservation, and rollback with isolated synthetic packages.
- [ ] Document the expected Windows SmartScreen warning for unsigned early releases.

Phase acceptance: a non-Git user can install and update with one bootstrap,
while a contributor can clone and work on `main` without mixing the two channels.

## Phase 4 — CI, security, and repository policy

- [~] Keep existing Windows `build` and `smoke` jobs on pull requests; mark them required when public branch protection becomes available.
- [ ] Add CodeQL for C#.
- [x] Add Dependabot for NuGet and GitHub Actions.
- [ ] Add dependency review and license-policy checks.
- [x] Add repository-owned DCO enforcement for every pull-request commit.
- [x] Add release-package smoke tests, manifest/license checks, and checksum verification.
- [x] Set current workflows to read-only repository contents permissions.
- [ ] Confirm fork pull requests never receive publication secrets.
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

Validated locally on Windows 11 on 2026-09-04:

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
- [x] Local self-contained `0.1.0-beta.1` rehearsal: 362 files, full license metadata/text payload, checksum verification, temporary install, and portable executable startup smoke.
- [x] `git diff --check`.
- [x] GitHub pull-request workflows on PR #95 initial head `89f491e`: `build`, `dco`, and `smoke` passed.
- [x] GitHub pull-request workflows on portable-channel PR #99 head `7553955`: `build`, `dco`, and `smoke` passed; merged as `94dac0e`.

## Phase 5 — Private release rehearsal and public launch

- [ ] Build `0.1.0-beta.1` in the private repository.
- [ ] Test installation on a clean Windows 11 environment without maintainer paths.
- [ ] Test Stable update from the previous rehearsal build.
- [ ] Re-run provenance, secret, and packaged-file audits.
- [ ] Produce a final go/no-go report for the owner.
- [!] Receive explicit owner approval to change visibility.
- [ ] Change `PRIVATE` to `PUBLIC`.
- [ ] Apply branch protection immediately after visibility changes.
- [ ] Publish `v0.1.0-beta.1` and seed a small set of `good first issue` tasks.

## Known risk register

### Yellow — CSDK setup automation

Deadlimit currently reads a third-party CSDK guide, uses DepotDownloader, and
extracts the user's locally downloaded VPK data. Public source and releases must
carry no Valve content. The action must be opt-in, identify third-party sources,
and stop on authentication/access failure.

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
