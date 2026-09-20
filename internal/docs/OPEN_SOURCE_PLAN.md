# Deadlimit Open-Source Readiness Plan

Status: **PUBLIC SOURCE — FIRST BETA RELEASE PUBLISHED**

Last updated: 2026-09-20

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
- Public user delivery: one-file bootstrap that creates a Git checkout of `main`; Git for Windows and .NET 10 SDK are required.
- Developer delivery: the same Git checkout/update path.
- The repository is public; supported installs track `main`.

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
- [x] Inventory and verify every transitive NuGet dependency and required notice. The resolved dependency inventory and required notices are recorded in `THIRD_PARTY_NOTICES.md`.
- [x] Scan the full Git history for secrets, personal files, and prohibited game assets without printing secret values. The final 822-commit scan found zero prohibited asset paths and zero configured high-confidence credential signatures.
- [x] Audit all network download/install paths and record their owners, checksums, and trust boundaries in `NETWORK_TRUST_AUDIT.md`.
- [x] Review the opt-in CSDK setup path that automates DepotDownloader and local VPK extraction. The supported Git installation exposes explicitly initiated CSDK/DepotDownloader/DeadlockTools actions.
- [x] Add a CI policy that rejects prohibited game archives, extracted game-tree paths, compiled retail resources, third-party executables/archives, and unexpected files larger than 2 MiB.

Phase acceptance: a written audit has no unresolved red finding; every yellow
finding has an explicit mitigation or owner-accepted limitation.

Current result: **accepted with documented yellow findings**. The repository and
history contain no detected prohibited content. Mutable upstream tool downloads
remain explicit user actions and share the same behavior across delivery modes.

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
- [x] Document the clone-based user and contributor start paths.
- [x] Document the contributor/developer setup using .NET SDK 10.
- [x] Add `COMPATIBILITY.md` with the exact audited workstation snapshot: Windows/DCC/Painter versions, Wall Worm build, CSDK binary fingerprints, Deadlock build/depot manifests, DeadlockTools release/commit/fingerprint, and Shade research status. Upstream archive authentication remains a separate trust gate.
- [x] Add `CHANGELOG.md` and use semantic versioning for numbered source milestones.
- [x] Retire obsolete `DeadlimitAggregator*` entry points after compatibility review; keep only the neutral `Deadlimit.cmd` shim for older local shortcuts.
- [x] Remove maintainer-workstation path defaults from runtime code and public installation paths; derive repository roots and keep Steam discovery explicit in Settings.
- [x] Expand `.gitignore`, `.gitattributes`, and `.editorconfig` for public development without renormalizing unrelated source files in this change.
- [x] Clearly separate current focus, experimental Shade, and unsupported/planned Blender and platforms.

Initial supported/tested matrix:

- Windows 11 x64: tested and supported.
- Deadlimit Scripts MAXScript host 2025: tested and supported.
- Wall Worm 7: supported only for the exact build recorded in release notes.
- Reduced CSDK 12: supported only for the exact setup generation recorded in release notes.
- Current Deadlock Steam build: tested snapshot recorded per release.
- .NET SDK 10: required for all supported installations.
- Windows 10: untested.
- Linux and macOS: unsupported.
- Deadlimit Shade: experimental.
- Blender: unsupported/planned.

Phase acceptance: a first-time user and a first-time contributor can follow
separate instructions without knowing the maintainer's workstation layout.

## Phase 3 — Git-only delivery and updater

- [x] Use one permanent `Install-Deadlimit.cmd` bootstrap from the repository.
- [x] Require Git for Windows and .NET 10 SDK instead of publishing a
  self-contained Deadlimit package.
- [x] Install as a normal `main` checkout and build Deadlimit Manager locally.
- [x] Use the same `origin/main` fast-forward/rebuild updater for every
  supported installation.
- [x] Preserve user settings and caches in centralized
  `%LocalAppData%\Deadlimit`.
- [x] Publish numbered source milestones with only `Install-Deadlimit.cmd` and
  its SHA-256 checksum as user-facing assets.
- [x] Do not publish packaged Deadlimit binaries or routine per-merge artifacts.


## Phase 4 — CI, security, and repository policy

- [x] Require the Windows `build`, `dco`, and `smoke` checks on protected `main`.
- [ ] Add CodeQL for C# as a later security improvement; it is outside the first installer/release scope.
- [x] Add Dependabot for NuGet and GitHub Actions.
- [~] Add dependency review and license-policy checks. Dependency license evidence is recorded; GitHub dependency review still depends on repository feature availability.
- [x] Add repository-owned DCO enforcement for every pull-request commit.
- [x] Keep PR validation read-only; grant contents write only to the post-merge source-milestone workflow.
- [x] Confirm fork pull requests cannot publish: source milestones are created only after successful checks on protected `main`, using the scoped workflow token.
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

The current Git-only delivery contract is validated by the repository CI:
Release build, startup smoke, installer/updater static contract, updater
root-resolution and dirty-worktree transaction smoke, path-default policy,
content policy, and the existing pipeline regression tests.

## Phase 5 — Public repository operation

- [x] Repository is public with protected `main`.
- [x] MIT/DCO/community-health files are in place.
- [x] Supported user installation now follows the same Git checkout as
  development, with a one-file bootstrap and guarded updater.
- [ ] Continue clean-machine testing with real artists on the Git-only install
  path and record compatibility regressions against concrete commits.

## Known risk register

### Yellow — CSDK setup automation

Deadlimit can read a third-party CSDK guide, use DepotDownloader, and extract
the user's locally downloaded VPK data after an explicit action. The supported
Git installation exposes this behavior. The repository carries no Valve content; these operations identify third-party sources
and stop on authentication/access failure.

### Yellow — runtime decompilation of local retail resources

Hero extraction uses ValveResourceFormat against the user's local Deadlock
installation. Generated `0source` content belongs in the user's project and must
never be accepted into this repository or issues.

### Yellow — rapidly changing external toolchain

Deadlock, Reduced CSDK, Wall Worm, and resource formats can change without
notice. Compatibility claims must name a tested snapshot and avoid a permanent
promise for unspecified "latest" versions.

### Yellow — unsigned Windows scripts and locally built executable

The bootstrap and locally built Manager are unsigned. Windows reputation or
script-policy warnings can still occur; code signing can be reconsidered if the
project gains enough users to justify certificate cost and maintenance.
