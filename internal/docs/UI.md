# Deadlimit UI settings

Deadlimit stores machine-local interface preferences in `%LOCALAPPDATA%\Deadlimit\settings.json`.

## Theme

Available interface themes:

- `system` — follow the Windows light/dark preference; this is the default for new and existing installations that do not yet have a saved theme;
- `light` — force the standard WinForms light theme;
- `gray` — Deadlimit's neutral gray palette;
- `dark` — force the WinForms dark theme.

Theme changes are applied after Deadlimit restarts. The application targets .NET 10 WinForms, so Windows/light/dark use the built-in `Application.SetColorMode` path. The gray option uses the classic WinForms mode plus a Deadlimit-owned palette for application forms and controls.

## Language

The interface language supports English and Russian and is stored alongside the theme. Language changes are also applied after restart.
