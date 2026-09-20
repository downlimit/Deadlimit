# Changelog

Deadlimit is delivered from `main` as a Git checkout. The installer and updater
keep that checkout current and rebuild the Manager locally. Numbered tags remain
historical source milestones. Dates use `YYYY-MM-DD`.

## Unreleased

### Changed

- The one-file installer uses Git for Windows and the .NET 10 SDK, asks before
  installing either missing dependency through WinGet, then clones `main` and
  builds Deadlimit Manager locally.
- Every supported installation now updates through the existing Git
  fast-forward/rebuild path.
- The portable ZIP, `latest-main` rolling release channel, package checksums,
  package rollback payload, and package-specific release policy were retired.
- CI no longer uploads the identifier-audit artifact or publishes Deadlimit
  release assets after every merge.
- User settings and caches use the centralized `%LocalAppData%\Deadlimit`
  location for all supported installations.
- Settings shows Deadlimit Manager as the first tool row, including the current
  `main` commit status and a contextual check/update action.
- Runtime tool defaults derive from the current application location and Steam
  discovery instead of maintainer-workstation drive paths.
- Obsolete `DeadlimitAggregator*` compatibility entry points remain retired;
  `Deadlimit.cmd` stays as the neutral legacy shim.

### Added

- MIT licensing and DCO-based contribution policy.
- English and Russian public project guides.
- Community health, support, security, ownership, issue, and pull-request files.
- Dependency, external-tool, compatibility, and network trust documentation.
- CI policy rejecting retail game resources, extracted content paths,
  third-party binary/archive formats, and unexpected large files.

### Security

- External CSDK, DepotDownloader, and DeadlockTools operations remain explicit
  user actions and are not distributed as part of Deadlimit.
- Final 822-commit history scan found no configured high-confidence secret
  signatures or prohibited historical asset paths on 2026-09-05.

## 0.1.0-beta.1

First public beta milestone.
