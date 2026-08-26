# Deadlimit UI settings

Deadlimit stores machine-local interface preferences in `%LOCALAPPDATA%\Deadlimit\settings.json`.

## Projects library

Settings includes a machine-local `Projects folder / Папка проектов` path. The main window shows the immediate child directories of that folder as a vertical project library.

The library hides directories that contain configured tool roots when those roots are inside the projects folder. This includes Reduced CSDK12, DeadlockTools and retail Deadlock. The Deadlimit directory is also excluded, including the current application directory when it is located under the projects root.

A `+` action in the Library header creates a new child directory after asking for its name, so a project folder does not need to be created manually in Explorer. That directory name is also the canonical project name; the main project panel does not expose a second editable project-name field.

Each library entry shows its Release ID. A valid `.deadlimit/project.json` entry is marked with `◆`; a directory without project metadata is marked with `◇`; an existing metadata file that cannot be loaded is marked with `!` plus `JSON ERROR / ОШИБКА JSON`. Entries without a Release ID display `ID —`.

Selecting a library entry loads its existing `.deadlimit/project.json` metadata when present. A plain folder without metadata is still selectable and can be initialized by filling the remaining project fields and pressing `SAVE PROJECT / СОХРАНИТЬ ПРОЕКТ`. Double-clicking a library entry opens that folder in Explorer.

For compatibility with existing project JSON files, the manifest may still contain `ProjectName`, but Deadlimit derives and normalizes that value from the project folder name whenever metadata is loaded or saved.

The project panel places the folder actions on the project-folder row: a compact `📂` button opens the selected project folder and `EXTRACT SOURCE / ИЗВЛЕЧЬ ИСХОДНИКИ` sits immediately to its right. `SAVE PROJECT / СОХРАНИТЬ ПРОЕКТ` remains in the right action column directly below the hero-list refresh action.

`Release ID` is a numeric `01-99` spinner rather than free-form text. It accepts direct numeric typing as well as the up/down arrows, existing blank projects remain blank until an ID is chosen, and the control preserves the two-digit presentation. Its hover tip explains that the value maps to the deployed retail VPK name `pak##_dir.vpk`.

Hero selection is unlocked while a folder has not yet been initialized as a saved Deadlimit project. After a successful first save it locks automatically. Existing projects open with hero selection locked; the lock button next to `REFRESH LIST / ОБНОВИТЬ СПИСОК` toggles between `🔒` and `🔓` to explicitly permit or prevent hero changes. Mouse-wheel input does not change the hero while the combo box is closed.

The library and the selected project's file scan refresh automatically when the application regains focus, after settings changes and after project saves. The Library ListBox scrolls normally with the mouse wheel when the project count exceeds the visible area. The project-files area likewise supports scrolling and is laid out so additional file-format columns can be added later without redesigning the main window.

## Project header

The workspace header follows a Steam-library-style hierarchy. A compact gear opens Settings in the upper-right corner. The lower-left action pair is `PREPARE FOR CSDK / ПОДГОТОВИТЬ ДЛЯ CSDK` above a large purple `LAUNCH CSDK / ЗАПУСК CSDK` button. The lower-right pair is `BUILD FOR TEST / СОБРАТЬ ДЛЯ ТЕСТА` above a large green `LAUNCH GAME / ЗАПУСК ИГРЫ` button. Build for Test compiles and deploys the retail VPK but does not launch Deadlock; launching the game is a separate action.

The large launch buttons retain their project colors. The smaller Settings/Prepare/Build controls use a dark 70%-opaque overlay so project artwork remains visible beneath them, with no backing surface outside their bounds. A 33%-opacity edge vignette is drawn over the cover artwork and below the controls.

While ONLINE CSDK synchronization is active, the CSDK launch button replaces its play glyph with a red circular indicator of the same visual size. The indicator preserves the normal two-space gap before the label and continuously pulses its opacity on a sinusoidal cycle until online synchronization is stopped.

A normal click on `LAUNCH GAME / ЗАПУСК ИГРЫ` launches Deadlock through the installed Steam client using app id `1422450`, with the Steam URI as a fallback. Holding SHIFT while clicking does not launch Deadlock; it only copies `cl_lock_camera true` to the Windows clipboard for visual testing.

Each selected project owns a header image at `.deadlimit/project-header.png`. For a newly created project folder, Deadlimit creates the hidden `.deadlimit` directory and a plain dark-gray PNG sized to the live header area. Existing projects receive the same template the first time they are selected if the image is missing. Double-clicking the cover opens its hidden `.deadlimit` folder. Deadlimit reloads the artwork when the application regains focus and does not hold a persistent lock on the PNG.

## Project files

The project-files panel shows the information needed for the current artist-input contract without repeating full source paths: counts by format, extracted hero-source file count, the detected retail main model, and filename lists grouped by format.

Currently the artist-facing project root is intentionally limited to `DMX` model exports and `PNG` texture sources. Those two formats cover the current model/material replacement pipeline, but they are not intended to represent every future Source 2 authoring workflow. Animation replacement will require an explicit animation-authoring pipeline before animation resource types are promoted into the project-root contract. The file-list layout already supports adding more format columns when those inputs become real Deadlimit features.

## Status bar

The bottom status area is split into three Steam-like zones. The left zone identifies the selected project, the center zone carries the existing operation/status message and build progress, and the right zone shows compact project context such as hero and Release ID. Existing status producers and the build progress source remain authoritative; the custom bottom bar only presents them in the new layout.

## Theme

Available interface themes:

- `system` — follow the Windows light/dark preference; this is the default for new and existing installations that do not yet have a saved theme;
- `light` — force the light interface palette;
- `gray` — use Deadlimit's neutral mid-gray palette;
- `dark` — displayed as `Original theme / Исходная тема`; uses the low-contrast Source 2/CSDK12-style dark palette.

Theme changes are applied after Deadlimit restarts. The application targets .NET 10 WinForms and uses `Application.SetColorMode` for native/system color context. Deadlimit then applies its own restrained application palette so panels, inputs, buttons and borders keep a consistent low-contrast hierarchy. The `system` option resolves that palette from the active Windows light/dark preference. Windows high-contrast mode is left untouched.

The Original theme uses the current CSDK12 reference values for the controls discussed during the 2026-08-23 UI pass: ordinary section outlines are approximately `#3F3F3F`, and the normal Launch Tools button surface is `#3C3C3C` without a bright outline. Deadlimit also disables persistent button tab focus and clears mouse-click focus after actions so the last-used button does not retain an extra native default/focus outline.

## Language

The interface language supports English and Russian and is stored alongside the theme. Language changes are also applied after restart.
