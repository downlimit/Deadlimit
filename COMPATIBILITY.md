# Compatibility

Deadlimit depends on rapidly changing external software. Compatibility claims
apply only to the snapshots recorded below and in each release note.

## Current development baseline

| Component | Baseline | Status | Evidence limitation |
| --- | --- | --- | --- |
| Operating system | Windows 11 Home 25H2 x64, build `26200.8894` | Supported and tested | Windows 10 is currently untested. |
| .NET | SDK `10.0.400` | Required for clone-based development/launch | Portable `win-x64` rehearsal builds include a self-contained runtime. |
| Deadlimit Scripts MAXScript host | 2025, `27.3.0.30874` | Supported and tested | Other Max versions are untested. |
| Wall Worm | `7.36.2` | Snapshot-tested | Version read from the installed Wall Worm configuration; other builds remain untested. |
| Reduced CSDK | Generation 12; `csdkcfg.exe` `0.1.0` | Snapshot-tested locally | Local binaries are fingerprinted below; the mutable upstream archive still lacks an authenticated release hash. |
| Deadlock | Steam app `1422450`, build `24882156` | Snapshot-tested | Installed depot manifests are recorded below. |
| ValveResourceFormat / Source 2 Viewer library | NuGet 20.0.6980 | Build dependency | Revalidate extraction after package or game-format changes. |
| DeadlockTools | release `v1.1.0`, product commit `ed8eda954f63dde4869b57b8976f9e873fe19187` | Snapshot-tested | Installed executable is fingerprinted below; the upstream ZIP still needs an authenticated release hash. |
| Substance 3D Painter / Deadlimit Shade | Painter `9.1.0`; Shade research snapshot only | Experimental | No distributable Shade shader or preset exists yet. |
| Blender | — | Unsupported / planned | No Blender pipeline exists yet. |
| Linux and macOS | — | Unsupported | The Manager is a Windows desktop application. |

## Local binary and depot fingerprint — 2026-09-05

These values identify the workstation snapshot used for the current audit. They
do not authenticate the mutable upstream downloads.

| Component | SHA-256 / manifest |
| --- | --- |
| Reduced CSDK `csdkcfg.exe` | `8C347D3BB67863E37F64809CDE49F1A770BF9CB7478D1AE2C72BAD4C9D63F34A` |
| Reduced CSDK `resourcecompiler.exe` (`game\bin_cs2\win64`) | `A0D3B34EC619C4A122D82CE2A8FF1517D6320519A5564923A607CE341EBC4201` |
| Reduced CSDK `CSDKCfgVPK.exe` | `15EC6287918B6A7B64C62AE4D0278C9978F81ED1CFACF2DEC2317ED3BD798954` |
| DeadlockTools `DeadlockTools.exe` | `0A7D29BEBC20A6FE004CE075A7891B19F1BC4BA2A9E0DAD5C861EC646699AF27` |
| Deadlock depot `1422451` | manifest `5812102072245947064` |
| Deadlock depot `1422452` | manifest `5497551246774317622` |
| Deadlock depot `1422456` | manifest `4428960194428638747` |

## Release rule

Every published build must refresh these exact versions, commit or manifest
identifiers, and local fingerprints. Automatic download trust additionally
requires expected SHA-256 values for the upstream executable archives.
“Latest” is not a durable compatibility claim.

When reporting a bug, include the Deadlimit tag/commit and all relevant external
versions. An upstream update can be the cause even when the same project worked
with an earlier snapshot.
