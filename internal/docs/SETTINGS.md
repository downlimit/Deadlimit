# Deadlimit Manager — machine-local settings and toolchain management

## Purpose

Deadlimit Manager keeps workstation-specific tool roots and UI preferences outside project manifests and outside Git. Saved values live in `%LOCALAPPDATA%\Deadlimit\settings.json`.

The Settings window treats external tools as managed dependencies instead of plain editable path fields.

## Tool rows

Rows are ordered by pipeline importance:

```text
Reduced CSDK
DeadlockTools
Deadlock game client
Projects folder
```

`Deadlock game client` is the user-facing name for the installed Steam copy of Deadlock (`Project8Staging`). Internal code/settings may still use the historical `RetailDeadlockRoot` identifier so existing configuration remains compatible.

Each row presents the dependency name, current status, context-sensitive actions, a read-only path, an Explorer button and `BROWSE…` for selecting an existing installation/folder.

Tool paths are intentionally read-only. Paths change only through `INSTALL…` or `BROWSE…`, avoiding partially typed path states.

The Settings window forces a status refresh when it opens. Checks also provide visible feedback in the row: the status and action button switch to `Checking…` / `CHECKING…` before asynchronous work starts, then leave a persistent result.

Relevant status values include:

```text
Not specified
Installed · version unknown
Up to date
Update available
Invalid path
Network issue
Checking…
Working…
Game client ready
Folder ready
```

When known, CSDK generation is shown directly in the status, for example `Up to date · CSDK 12`. The useful result must remain visible in the row; tooltips contain additional detail rather than being the only feedback.

## Reduced CSDK

User-facing UI refers to the dependency as `Reduced CSDK`; the current generation number is version/state information rather than part of the stable dependency name. Historical/current physical distribution folder names such as `Reduced_CSDK_12` remain valid and are not renamed.

Actions are state-driven:

```text
not configured / invalid -> INSTALL…
valid current install     -> CHECK
newer generation found    -> UPDATE…
```

`INSTALL…` asks for the exact folder that will become the Reduced CSDK root, downloads the current published Reduced CSDK archive and installs its contents there. The destination must be empty.

`UPDATE…` downloads the current archive and overlays distribution-owned files onto the existing CSDK root without deleting unrelated user files.

`CHECK` validates `csdkcfg.exe`, reads the installed generation from a Deadlimit marker or a recognizable manual installation folder name, and checks the current CSDK generation published by Deadlock Modding Notes.

### CSDK Setup

`SETUP` is deliberately separate from `INSTALL…`. It implements the optional Full Game Files procedure documented by the current CSDK installation guide.

The button is enabled only when:

- the Reduced CSDK root is valid;
- the configured Deadlock game client root is valid;
- the current CSDK network source was reachable during the status check.

Current setup flow:

1. read the current depot/manifest IDs from the current CSDK guide;
2. obtain the current Windows x64 DepotDownloader release from SteamRE/DepotDownloader;
3. run the published depot downloads with Steam QR authentication;
4. if the published old-manifest fallback archive is needed, apply it and retry;
5. extract `game\citadel\pak01_dir.vpk` as-is with the ValvePak library already used by Deadlimit;
6. remove the downloaded `pak01_*.vpk` sets from CSDK `game\citadel` and `game\core`;
7. re-apply the current Reduced CSDK archive over the result.

The configured Deadlock game client installation is validated as a prerequisite and is never modified by this setup transaction.

The Full Game Files step remains optional according to the upstream CSDK documentation; it is required for features such as `bin_server`, S2FM and Hammer rather than for every Reduced CSDK authoring task.

## DeadlockTools

DeadlockTools is managed against the upstream repository:

```text
https://github.com/dotryen/DeadlockTools
branch: master
```

`INSTALL…` clones the repository into an empty selected folder and builds `DeadlockTools/DeadlockTools.csproj` in Release configuration.

For Git checkouts, `CHECK` compares the local commit with the current upstream `master` commit. `UPDATE…` performs a fast-forward pull and rebuilds Release. A manually copied valid build can be used, but its freshness cannot be established automatically without Git metadata.

The expected executable remains:

```text
<DeadlockTools root>\DeadlockTools\bin\Release\net10.0\DeadlockTools.exe
```

## Deadlock game client

This row means the actual installed game that the user launches through Steam. In the current Steam layout its root folder is `Project8Staging`.

Deadlimit Manager does not install or update the Steam game from Settings. `BROWSE…` selects the existing game-client root and `CHECK` validates that it contains `game\citadel`.

## Projects folder

Projects folder is a workspace location, not a managed external dependency. It has only local validity state plus `BROWSE…` / Explorer access; it has no install/update lifecycle.

## Other settings and bundled tools

The following controls remain in Settings:

```text
Interface language
Interface theme
3ds Max -> Deadlimit Max Script
CSDK -> CSDK Fast Startup Fix
Deadlimit Manager version
```

The Settings window uses the same embedded application icon as the main Deadlimit Manager executable.

Theme selection previews immediately while Settings is open. Cancel restores the previous theme. After Save, language/theme changes rebuild the main UI inside the existing Deadlimit Manager process, so the user does not need to relaunch or restart the program.

Tooltip copy/layout rules are defined in `UI_GUIDELINES.md`.

## Consumers

`DeadlimitPaths` continues to expose the saved machine-local roots to existing pipeline actions:

```text
EXTRACT HERO SOURCE -> Deadlock game client
PREPARE FOR CSDK    -> Reduced CSDK content/game roots
BUILD FOR TEST      -> Reduced CSDK + Deadlock game client + DeadlockTools
LAUNCH CSDK         -> Reduced CSDK\csdkcfg.exe
```
