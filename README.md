# Deadlimit

**Deadlock character-replacement modding, with less file management and fewer manual rebuild steps.**

Deadlimit is a free, open-source Windows toolset for artists building character-replacement mods for Deadlock. It connects project files, DCC export, Reduced CSDK authoring, Source 2 compilation, VPK packaging, and local deployment into one workflow.

It does not make the model, textures, or design decisions. It does handle a large amount of the part where files need to be in exactly the right place.

[Русская версия](README.ru.md)

## Start here

### Install Deadlimit

Download the permanent [`Install-Deadlimit.cmd`](https://github.com/downlimit/Deadlimit/releases/download/latest-main/Install-Deadlimit.cmd) file and run it.

The installer downloads the newest successful `main` build, verifies it, installs Deadlimit under `%LocalAppData%\Programs\Deadlimit`, creates Manager and Updater shortcuts, and launches `DeadlimitManager.exe`.

**Git and the .NET SDK are not required for the artist installation.**

Early Deadlimit builds are unsigned, so Windows SmartScreen may show an unknown-publisher warning. Continue only when the installer came from the official `downlimit/Deadlimit` release and its published SHA-256 checksum matches.

### What you need

The currently tested workflow uses:

- Windows 11 x64;
- a current Steam installation of Deadlock;
- Reduced CSDK 12;
- Deadlimit Scripts in 3ds Max 2025;
- Wall Worm 7.36.2.

Exact tested versions, game builds, tool snapshots, and known limitations live in [COMPATIBILITY.md](COMPATIBILITY.md). Deadlock and its external tools change frequently, so this page avoids pretending that “latest” is a version number.

Deadlimit does not provide commercial software, retail game content, or access to third-party services. Obtain required tools through their owners and follow their licenses and terms.

## What Deadlimit does

Deadlimit Manager covers the repetitive parts of the character-replacement workflow:

- keeps artist projects in a project library;
- extracts supported source data;
- prepares project files for Reduced CSDK;
- keeps supported files synchronized during CSDK work;
- compiles changed Source 2 resources;
- restores model data required by the replacement pipeline;
- packages the result into a VPK;
- deploys it to the configured local Deadlock addons slot;
- checks for Deadlimit updates and supports rollback.

The practical result is simple: you spend more time changing the mod and less time repeating the same file operations between each test.

## The three Deadlimit products

### Deadlimit Manager

The main Windows application. It manages projects, source extraction, CSDK preparation, synchronization, build, packaging, deployment, updates, and local tool paths.

### Deadlimit Scripts

**Deadlimit Scripts** — also referred to as **Deadlimit Pipeline Scripts** — contains DCC-side authoring and export helpers for the DMX, Vertex Color, and Deadlock/Source 2 pipeline.

The current bundled module is MAXScript-based. Blender support is planned under the same product name. The technical `DeadlimitPipelineScripts.ms` identifier remains in the codebase for compatibility.

### Deadlimit Shade

Experimental Substance 3D Painter shader and preset work. It is part of the Deadlimit project, but it is not yet a distributable production tool.

Three products, one project, and currently only one of them asks for 3ds Max.

## Your first project

1. Install and open **Deadlimit Manager**.
2. Open **Settings** and configure your retail Deadlock, Reduced CSDK, and other required tool locations. You can point Deadlimit at existing installations; where a download action is available, it is explicitly user-initiated.
3. Create or open a Deadlimit project. Its root is the artist-owned working area for DMX files and matching texture sources.
4. Run **PREPARE FOR CSDK** once.
5. Launch CSDK and continue the normal ModelDoc and material work there.
6. Use **BUILD & TEST** when you want a playable build. Deadlimit prepares current inputs, compiles changed resources, restores required model data, packages the VPK, and deploys it to the configured local addons slot.
7. Launch Deadlock separately and test the result.

Deadlock locks a loaded VPK. If the client is running when Deadlimit needs to replace that archive, Deadlimit asks you to close the game first. This is expected behavior, even if the timing is usually impeccable.

## Faster iteration: ONLINE CSDK

Hold **SHIFT** while clicking **LAUNCH CSDK** to run the preserving preparation, enable ONLINE CSDK synchronization, and launch CSDK. Repeat the same gesture to stop synchronization without opening another CSDK instance.

While ONLINE CSDK is active, supported project-root DMX files, textures, and required `*_vertexcolor.fbx` changes are synchronized automatically. Structural changes or material-reference changes trigger a full preserving PREPARE transaction while CSDK stays open.

Manual **PREPARE FOR CSDK** becomes a recovery action when the UI reports that an automatic transaction failed and kept the last good content.

SHIFT has one more job now.

### Vertex Color sidecars

The Vertex Color sidecar wait applies only to a renderable DMX mesh assigned to a material whose name contains `vertexcolor` case-insensitively and whose embedded color is unavailable. Unrelated DMX files do not wait for a paired FBX.

This rule exists so a freshly exported DMX does not outrun the Vertex Color file that is supposed to accompany it.

## Where your files live

The **project root** is the artist-owned handoff area. Deadlimit keeps its project metadata and logs under the hidden `.deadlimit` folder.

Generated CSDK and game outputs are written into the configured local environments rather than back into the repository.

Do not commit or attach extracted `0source`, retail resources, compiled Source 2 files, or deployed VPKs to this repository or its issues.

## Updates and rollback

Every accepted merge to `main` is built and published to the rolling `latest-main` channel after CI succeeds.

`Update Deadlimit.cmd` downloads the latest verified package, updates the program files in place, preserves `UserData`, and keeps the previous program payload under `Backup`. `Update Deadlimit.cmd -Rollback` swaps the current and backup payloads without replacing user settings or projects.

The first row in **Settings** shows the installed Deadlimit Manager version and update status. Its contextual **CHECK** / **UPDATE...** action uses the same updater.

The ZIP and checksum files visible in the rolling release are transport files used by the installer and updater. Manual ZIP setup is not a separate supported artist workflow.

## Compatibility and support

Deadlimit is hobby software maintained on a best-effort basis. A Deadlock update, a Source 2 format change, or an external tool update can temporarily break part of the workflow.

Before reporting a bug, check [COMPATIBILITY.md](COMPATIBILITY.md) and include the Deadlimit version or commit plus the relevant external-tool versions.

- Bug-report scope and support: [SUPPORT.md](SUPPORT.md)
- Private vulnerability reporting: [SECURITY.md](SECURITY.md)
- Third-party notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)

