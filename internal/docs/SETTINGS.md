# Deadlimit — Machine-local settings and quick access

## Purpose

Deadlimit must not depend on one developer machine having the historical default directory layout. Tool/install roots and UI preferences are machine-local configuration and are deliberately kept outside project manifests and outside the Git repository.

The desktop UI exposes `SETTINGS` for:

```text
Reduced CSDK12 root
DeadlockTools root
Retail Deadlock root (Steam Project8Staging)
Interface language: English / Русский
```

Settings also exposes `📂 MaxScript VertColor Trans`. It opens the bundled repository folder `.deadlimit/maxscript-vertcolor-trans/`, which contains the path-free `DeadlimitVertexColorFBX.ms` and its README.

The current known local path defaults remain fallbacks only:

```text
CSDK:
C:\WorkProjects\Deadlock\Reduced_CSDK_12

DeadlockTools:
C:\WorkProjects\Deadlock\DeadlockTools

Retail Deadlock:
D:\Program Files (x86)\Steam\steamapps\common\Project8Staging
```

Saved values live in the machine-local Deadlimit settings file under `%LOCALAPPDATA%\Deadlimit\settings.json`. They are not written to `.deadlimit\project.json`, because a project may be moved or opened on another workstation with different installs and UI preferences.

## Validation

Saving settings currently requires:

- the selected Reduced CSDK12 directory to exist and contain `csdkcfg.exe`;
- the selected DeadlockTools root to exist and resolve the currently supported `DeadlockTools\bin\Release\net10.0\DeadlockTools.exe` build path;
- the selected retail Deadlock root to exist and contain `game\citadel`;
- UI language to normalize to either `en` or `ru`.

If a future DeadlockTools release changes its executable layout, update only path resolution/validation; do not migrate project manifests.

## Consumers

A new `DeadlimitPaths()` resolves the latest saved machine-local paths immediately. Existing actions therefore pick up a path change without restarting Deadlimit:

```text
EXTRACT HERO SOURCE -> Retail Deadlock root
PREPARE FOR CSDK    -> Reduced CSDK12 content/game roots
BUILD & TEST        -> Reduced CSDK12 + Retail Deadlock + DeadlockTools
LAUNCH CSDK         -> Reduced CSDK12\csdkcfg.exe
```

Language is also stored in the same machine-local settings file. Changing language intentionally restarts the small desktop app once so all already-created WinForms controls, tooltips and dialogs are rebuilt consistently in the selected language.

Current UI language coverage includes:

- main project controls and status messages;
- settings dialog;
- PREPARE / BUILD & TEST / LAUNCH CSDK controls and tooltips;
- BUILD & TEST result dialog;
- the app-owned progress/status messages around the build transaction.

Low-level compiler/tool output and exception text may remain in the language emitted by the external tool; Deadlimit does not rewrite diagnostic logs.

## Quick-access UI

Two mechanical navigation actions are part of the desktop workflow:

- `OPEN` / `ОТКРЫТЬ` beside the project-folder browse button opens the current artist project folder in Windows Explorer;
- `LAUNCH CSDK` / `ЗАПУСТИТЬ CSDK` launches the configured `csdkcfg.exe` with the CSDK root as working directory.

`BUILD & TEST` / `СОБРАТЬ И ТЕСТИРОВАТЬ` has a tooltip documenting the hidden recovery gesture:

```text
SHIFT + BUILD & TEST
→ force a clean/full rebuild
```

The normal click remains incremental.

Current CSDK12 documentation (rechecked 2026-08-23) still defines `Reduced_CSDK_12\csdkcfg.exe` as the configuration-tool launcher. Steam must be running before the Source 2 tools themselves are launched from that configuration tool.
