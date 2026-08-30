# Deadlimit

Deadlimit is the umbrella repository for a family of Deadlock modding tools.

## Projects

### Deadlimit Manager

The main Windows desktop application. It manages the Deadlock modding pipeline and related tools, including project handling, source extraction, CSDK authoring preparation, build/test flow, and integration with the external tooling used by the project.

Source: `internal/src/Deadlimit/`

User-facing launcher: `DeadlimitManager.cmd`

Built application: `DeadlimitManager.exe`

### Deadlimit Max Script

3ds Max integration and tooling used by the Deadlimit pipeline. The current implementation lives in `.deadlimit/maxscript-vertcolor-trans/` and retains the existing `DeadlimitPipelineScripts.ms` filename and MaxScript identifiers for compatibility. A future Blender direction is planned, but Blender support is not part of the current implementation.

### Deadlimit Shade

Substance 3D Painter shader/preset/tooling work for reproducing Deadlock material response.

Directory: `DeadlimitShade/`

## Repository updater

`Deadlimit Updater` updates the entire Deadlimit repository from `origin/main`. It is not tied to one subproduct. After updating the checkout it refreshes the local Deadlimit Manager build and the two user-facing root shortcuts.

## Naming and compatibility

`Deadlimit` remains the repository/family name. The desktop application is `Deadlimit Manager`. The repository-wide updater is `Deadlimit Updater`.

Some legacy technical identifiers intentionally remain unchanged because they are persisted or are part of the established repository/project contract:

- `.deadlimit` project metadata;
- `%LocalAppData%\Deadlimit\settings.json` user settings;
- `Deadlimit.*` .NET namespaces and the existing source directory;
- the repository root `C:\WorkProjects\Deadlock\Deadlimit`;
- the legacy single-instance mutex;
- `DeadlimitPipelineScripts.ms` and existing MaxScript class/global identifiers.

These identifiers are compatibility details and do not represent the user-facing desktop application name.
