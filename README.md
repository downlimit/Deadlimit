# Deadlimit

Deadlimit is a free, open-source Windows toolset that removes repetitive steps
from the Deadlock character-replacement authoring workflow.

It connects an artist-owned project folder, DCC authoring/export, Reduced CSDK
authoring, resource compilation, VPK packaging, and local game deployment. The
project is hobby software maintained on a best-effort basis and may need updates
whenever Deadlock or an external tool changes.

> Deadlimit is installed from the public Git repository. Git for Windows and
> the .NET 10 SDK are required; the installer can install missing copies through
> Windows Package Manager (WinGet) after asking for permission.

[Русская версия](README.ru.md)

## Components

- **Deadlimit Manager** — the main Windows desktop application for projects,
  source extraction, CSDK preparation, live synchronization, build, packaging,
  and local deployment.
- **Deadlimit Scripts** (**Deadlimit Pipeline Scripts**) — DCC-side authoring and export helpers for the DMX, Vertex Color, and Deadlock/Source 2 pipeline. The current bundled module is MAXScript-based; Blender support is planned under the same product. The implementation retains the established `DeadlimitPipelineScripts.ms` identifiers for compatibility.
- **Deadlimit Shade** — experimental Substance 3D Painter shader and preset work.

The current bundled Deadlimit Scripts module is MAXScript-based; Blender support is planned under the same product. Deadlimit Shade remains experimental.

## Tested environment

- Windows 11 x64
- Deadlimit Scripts MAXScript host: 2025
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

   The CSDK, DepotDownloader, and DeadlockTools actions are available with the
   same explicit, user-initiated behavior in Git, installed, and manually
   extracted copies.
4. Create or open a Deadlimit project whose root contains artist DMX, FBX,
   glTF/GLB model files and matching texture sources. Use one model format for
   each retail render-mesh target.
5. Run **PREPARE FOR CSDK** once, then launch CSDK for ModelDoc and material work.
   Hold `SHIFT` while clicking **PREPARE FOR CSDK** to open optional preparation
   tasks, including creation of an editable hero-select VMAP from the selected
   hero's CSDK prefab. Existing VMAP edits are preserved.
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

`1authoring` is the recursive artist-owned handoff area. Deadlimit stores metadata and
logs under its hidden `.deadlimit` folder and generates CSDK/game outputs in the
configured local environments. Extracted `0source`, retail resources, compiled
Source 2 files, and deployed VPKs are local user content and must never be
committed to this repository or attached to its issues.

## Development

```powershell
dotnet restore internal/src/Deadlimit/Deadlimit.csproj
dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore
internal/tests/open-source-content-policy-smoke.ps1
internal/tests/path-defaults-smoke.ps1
internal/tests/prepare-behavior-smoke.ps1
```

The full Windows CI contract is in [`.github/workflows/build.yml`](.github/workflows/build.yml).
See [CONTRIBUTING.md](CONTRIBUTING.md) for the fork/PR workflow and required DCO
sign-off.

## Install for artists

1. Download the single
   [`Install-Deadlimit.cmd`](https://raw.githubusercontent.com/downlimit/Deadlimit/main/Install-Deadlimit.cmd)
   file from the official repository and run it.
2. If Git for Windows or the .NET 10 SDK is missing, the installer lists the
   missing dependencies and asks permission to install them through WinGet.
3. The installer clones `main` into `%LocalAppData%\Programs\Deadlimit`,
   builds Deadlimit Manager locally, creates Manager/Updater shortcuts on the
   Desktop and in the Start menu, then launches the Manager.

The supported installation is a normal Git checkout. No Deadlimit ZIP, portable
package, package checksum, or rolling `latest-main` release is involved.
Settings and caches are stored under `%LocalAppData%\Deadlimit`; artist
projects remain wherever the user chooses to keep them.

If the old package-based installation exists at the standard install path, the
installer performs a one-time migration: it clones a fresh Git checkout, copies
the previous in-folder `UserData` into the centralized user-data directory, and
removes the retired package payload only after the clone succeeds.

## Updating Deadlimit

`Update Deadlimit.cmd` and the Settings update action use the same Git path:
fetch `origin/main`, refuse an unsafe overlap with local tracked edits,
fast-forward the checkout, rebuild Deadlimit Manager with the installed .NET 10
SDK, and relaunch when appropriate.

Early Deadlimit files are unsigned, so Windows may show a reputation warning.
Run the installer only when it was downloaded from the official
`downlimit/Deadlimit` repository.

## License, support, and independence

Deadlimit source code is distributed under the [MIT License](LICENSE). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency and external-tool
notices, [SUPPORT.md](SUPPORT.md) for issue scope, and [SECURITY.md](SECURITY.md)
for private vulnerability reporting.

Deadlimit interoperates with user-installed third-party tools and local content;
it does not distribute them. This is an independent community project with no
affiliation, sponsorship, endorsement, or approval from Valve, Autodesk, Adobe,
Wall Worm, or the maintainers of the other tools it can invoke.

The readiness record is
[`internal/docs/OPEN_SOURCE_PLAN.md`](internal/docs/OPEN_SOURCE_PLAN.md).
