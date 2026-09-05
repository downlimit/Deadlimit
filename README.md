# Deadlimit

Deadlimit is a free, open-source Windows toolset that removes repetitive steps
from the Deadlock character-replacement authoring workflow.

It connects an artist-owned project folder, DCC authoring/export, Reduced CSDK
authoring, resource compilation, VPK packaging, and local game deployment. The
project is hobby software maintained on a best-effort basis and may need updates
whenever Deadlock or an external tool changes.

> Public-readiness work is in progress. The portable ZIP and stable
> release updater described in the roadmap are not published yet. Current users
> run Deadlimit from a Git clone and need the .NET 10 SDK.

[Русская версия](README.ru.md)

## Components

- **Deadlimit Manager** — the main Windows desktop application for projects,
  source extraction, CSDK preparation, live synchronization, build, packaging,
  and local deployment.
- **Deadlimit Scripts** (**Deadlimit Pipeline Scripts**) — DCC-side authoring and export helpers for the DMX, Vertex Color, and Deadlock/Source 2 pipeline. The current bundled module is MAXScript-based; Blender support is planned under the same product. The implementation retains the established
  `DeadlimitPipelineScripts.ms` identifiers for compatibility.
- **Deadlimit Shade** — experimental Substance 3D Painter shader and preset work.

Blender is planned but unsupported. Shade is experimental; Manager and the Max
Script are the current project focus.

## Tested environment

- Windows 11 x64
- Autodesk tooling used by the current MAXScript module 2025
- Wall Worm 7
- Reduced CSDK 12
- a current Steam installation of Deadlock
- .NET 10 SDK for the current clone-based launcher

Exact external-tool and game snapshots must be recorded for each release. See
[COMPATIBILITY.md](COMPATIBILITY.md) before assuming another version works.

Deadlimit does not install or grant access to commercial software or retail game
content. Obtain every required tool and account through its owner and follow its
license and terms.

## Five-minute start from a clone

1. Install Git and the .NET 10 SDK.
2. Clone the repository and start the root launcher:

   ```powershell
   git clone https://github.com/downlimit/Deadlimit.git
   Set-Location Deadlimit
   .\DeadlimitManager.cmd
   ```

3. Open **Settings** and configure your retail Deadlock, Reduced CSDK, and other
   tool locations. Automatic dependency actions download third-party files into
   local tool folders; review the displayed source before using them.

   Portable releases accept existing tool locations through **Browse**. Their
   automatic CSDK, DepotDownloader, and DeadlockTools install/update actions
   remain disabled until those upstream archives have release-pinned trusted
   checksums. The clone-based Developer channel retains the opt-in automation.
4. Create or open a Deadlimit project whose root contains the artist DMX files
   and matching texture sources.
5. Run **PREPARE FOR CSDK** once, then launch CSDK for ModelDoc and material work.
6. Use **BUILD & TEST** to prepare current inputs, compile changed resources,
   restore required model data, package a VPK, and deploy it to the configured
   local Deadlock addons slot. Launch the game separately.

Deadlock locks a loaded VPK. If the client is running during deployment,
Deadlimit asks to close it before replacing the archive.

## ONLINE CSDK iteration

Hold **SHIFT** while clicking **LAUNCH CSDK** to run the preserving preparation,
enable ONLINE CSDK synchronization, and launch CSDK. Repeat the gesture to stop
synchronization without opening another CSDK instance.

While online mode is active, supported project-root DMX, texture, and required
`*_vertexcolor.fbx` changes are synchronized automatically. Structural or
material-reference changes trigger a full preserving PREPARE transaction while
CSDK stays open. Manual **PREPARE FOR CSDK** is a recovery step only when the UI
reports that an automatic transaction failed and kept the last good content.

The Vertex Color sidecar wait applies only to a renderable DMX mesh assigned to
a material whose name contains `vertexcolor` (case-insensitive) and whose
embedded color is unavailable. Unrelated DMX files do not wait for an FBX pair.

## Project and generated content boundary

The project root is the artist-owned handoff area. Deadlimit stores metadata and
logs under its hidden `.deadlimit` folder and generates CSDK/game outputs in the
configured local environments. Extracted `0source`, retail resources, compiled
Source 2 files, and deployed VPKs are local user content and must never be
committed to this repository or attached to its issues.

## Development

```powershell
dotnet restore internal/src/Deadlimit/Deadlimit.csproj
dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore
internal/tests/open-source-content-policy-smoke.ps1
internal/tests/portable-path-defaults-smoke.ps1
internal/tests/prepare-behavior-smoke.ps1
```

The full Windows CI contract is in [`.github/workflows/build.yml`](.github/workflows/build.yml).
See [CONTRIBUTING.md](CONTRIBUTING.md) for the fork/PR workflow and required DCO
sign-off.

## Portable user release

After the first public release, an artist downloads `Deadlimit-win-x64.zip`
and its `.sha256` file from the official GitHub Release, verifies the checksum,
extracts the ZIP into a permanent writable folder, and runs
`DeadlimitManager.exe`. Running directly from the opened ZIP is unsupported.

The release is self-contained: Git and the .NET SDK are unnecessary. Deadlimit
creates no Start-menu/Desktop shortcuts and registers no Windows installer or
uninstaller. Portable settings and caches stay under `UserData` inside the
extracted folder. Artist projects remain wherever the user chooses to keep them.

`Update Deadlimit.cmd` downloads the latest published ZIP, verifies SHA-256,
updates the program files in place, preserves `UserData`, and keeps the previous
program payload under `Backup`. `Update Deadlimit.cmd -Rollback` swaps the
current and backup program payloads while leaving user data in place. Deleting
the extracted Deadlimit folder removes the application, its settings, cache,
and rollback payload.

The Settings window exposes the same `UPDATE DEADLIMIT` action in both release
channels. A portable build opens its in-folder updater; a Git checkout opens the
repository updater.

## Updating the current clone

`Deadlimit Updater` is currently the developer/Git channel: it fetches
`origin/main`, preserves unrelated local work when it can fast-forward safely,
and rebuilds the Manager. The portable user channel is implemented but has not
been published. These channels stay separate so a normal user update never
depends on a mutable source checkout.

Early portable beta executables are expected to be unsigned, so Windows
SmartScreen may display an unknown-publisher warning. Continue only when the
ZIP came from the official `downlimit/Deadlimit` GitHub Release
and its published SHA-256 checksum matches. Report any checksum mismatch and do
not run the downloaded file.

## License, support, and independence

Deadlimit source code is distributed under the [MIT License](LICENSE). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency and external-tool
notices, [SUPPORT.md](SUPPORT.md) for issue scope, and [SECURITY.md](SECURITY.md)
for private vulnerability reporting.

Deadlimit interoperates with user-installed third-party tools and local content;
it does not distribute them. This is an independent community project with no
affiliation, sponsorship, endorsement, or approval from Valve, Autodesk, Adobe,
Wall Worm, or the maintainers of the other tools it can invoke.

The live readiness plan is
[`internal/docs/OPEN_SOURCE_PLAN.md`](internal/docs/OPEN_SOURCE_PLAN.md). Changing
the repository to public and publishing the first portable release remain
explicit maintainer approval gates.
