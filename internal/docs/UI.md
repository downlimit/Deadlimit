# Deadlimit UI settings

Deadlimit stores machine-local interface preferences in `%LOCALAPPDATA%\Deadlimit\settings.json`.

## Projects library

Settings includes a machine-local `Projects folder / Папка проектов` path. The main window shows the immediate child directories of that folder as a vertical project library.

The library hides directories that contain configured tool roots when those roots are inside the projects folder. This includes Reduced CSDK12, DeadlockTools and retail Deadlock. The Deadlimit directory is also excluded, including the current application directory when it is located under the projects root.

A `+` action in the Projects header creates a new child directory after asking for its name, so a project folder does not need to be created manually in Explorer. That directory name is also the canonical project name; the main project panel does not expose a second editable project-name field.

Each library entry shows its Release ID. A valid `.deadlimit/project.json` entry is marked with `◆`; a directory without project metadata is marked with `◇`; an existing metadata file that cannot be loaded is marked with `!` plus `JSON ERROR / ОШИБКА JSON`. Entries without a Release ID display `ID —`.

Selecting a library entry loads its existing `.deadlimit/project.json` metadata when present. A plain folder without metadata is still selectable and can be initialized by filling the remaining project fields and pressing `SAVE PROJECT / СОХРАНИТЬ ПРОЕКТ`. Double-clicking a library entry opens that folder in Explorer.

For compatibility with existing project JSON files, the manifest may still contain `ProjectName`, but Deadlimit derives and normalizes that value from the project folder name whenever metadata is loaded or saved.

The project panel places `SAVE PROJECT / СОХРАНИТЬ ПРОЕКТ` in the right action column directly below the hero-list refresh action. `Release ID` has a hover tip explaining that valid IDs are `01-99` and map to the deployed retail VPK name `pak##_dir.vpk`.

The library and the selected project's DMX/PNG scan refresh automatically when the application regains focus, after settings changes and after project saves. The previous NEW PROJECT, OPEN PROJECT and RESCAN actions are therefore not exposed in the main UI.

## Theme

Available interface themes:

- `system` — follow the Windows light/dark preference; this is the default for new and existing installations that do not yet have a saved theme;
- `light` — force the light interface palette;
- `gray` — use Deadlimit's neutral mid-gray palette;
- `dark` — displayed as `Original theme / Исходная тема`; uses the low-contrast Source 2/CSDK12-style dark palette.

Theme changes are applied after Deadlimit restarts. The application targets .NET 10 WinForms and uses `Application.SetColorMode` for native/system color context. Deadlimit then applies its own restrained application palette so panels, inputs, buttons and borders keep a consistent low-contrast hierarchy. The `system` option resolves that palette from the active Windows light/dark preference. Windows high-contrast mode is left untouched.

The Original theme uses the current CSDK12 reference values for the controls discussed during the 2026-08-23 UI pass: ordinary section outlines are approximately `#3F3F3F`, and the normal Launch Tools button surface is `#3C3C3C` without a bright outline.

## Language

The interface language supports English and Russian and is stored alongside the theme. Language changes are also applied after restart.