## For developers

If you are here to make a mod, you can stop reading. Everything below is for working on Deadlimit itself.

### Run from a clone

Install Git and the .NET 10 SDK, then:

```powershell
git clone https://github.com/downlimit/Deadlimit.git
Set-Location Deadlimit
.\DeadlimitManager.cmd
```

### Build and smoke tests

```powershell
dotnet restore internal/src/Deadlimit/Deadlimit.csproj
dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore
internal/tests/open-source-content-policy-smoke.ps1
internal/tests/portable-path-defaults-smoke.ps1
internal/tests/prepare-behavior-smoke.ps1
```

The full Windows CI contract is in [`.github/workflows/build.yml`](.github/workflows/build.yml). See [CONTRIBUTING.md](CONTRIBUTING.md) for the fork/PR workflow and required DCO sign-off.

The open-source readiness record is [`internal/docs/OPEN_SOURCE_PLAN.md`](internal/docs/OPEN_SOURCE_PLAN.md).

## License and independence

Deadlimit source code is distributed under the [MIT License](LICENSE).

Deadlimit interoperates with user-installed third-party tools and local content; it does not distribute those tools or that content. Deadlimit is an independent community project with no affiliation, sponsorship, endorsement, or approval from Valve, Autodesk, Adobe, Wall Worm, or the maintainers of the other tools it can invoke.
