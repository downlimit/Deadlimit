# Network and External-Execution Trust Audit

Status: **AUDITED — external-tool actions are explicit and user-initiated**

Last reviewed: 2026-09-20

This document inventories Deadlimit-controlled downloads and the points where
downloaded or user-installed executables can run. It describes the current
implementation; it does not endorse or grant rights to any external content.

## Current download paths

| Flow | Source selected by Deadlimit | Destination / execution | Current integrity evidence | Required public-release mitigation |
| --- | --- | --- | --- | --- |
| Deadlimit installer/updater | `https://github.com/downlimit/Deadlimit.git`, branch `main` | The one-file installer creates a Git checkout under the user profile and builds locally; the updater performs a guarded fast-forward and rebuild | HTTPS plus Git object integrity; installer verifies the expected repository origin and requires Git for Windows plus .NET 10 SDK | This is the single supported Deadlimit delivery path. |
| Reduced CSDK archive | Pinned CSDK 12 page plus pinned Google Drive file ID `1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS` | ZIP contents copied into the user-selected CSDK root | Deadlimit no longer discovers arbitrary future CSDK generations or archive IDs from mutable page HTML. HTTPS, HTML-response rejection, contained ZIP extraction, and expected `csdkcfg.exe` presence remain in force. The upstream archive still has no independently published authenticated checksum. | Keep CSDK12 explicit and interactive. Add a maintainer-reviewed expected archive SHA-256 if/when the upstream project publishes or the maintainer independently records a canonical digest. Never ship the archive. |
| Depot manifests | Pinned CSDK12 app/depot/manifest IDs; pinned CSDK12 fallback archive URL | IDs are passed to DepotDownloader; fallback ZIP is applied only when the pinned depot request requires it | Mutable page HTML no longer controls depot arguments. ZIP extraction is contained. The fallback manifest archive still lacks an independently authenticated checksum. | Keep the fallback explicit in diagnostics and pin an expected digest when an upstream digest becomes available. |
| DepotDownloader | Pinned `SteamRE/DepotDownloader` release `DepotDownloader_3.4.0`, asset `DepotDownloader-windows-x64.zip` | Cached under `%LocalAppData%\Deadlimit\tools\DepotDownloader`, then run interactively for Steam authentication/download | Exact release/tag and asset plus expected SHA-256 `41C9E9F0DF54B3AD02E67A11726756E5C73283BD7C2E1B04ACFA5AE4C2ED3767`; hash is verified while downloading before extraction/execution | Update the reviewed tag and digest together in a Deadlimit change. |
| DeadlockTools managed release | Pinned `dotryen/DeadlockTools` release `v1.1.0`, asset `DeadlockTools-windows-x64.zip` | Installed in a user-selected location and invoked by build workflows | Exact release/tag and asset plus expected SHA-256 `7E4668DA796E4CA67B1EE684CF03270E07FECEBECCF66D04DDF1F3A3E7409DCF`; hash is verified while downloading before extraction/execution | Update the reviewed tag and digest together in a Deadlimit change. |
| DeadlockTools developer checkout | `https://github.com/dotryen/DeadlockTools.git` or an existing checkout | Git clone/pull and local `dotnet build` | Git commit identity and HTTPS transport; tracks mutable `master` | Keep this path explicitly developer-oriented, record the resolved commit, and avoid using it for stable portable installs. |

## Existing safety controls

- Downloads use HTTPS and a 30-minute HTTP timeout.
- ZIP and VPK entry paths pass through `SafePath.ResolveUnderRoot`, preventing
  lexical traversal outside the declared extraction root.
- Temporary download folders are removed on completion or failure when possible.
- Downloads returning an HTML content type are rejected before extraction.
- The Deadlimit updater changes only a Git checkout. It refuses an incoming
  update that overlaps local tracked edits and performs no automatic stash,
  reset, or overwrite of those edits. It never force-kills Deadlimit Manager;
  an in-app update exits the Manager normally and the updater waits for that
  process to terminate before changing the checkout.
- CSDK, DepotDownloader, and DeadlockTools install/update actions require an
  explicit user click and show their current source context.
- CSDK setup validates the selected retail installation but writes full-game
  depot output into the separate user-selected CSDK root.
- DepotDownloader runs visibly when Steam authentication may be required.
- Retail hero extraction reads locally installed VPK files; Deadlimit does not
  upload them.

## Unresolved trust gaps

DeadlockTools and DepotDownloader managed binaries are pinned to reviewed
immutable release identities and expected SHA-256 values.

Reduced CSDK12 remains the exception: its Google Drive file ID and required
depot manifest IDs are pinned in Deadlimit, so mutable community-page HTML can
no longer redirect the install or change DepotDownloader arguments. The CSDK12
ZIP and optional manifest-fallback ZIP do not currently have an independently
published authenticated checksum. Deadlimit therefore cannot cryptographically
authenticate a first-time CSDK download against an external maintainer digest.
The action remains explicit and user-initiated, and a future reviewed digest
should be added as soon as one is available.

## Redistribution boundary

The Deadlimit repository must contain none of the downloaded archives,
extracted Valve/Deadlock depot files, Reduced CSDK files, or external
executables. The repository content-policy smoke test enforces common filenames,
extensions, and game-tree paths.

## Trust hardening

Managed DeadlockTools and DepotDownloader downloads are pinned and hashed.
Reduced CSDK12 is pinned by generation, page, Drive file ID and depot manifests;
the remaining hardening item is an independently reviewed expected checksum for
the CSDK12 archive and its optional manifest fallback.
