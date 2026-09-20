# Changelog

Deadlimit is delivered from `main` as a Git checkout. The installer and updater
keep that checkout current and rebuild the Manager locally. Numbered tags remain
historical source milestones. Dates use `YYYY-MM-DD`.

## Unreleased

No user-facing changes recorded yet.

## 0.1.0-beta.3 - 2026-09-20

### Changed

- The one-file installer uses Git for Windows and the .NET 10 SDK, asks before
  installing either missing dependency through WinGet, then clones `main` and
  builds Deadlimit Manager locally.
- Every supported installation now updates through the existing Git
  fast-forward/rebuild path.
- The portable ZIP, `latest-main` rolling release channel, package rollback
  payload, and package-specific release policy were retired.
- Numbered GitHub releases are source milestones. Their only user-facing
  downloadable assets are `Install-Deadlimit.cmd` and its SHA-256 checksum;
  no Deadlimit binary ZIP is published.
- CI no longer uploads the identifier-audit artifact or publishes large
  per-merge release packages.
- User settings and caches use the centralized `%LocalAppData%\Deadlimit`
  location for all supported installations.
- Settings shows Deadlimit Manager as the first tool row, including the current
  `main` commit status and a contextual check/update action.
- Runtime tool defaults derive from the current application location and Steam
  discovery instead of maintainer-workstation drive paths.
- Obsolete `DeadlimitAggregator*` compatibility entry points remain retired;
  `Deadlimit.cmd` stays as the neutral legacy shim.
- Public project documentation was rebuilt around the artist workflow and now
  includes English, Russian, Simplified Chinese, and Brazilian Portuguese
  landing pages.

### Added

- MIT licensing and DCO-based contribution policy.
- Community health, support, security, ownership, issue, and pull-request files.
- Dependency, external-tool, compatibility, and network trust documentation.
- CI policy rejecting retail game resources, extracted content paths,
  third-party binary/archive formats, and unexpected large files.

### Security

- External CSDK, DepotDownloader, and DeadlockTools operations remain explicit
  user actions and are not distributed as part of Deadlimit.
- Final 822-commit history scan found no configured high-confidence secret
  signatures or prohibited historical asset paths on 2026-09-05.

## 0.1.0-beta.2 - 2026-09-05

### Changed

- Established the canonical product names **Deadlimit Manager**,
  **Deadlimit Scripts**, and **Deadlimit Shade**.
- Renamed the bundled scripting product and source layout from the earlier
  Max-focused naming to **Deadlimit Scripts**, while retaining MAXScript
  implementation identifiers where compatibility requires them.

## 0.1.0-beta.1 - 2026-09-05

First public beta milestone.
