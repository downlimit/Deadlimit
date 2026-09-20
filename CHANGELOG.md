# Changelog

Deadlimit is delivered from `main` as a Git checkout. The installer and updater
keep that checkout current and rebuild Deadlimit Manager locally. Numbered
releases mark source milestones. Dates use `YYYY-MM-DD`.

## Unreleased

No user-facing changes recorded yet.

## 0.1.0-beta.3 - 2026-09-20

### Changed

- The one-file installer installs Deadlimit into a `Deadlimit` folder next to
  the installer itself.
- If Git for Windows or the .NET 10 SDK is missing, the installer explains what
  is required and asks before installing it through WinGet.
- Supported installations track `origin/main` and update through the guarded
  Git fast-forward/rebuild path.
- Numbered GitHub releases are source milestones. Their only user-facing
  downloadable assets are `Install-Deadlimit.cmd` and its SHA-256 checksum.
- User settings and caches use the centralized `%LocalAppData%\Deadlimit`
  location.
- Deadlimit Manager exposes the current `main` revision and update state in
  Settings.
- Public documentation was rebuilt around the artist workflow and now includes
  English, Russian, Simplified Chinese, and Brazilian Portuguese landing pages.
- The public product naming is standardized as **Deadlimit Manager**,
  **Deadlimit Scripts**, and **Deadlimit Shade**.
- The active CSDK synchronization mode is named **LIVE SYNC** throughout the
  user interface and documentation.

### Added

- MIT licensing and DCO-based contribution policy.
- Community health, support, security, ownership, issue, and pull-request files.
- Dependency, external-tool, compatibility, and network trust documentation.
- CI policy rejecting retail game resources, extracted content paths,
  third-party binary/archive formats, and unexpected large files.

### Security

- External CSDK, DepotDownloader, and DeadlockTools operations remain explicit
  user actions and are not distributed as part of Deadlimit.
