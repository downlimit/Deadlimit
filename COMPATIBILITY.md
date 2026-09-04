# Compatibility

Deadlimit depends on rapidly changing external software. Compatibility claims
apply only to the snapshots recorded below and in each release note.

## Current development baseline

| Component | Baseline | Status | Evidence limitation |
| --- | --- | --- | --- |
| Operating system | Windows 11 x64 | Supported and tested | Windows 10 is currently untested. |
| .NET | .NET 10 SDK | Required for clone-based development/launch | Portable `win-x64` rehearsal builds include a self-contained runtime. |
| Autodesk 3ds Max | 2025 | Supported and tested | Other Max versions are untested. |
| Wall Worm | Major version 7, current build used on 2026-09-04 | Provisionally supported | The exact plugin build must be captured before the first release. |
| Reduced CSDK | Generation 12, current archive used on 2026-09-04 | Provisionally supported | The current installer discovers a mutable community archive; immutable version/hash capture is a release gate. |
| Deadlock | Steam build installed and tested on 2026-09-04 | Snapshot-tested | Exact app/depot/manifest IDs must be captured by release tooling. |
| ValveResourceFormat / Source 2 Viewer library | NuGet 20.0.6980 | Build dependency | Revalidate extraction after package or game-format changes. |
| DeadlockTools | Current upstream release/checkout | Provisionally supported | Exact tag/commit and archive hash must be captured before release. |
| Substance 3D Painter / Deadlimit Shade | Maintainer workstation version | Experimental | Exact version and repeatable installation remain to be documented. |
| Blender | — | Unsupported / planned | No Blender pipeline exists yet. |
| Linux and macOS | — | Unsupported | The Manager is a Windows desktop application. |

## Release rule

Every published build must replace provisional entries with exact versions,
commit or manifest identifiers where available, download origins, and expected
SHA-256 values for executable archives. “Latest” is not a durable compatibility
claim.

When reporting a bug, include the Deadlimit tag/commit and all relevant external
versions. An upstream update can be the cause even when the same project worked
with an earlier snapshot.
