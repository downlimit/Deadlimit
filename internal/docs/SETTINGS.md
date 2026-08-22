# Deadlimit — Machine-local paths and quick access

## Purpose

Deadlimit must not depend on one developer machine having the historical default directory layout. Tool/install roots are machine-local configuration and are deliberately kept outside project manifests and outside the Git repository.

The desktop UI exposes `SETTINGS` for three roots:

```text
Reduced CSDK12 root
DeadlockTools root
Retail Deadlock root (Steam Project8Staging)
```

The current known local defaults remain fallbacks only:

```text
CSDK:
C:\WorkProjects\Deadlock\Reduced_CSDK_12

DeadlockTools:
C:\WorkProjects\Deadlock\DeadlockTools

Retail Deadlock:
D:\Program Files (x86)\Steam\steamapps\common\Project8Staging
```

Saved values live in the machine-local Deadlimit settings file under `%LOCALAPPDATA%\Deadlimit\settings.json`. They are not written to `.deadlimit\project.json`, because a project may be moved or opened on another workstation with different installs.

## Validation

Saving settings currently requires:

- the selected Reduced CSDK12 directory to exist and contain `csdkcfg.exe`;
- the selected DeadlockTools root to exist and resolve the currently supported `DeadlockTools\bin\Release\net10.0\DeadlockTools.exe` build path;
- the selected retail Deadlock root to exist and contain `game\citadel`.

If a future DeadlockTools release changes its executable layout, update only path resolution/validation; do not migrate project manifests.

## Consumers

A new `DeadlimitPaths()` resolves the latest saved machine-local paths immediately. Existing actions therefore pick up a settings change without restarting Deadlimit:

```text
EXTRACT HERO SOURCE -> Retail Deadlock root
PREPARE FOR CSDK    -> Reduced CSDK12 content/game roots
LAUNCH CSDK         -> Reduced CSDK12\csdkcfg.exe
future RELEASE      -> Reduced CSDK12 + Retail Deadlock + DeadlockTools as required
```

## Quick-access UI

Two mechanical navigation actions are part of the desktop workflow:

- `OPEN` beside the project-folder `BROWSE` button opens the current artist project folder in Windows Explorer;
- `LAUNCH CSDK` beside `PREPARE FOR CSDK` launches the configured `csdkcfg.exe` with the CSDK root as working directory.

Current CSDK12 documentation (checked 2026-08-22) still defines `Reduced_CSDK_12\csdkcfg.exe` as the configuration-tool launcher. Steam must be running before the Source 2 tools themselves are launched from that configuration tool.
