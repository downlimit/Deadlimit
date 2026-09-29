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

## CI minutes, storage, and release safety

Read [internal/docs/CI_POLICY.md](internal/docs/CI_POLICY.md) before changing a workflow, artifact, cache, installer or source-milestone publication process. The MICROMACRO portable policy was adapted to Deadlimit; the repository-specific policy is authoritative here.

- Run `internal/Run-Local-Checks.ps1 -Scope Fast` after routine edits and `-Scope Full` for relevant Windows/.NET changes before requesting a hosted run. Report the command, exit status, and local log path; do not present local validation as hosted success.
- Full hosted Windows Build, texture, launch and UI suites are manual `workflow_dispatch` checks. Protected `main` also requires short PR-only `build` (.NET cross-target Ubuntu compile) and `smoke` (source contract); DCO remains the third automatic PR check. Do not add push or scheduled expensive runs, duplicate PR+push builds, or relax the full `native-build`/`native-smoke` release gate. Branch cleanup stays an automatic merged-PR maintenance action.
- No Actions artifacts or caches without documented owner approval. Never upload game assets, CSDK contents, VPKs, proprietary projects, build folders, credentials or unredacted diagnostics. Preserve existing Git-based installation and small source-milestone attachments; publication requires explicit approval.
- A workflow change must update the inventory, retain least-privilege permissions, per-job timeout and workflow/ref concurrency, and pass `internal/tests/ci-policy-smoke.ps1`. Do not remove failing tests or bypass release checks to save CI minutes.
- Billing, retention, historical artifact deletion, Packages, secrets, branch protection and store publication require separate owner approval. A policy change alone does not configure administrative settings.
