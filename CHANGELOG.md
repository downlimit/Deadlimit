# Changelog

Deadlimit follows [Semantic Versioning](https://semver.org/) beginning with the
planned `0.1.0-beta.1` public beta. Dates use `YYYY-MM-DD`.

## Unreleased

### Changed

- Portable releases now run directly from an extracted folder, keep settings
  and caches under their local `UserData`, update in place, preserve the prior
  program payload under local `Backup`, and create no automatic shortcuts or
  Windows installer state.
- GitHub workflows now use the current Node 24-based major versions of the
  official checkout, .NET setup, and artifact upload actions.
- Runtime tool defaults now derive from the current clone/portable location and
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

- Portable releases disable automatic CSDK, DepotDownloader, and DeadlockTools
  installation/update until upstream archives have release-pinned trusted
  checksums; users can select existing installations in Settings.
- Final 822-commit history scan found no configured high-confidence secret
  signatures or prohibited historical asset paths on 2026-09-05.

## 0.1.0-beta.1

Unreleased. This section will be populated by the private release rehearsal.
