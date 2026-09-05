# Network and External-Execution Trust Audit

Status: **AUDITED — external-tool actions are explicit and user-initiated**

Last reviewed: 2026-09-04

This document inventories Deadlimit-controlled downloads and the points where
downloaded or user-installed executables can run. It describes the current
implementation; it does not endorse or grant rights to any external content.

## Current download paths

| Flow | Source selected by Deadlimit | Destination / execution | Current integrity evidence | Required public-release mitigation |
| --- | --- | --- | --- | --- |
| Developer repository updater | `https://github.com/downlimit/Deadlimit.git`, branch `main` | Fast-forward of a developer Git checkout, then local build | HTTPS plus Git object integrity; no signed release boundary | Label this as the Developer channel. Build the user updater on immutable GitHub Releases with a published SHA-256. |
| Installer, portable ZIP, and release updater | GitHub Releases API for `downlimit/Deadlimit`; exact ZIP and `.sha256` asset names | The one-file installer places the same ZIP payload under the user profile and creates shortcuts; manual extraction runs it anywhere; the shared updater preserves local `UserData` | HTTPS, source-host allowlist, SHA-256 before updater extraction and package activation, contained ZIP extraction, file manifest, local `Backup` rollback | Keep release publication owner-only and publish the installer plus ZIP/checksum together with the version tag. |
| Reduced CSDK discovery and archive | `https://deadlockmodding.pages.dev/modding-tools/`, a discovered `csdk-N` page, then a Google Drive download | ZIP contents copied into the user-selected CSDK root | HTTPS, HTML-response rejection, contained ZIP extraction, and expected `csdkcfg.exe` presence; no authenticated checksum | Keep the action explicit and interactive. Show the source/generation before download, compute and record SHA-256 and source URL, and verify against an allowlisted release manifest when one is available. Never ship the archive. |
| Depot manifests | Parsed from the selected community CSDK page; optional relative `DepotDownloaderManifests.zip` fallback | Parsed IDs are passed to DepotDownloader; fallback ZIP is applied to the selected CSDK root | HTTPS and contained ZIP extraction; page contents are mutable and no checksum is verified | Restrict redirects/hosts, record all app/depot/manifest IDs, hash the fallback archive, and include them in diagnostics before execution. |
| DepotDownloader | GitHub API latest release for `SteamRE/DepotDownloader`, asset `DepotDownloader-windows-x64.zip` | Cached under `%LocalAppData%\Deadlimit\tools\DepotDownloader`, then run interactively for Steam authentication/download | HTTPS, exact asset-name selection, contained ZIP extraction, and expected executable presence; no version pin or checksum | Pin a reviewed version and SHA-256 in each Deadlimit release. Display that an external executable will run and preserve authentication inside its own visible console. |
| DeadlockTools managed release | GitHub API latest release for `dotryen/DeadlockTools`, asset `DeadlockTools-windows-x64.zip` | Installed in a user-selected location and invoked by build workflows | HTTPS, exact asset-name selection, contained ZIP extraction, expected executable presence, and a local tag/source marker; no checksum | Pin a reviewed tag and SHA-256, verify before extraction, store the computed hash in the marker, and show source/version in the UI. |
| DeadlockTools developer checkout | `https://github.com/dotryen/DeadlockTools.git` or an existing checkout | Git clone/pull and local `dotnet build` | Git commit identity and HTTPS transport; tracks mutable `master` | Keep this path explicitly developer-oriented, record the resolved commit, and avoid using it for stable portable installs. |

## Existing safety controls

- Downloads use HTTPS and a 30-minute HTTP timeout.
- ZIP and VPK entry paths pass through `SafePath.ResolveUnderRoot`, preventing
  lexical traversal outside the declared extraction root.
- Temporary download folders are removed on completion or failure when possible.
- Downloads returning an HTML content type are rejected before extraction.
- The updater shipped inside the portable ZIP verifies the next ZIP before
  extraction; a malformed checksum, traversal entry, missing Manager, reserved
  data path, or missing release metadata stops before the current payload is
  replaced. The installer extracts its worker only from that verified ZIP;
  manual portable extraction creates no shortcuts or Windows installer state.
- CSDK, DepotDownloader, and DeadlockTools install/update actions require an
  explicit user click and show their current source context. Their availability
  is identical in Git, installed, and manually extracted copies.
- CSDK setup validates the selected retail installation but writes full-game
  depot output into the separate user-selected CSDK root.
- DepotDownloader runs visibly when Steam authentication may be required.
- Retail hero extraction reads locally installed VPK files; Deadlimit does not
  upload them.

## Unresolved trust gaps

The CSDK, manifest fallback, DepotDownloader, and DeadlockTools archives are
currently accepted without an expected checksum or signature. A SHA-256
computed only after download is useful for diagnostics and repeatability but
does not authenticate a mutable upstream asset. Stable releases therefore need
a maintainer-reviewed immutable version plus an expected hash.

The community CSDK page controls archive location and depot manifest arguments.
Its content can change independently of Deadlimit. Before public release the UI
must show this boundary, store the resolved page, generation, manifest IDs,
download source, and hashes, and require an explicit user action. Failures in
Steam authentication or upstream access must stop without bypass behavior.

## Redistribution boundary

Deadlimit source and release archives must contain none of the downloaded
archives, extracted Valve/Deadlock depot files, Reduced CSDK files, or external
executables. The repository content-policy smoke test enforces common filenames,
extensions, and game-tree paths. The final release job must apply the same test
to the produced portable ZIP.

## Release gate

The public repository and binary package document these integrations as
explicit user-initiated operations. Deadlimit releases use versioned tag inputs,
package checksums, and a manifest. Pinning reviewed upstream versions and hashes
remains the preferred future hardening for external tool downloads.
