# Deadlimit UI settings

Deadlimit stores machine-local interface preferences in `%LOCALAPPDATA%\Deadlimit\settings.json`.

## Theme

Available interface themes:

- `system` — follow the Windows light/dark preference; this is the default for new and existing installations that do not yet have a saved theme;
- `light` — force the light interface palette;
- `gray` — use Deadlimit's neutral mid-gray palette;
- `dark` — force the low-contrast dark interface palette.

Theme changes are applied after Deadlimit restarts. The application targets .NET 10 WinForms and uses `Application.SetColorMode` for native/system color context. Deadlimit then applies its own restrained application palette so panels, inputs, buttons and borders keep a consistent low-contrast hierarchy. The `system` option resolves that palette from the active Windows light/dark preference. Windows high-contrast mode is left untouched.

## Language

The interface language supports English and Russian and is stored alongside the theme. Language changes are also applied after restart.
