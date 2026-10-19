# Deadlimit agent instructions

This file applies to the entire repository. A nested `AGENTS.md` may add more specific rules for its subtree, but does not cancel these repository-wide rules unless it says so explicitly.

## Before changing UI

Read `internal/docs/UI_GUIDELINES.md` before creating or modifying Deadlimit Manager UI.

## UI invariants

- Reuse existing shared UI factories/components before creating a one-off WinForms control. Do not introduce a visually unique button, tooltip, modal window, spacing rule, color, or font when a neighboring shared pattern already exists.
- Settings action buttons must use `SettingsUiFactory.CreateActionButton()` (or another documented shared Settings factory for a different semantic button class). Do not create a bare `new Button` for a normal Settings row action.
- Main-window and Settings tooltips must use `RichToolTip`. Do not add a native WinForms `ToolTip` for these surfaces.
- Tooltip copy must follow the same information order: primary action first, modifier/alternate action second, warnings or side effects last. Keep paragraphs short and separate distinct ideas with blank lines. Localize both English and Russian copy through the existing UI text helpers.
- New modal/dialog windows must follow the existing Deadlimit dialog contract: explicit owner, `StartPosition = CenterParent`, consistent theme/style, and normal taskbar/z-order behavior. Show modal forms as `ShowDialog(owner)` (or the equivalent owned custom dialog API). Do not open ownerless `ShowDialog()` calls for interactive Deadlimit windows. Do not change taskbar/ownership flags after the window has already been shown.
- Interactive dialogs must not be created with `ShowInTaskbar = false`. The startup splash is the only intentional exception unless a documented UI decision explicitly adds another.
- Confirmation/action ordering must remain consistent with neighboring Deadlimit dialogs. Do not invent a new button order for a one-off dialog.
- When adding UI, compare it visually and structurally with the adjacent controls before considering the task complete.

## Required checks for UI changes

- Run the relevant UI/layout/localization/window smoke tests already present in `internal/tests/`.
- Build Deadlimit Manager in Release configuration.
- If a new reusable UI rule is introduced, update `internal/docs/UI_GUIDELINES.md` instead of leaving the rule only inside one implementation file.

## GitHub Actions budget and verification

Read [docs/CI_COST_POLICY.md](docs/CI_COST_POLICY.md) before modifying workflows, release automation, caches or Actions artifacts. Run `internal/tests/validate-local.ps1` for routine changes and add `-Full` for the native Windows/.NET validation where relevant. Do not start a hosted full build, publish a release, upload artifacts, or change account/repository billing and retention settings without the owner's explicit authorization. Run `internal/tests/workflow-cost-policy-smoke.ps1` for every workflow edit and update its approved inventory together with the policy when a new workflow is intentionally introduced. Report local, hosted and owner-only checks separately.

## Packaging regression checks

- After changing compilation, PREPARE, resource selection, model repair or VPK packing, inspect an actual resulting VPK before reporting success. Check exact entry paths, per-entry sizes, total archive/family size, largest resources, and the delta against the previous or known-good VPK. A passing build or checksum test alone is insufficient.
- Unchanged stock content used by a mod must resolve from the installed game and must not enter the VPK. A reference from an authored model/material or a CSDK compatibility transformation does not establish authorship. Changing one texture must not pull in the material's remaining stock textures. Preserve actual authored overrides even at retail resource paths.
- For IvyBuilder, explicitly report the measured VPK size and investigate growth against the known-good archive. The user's 20–30 MB expectation refers to the compressed archive. Keep compressed archive size and unpacked VPK size distinct. Do not remove authored resources, lower texture quality or strip animation/physics data merely to reach a size target.
- Preserve and hash-check project source/artist files during real fixture checks. Exercise imported VPK repacking as well when the shared dependency or repair path changes. Record verification locally; never commit game/model/texture payloads.
