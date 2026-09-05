# Changelog

Artist builds are published continuously from successful `main` commits. The
rolling package uses a build identity; numbered tags remain historical source
milestones. Dates use `YYYY-MM-DD`.

## Unreleased

### Changed

- Successful merges to `main` now replace one rolling `latest-main` artist
  package automatically. The permanent installer and in-app updater both use
  that channel, removing manual numbered releases from routine delivery.
- Settings now shows Deadlimit Manager as the first tool row, including the
  current version, update status, and a contextual check/update action.
- Artist installations keep settings and caches under local `UserData`, update
  in place, and preserve the prior program payload under local `Backup`.
- The one-file installer and in-app updater consume the same verified package.
- GitHub workflows now use the current Node 24-based major versions of the
  official checkout, .NET setup, and artifact upload actions.
- Runtime tool defaults now derive from the current application location and
  Steam discovery instead of maintainer-workstation drive paths.
- Obsolete `DeadlimitAggregator*` compatibility entry points were retired;
  `Deadlimit.cmd` remains as the neutral legacy shim.

### Added

- MIT licensing and DCO-based contribution policy.
- English and Russian public project guides.
- Community health, support, security, ownership, issue, and pull-request files.
- Dependency, external-tool, compatibility, and network trust documentation.
- CI policy rejecting retail game resources, extracted content paths,
  third-party binary/archive formats, and unexpected large files.

### Security

- External CSDK, DepotDownloader, and DeadlockTools operations remain explicit
  user actions and are never bundled into the Deadlimit package.
- Final 822-commit history scan found no configured high-confidence secret
  signatures or prohibited historical asset paths on 2026-09-05.

## 0.1.0-beta.1

First public beta milestone.
