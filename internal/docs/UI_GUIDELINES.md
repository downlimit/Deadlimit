# Deadlimit UI guidelines

## Shared controls

Deadlimit Manager UI must look like one product, not a collection of one-off WinForms controls.

- Reuse an existing shared control factory/component before constructing a one-off control with local dimensions or margins.
- Normal Settings-row action buttons use `SettingsUiFactory.CreateActionButton()`.
- Do not create a bare `new Button` for a normal Settings action unless the control is intentionally a different semantic class and the difference is documented.
- Do not hard-code a second copy of an existing button size/margin/style in another feature file. Put reusable visual rules in a shared helper.
- Adjacent controls with the same semantic role must use the same size, spacing, theme behavior and interaction states.
- A button whose primary action is to open a dialog/options step before any operation starts must end with an ellipsis (`…`) in every localized label. Direct actions that execute immediately must not gain an ellipsis.

## Dialogs and window ownership

Interactive Deadlimit windows must have predictable ownership, z-order and taskbar behavior.

- Modal windows must be shown with an explicit owner (`ShowDialog(owner)` or the equivalent owned custom dialog API).
- New dialog forms use `StartPosition = FormStartPosition.CenterParent` unless there is a documented reason not to.
- Interactive dialogs must not use `ShowInTaskbar = false`. The startup splash is the intentional exception.
- Do not change `ShowInTaskbar`, ownership or other shell-presence flags after the window has already been shown; doing so can recreate handles and break z-order/focus.
- If one modal flow closes one dialog and immediately opens another, schedule the next dialog after the first has fully closed and reactivate the owner first when needed.
- Reuse the shared Deadlimit `MessageBox`/dialog patterns rather than introducing a visually independent confirmation window.

## Tooltips

Tooltips must be readable as compact help, not rendered as one long sentence.

- All Deadlimit Manager tooltips use the shared `RichToolTip` renderer. Do not introduce separate native WinForms tooltip styling for main-window features or Settings.
- Tooltip background is consistently white with dark text and a neutral border, independent of the active application theme.
- Tooltip content is word-wrapped to a compact maximum width. No tooltip should expand into a screen-wide single line.
- Split different ideas into separate paragraphs with a blank line between them.
- Keep each paragraph short. Prefer 1-2 sentences per paragraph.
- Put the primary action first, modifiers/alternate actions second, and warnings or side effects last.
- When a modifier or input chord is important, write it as an emphasized token such as `**SHIFT+LMB**` instead of relying only on uppercase text.
- The shared renderer preserves explicit `**bold**` spans and also emphasizes common action/modifier tokens such as SHIFT interactions, PREPARE FOR CSDK, BUILD FOR TEST, LAUNCH CSDK, Release ID and managed-tool actions.
- Use bold only for short keywords, shortcuts and state names, not whole sentences.
- Avoid implementation jargon in user-facing tooltips when a plain product term exists.
- Russian UI copy must not use untranslated pipeline jargon such as `authoring-контент` or `retail Deadlock`. Use `рабочие файлы проекта` / `файлы проекта` and `игровой клиент Deadlock` instead.
- Technical names such as DMX, VMAT, VPK, ModelDoc and Material Editor may stay as-is when they identify an actual format or tool. Internal code identifiers may also keep technical terminology.
- In user-facing copy, prefer `Deadlock game client` / `игровой клиент Deadlock`. Use `retail` only when the retail-vs-toolchain distinction itself must be explained.

Example:

```text
Launch the Deadlock game client through Steam.

Hold **SHIFT+LMB** to copy the camera-lock command instead of launching the game.
```

## Status feedback

Actions that perform validation, network checks, installs or updates must provide visible state feedback in the window itself.

- The action button changes to a busy label such as `CHECKING...` / `WORKING...` while the action is running.
- The corresponding status text changes immediately and is repainted before awaiting network or process work.
- A completed check leaves a persistent, explicit result such as `Up to date`, `Update available`, `Installed - version unknown`, `Client ready`, or `Network issue`.
- Do not hide the only useful result inside a tooltip.

## Language and theme

Changing language or theme must not require the user to manually relaunch Deadlimit Manager.

- Theme preview may apply immediately while Settings is open.
- `APPLY` / `ПРИМЕНИТЬ` commits language/theme changes and rebuilds the main UI in-process.
- Tool/workspace paths persist immediately when installed or selected and do not depend on `APPLY`.
- Do not use `Application.Restart()` for normal language/theme changes.
