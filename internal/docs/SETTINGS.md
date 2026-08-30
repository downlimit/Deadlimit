# Deadlimit Manager — machine-local settings and toolchain management

## Purpose

Deadlimit Manager keeps workstation-specific tool roots and UI preferences outside project manifests and outside Git. Saved values live in `%LOCALAPPDATA%\Deadlimit\settings.json`.

The Settings window treats external tools as managed dependencies instead of plain editable path fields.

The window is intentionally compact and fixed-size. It must not be wider than the default main Manager window, and the user must not be able to resize it into a layout that clips `SAVE` / `CANCEL` or breaks row spacing.

## Tool rows

Rows are ordered by pipeline importance:

```text
Reduced CSDK
DeadlockTools
Deadlock client
Projects folder
```

`Deadlock client` is the user-facing name for the installed Steam copy of Deadlock (`Project8Staging`). Internal code/settings may still use the historical `RetailDeadlockRoot` identifier so existing configuration remains compatible.

Each row presents the dependency name, current status, context-sensitive actions, a read-only path, an Explorer button and `BROWSE…` for selecting an existing installation/folder.

Tool paths are intentionally read-only. Paths change only through `INSTALL…` or `BROWSE…`, avoiding partially typed path states.

The Settings window forces a status refresh when it opens. Checks also provide visible feedback in the row: the status and action button switch to `Checking…` / `CHECKING…` before asynchronous work starts, then leave a persistent result.

Relevant status values include:

```text
Not specified
Version unknown
Up to date
Update available
Invalid path
Network issue
Checking…
Working…
Client ready
Folder ready
```

When known, CSDK generation is shown directly in the status, for example `Up to date · CSDK 12`. Managed DeadlockTools installations show the GitHub release tag, for example `Up to date · v1.1.0`. The useful result must remain visible in the row; tooltips contain additional detail rather than being the only feedback.

## Download and long-operation feedback

Any Settings action that downloads or performs a long managed-tool operation must expose visible progress inside the Settings window. Updating only the title bar or changing the row to `Working…` is not sufficient feedback.

Progress rules:

- HTTP downloads use real byte progress when the server exposes a content length;
- the row reports transferred size and an approximate ETA when enough data exists to estimate it;
- stages that do not expose a trustworthy total, such as an interactive DepotDownloader run, use indeterminate progress rather than invented percentages;
- archive/VPK extraction and file-application stages report stage progress where a real item count exists;
- every active managed-tool operation exposes `CANCEL` / `ОТМЕНА`;
- cancellation propagates to HTTP reads, archive extraction, VPK extraction and child processes where possible;
- completion, failure or cancellation must always leave the dependency row in a usable state. `Working…` must never remain stuck after the operation has ended.

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
- the configured Deadlock client root is valid;
- the current CSDK network source was reachable during the status check.

Current setup flow:

1. read the current depot/manifest IDs from the current CSDK guide;
2. obtain the current Windows x64 DepotDownloader release from SteamRE/DepotDownloader;
3. run the published depot downloads with Steam QR authentication;
4. if the published old-manifest fallback archive is needed, apply it and retry;
5. extract `game\citadel\pak01_dir.vpk` as-is with the ValvePak library already used by Deadlimit;
6. remove the downloaded `pak01_*.vpk` sets from CSDK `game\citadel` and `game\core`;
7. re-apply the current Reduced CSDK archive over the result.

The configured Deadlock client installation is validated as a prerequisite and is never modified by this setup transaction.

The Full Game Files step remains optional according to the upstream CSDK documentation; it is required for features such as `bin_server`, S2FM and Hammer rather than for every Reduced CSDK authoring task.

## DeadlockTools

DeadlockTools upstream:

```text
https://github.com/dotryen/DeadlockTools
```

The normal user path is release-based rather than source-build-based.

`INSTALL…` asks for the location in which DeadlockTools should live. The user does not need to pre-create an empty `DeadlockTools` folder. Deadlimit creates exactly one `DeadlockTools` folder itself and installs the latest official `DeadlockTools-windows-x64.zip` release directly into that root.

Normal managed layout:

```text
<selected location>\DeadlockTools\
    DeadlockTools.exe
    ...release files...
```

There must not be a generated `DeadlockTools\DeadlockTools\bin\Release\...` folder chain for release installs. That deeper layout remains recognized only for legacy managed installs and source/Git checkouts.

For a Deadlimit-managed release installation, `CHECK` compares the recorded installed tag with the latest official GitHub release. `UPDATE…` downloads and overlays the newest release and updates the marker.

Existing Git checkouts remain supported. For them, `CHECK` compares the local commit with upstream `master`, and `UPDATE…` performs a fast-forward pull and Release rebuild.

A manually copied build without release metadata or Git metadata can still be selected with `BROWSE…`, but its version cannot be proven. In that state the row shows `Version unknown` and the primary action remains `INSTALL…`, allowing the user to switch to a managed current release. It must not offer a `CHECK` action that cannot actually establish freshness.

Deadlimit also performs a guarded one-time migration for its own previous managed-release layout. Migration only runs when the folder contains the Deadlimit release marker and the exact old nested output tree without unrelated top-level user content. The new flat executable is verified before the old nested tree is removed.

## Deadlock client

This row means the actual installed game that the user launches through Steam. In the current Steam layout its root folder is `Project8Staging`.

Deadlimit Manager does not install or update the Steam game from Settings. `BROWSE…` selects the existing client root and `CHECK` validates that it contains `game\citadel`.

For Russian UI the row label is intentionally compact: `Deadlock клиент`. It must remain on one line so all tool rows keep equal vertical spacing.

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

Theme names are user-facing palette descriptions only:

```text
System / Системная
Light / Светлая
Gray / Серая
Dark / Тёмная
```

Do not expose historical/internal wording such as `Original theme` / `Исходная` for the dark palette.

The Settings window uses the same embedded application icon as the main Deadlimit Manager executable.

Theme selection previews immediately while Settings is open. Cancel restores the previous theme. After Save, language/theme changes rebuild the main UI inside the existing Deadlimit Manager process, so the user does not need to relaunch or restart the program.

Tooltip copy/layout rules are defined in `UI_GUIDELINES.md`.

## Consumers

`DeadlimitPaths` continues to expose the saved machine-local roots to existing pipeline actions:

```text
EXTRACT HERO SOURCE -> Deadlock client
PREPARE FOR CSDK    -> Reduced CSDK content/game roots
BUILD FOR TEST      -> Reduced CSDK + Deadlock client + DeadlockTools
LAUNCH CSDK         -> Reduced CSDK\csdkcfg.exe
```
