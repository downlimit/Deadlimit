# Deadlimit

Deadlimit is the umbrella repository for a family of Deadlock modding tools.

## Projects

### Deadlimit Aggregator

The main Windows desktop application. It aggregates the Deadlock modding pipeline and related tools, including project handling, source extraction, CSDK authoring preparation, build/test flow, and integration with the external tooling used by the project.

Source: `internal/src/Deadlimit/`

User-facing launcher: `DeadlimitAggregator.cmd`

Built application: `DeadlimitAggregator.exe`

### Deadlimit Max Script

3ds Max integration and tooling used by the Deadlimit pipeline. The current implementation lives in `.deadlimit/maxscript-vertcolor-trans/` and retains the existing `DeadlimitPipelineScripts.ms` filename and MaxScript identifiers for compatibility. A future Blender direction is planned, but Blender support is not part of the current implementation.

### Deadlimit Shade

Substance 3D Painter shader/preset/tooling work for reproducing Deadlock material response.

Directory: `DeadlimitShade/`

## Naming and compatibility

`Deadlimit` remains the repository/family name. The desktop application is `Deadlimit Aggregator`.

Some legacy technical identifiers intentionally remain unchanged because they are persisted or are part of the established repository/project contract:

- `.deadlimit` project metadata;
- `%LocalAppData%\Deadlimit\settings.json` user settings;
- `Deadlimit.*` .NET namespaces and the existing source directory;
- the repository root `C:\WorkProjects\Deadlock\Deadlimit`;
- the legacy single-instance mutex;
- `DeadlimitPipelineScripts.ms` and existing MaxScript class/global identifiers.

These identifiers are compatibility details and do not represent the user-facing desktop application name.
