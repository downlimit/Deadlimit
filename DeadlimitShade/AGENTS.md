# Deadlimit Shade agent instructions

These instructions apply to work under `DeadlimitShade/`.

## GUI / DCC validation policy

For Adobe Substance 3D Painter and other desktop DCC applications, separate **process readiness**, **automation readiness**, and **visual proof**. Do not collapse them into one signal.

### Process/window evidence

- Never treat `Process.MainWindowHandle == 0`, an empty `MainWindowTitle`, or a missing value from one window-enumeration method as sufficient proof that the application has no visible window.
- `MainWindowHandle` is auxiliary evidence only. If it is queried after launch, wait for startup/input idle when applicable and refresh the process first, but a zero handle still means only that this method did not resolve the application's main window.
- Do not restart Painter only because `MainWindowHandle == 0`.
- Before starting another Painter instance, check whether an existing Painter process/session is already alive and usable.

### Preferred evidence order

Use the minimum evidence sufficient for the current claim:

1. For scripted Painter operations, prefer Painter's supported remote scripting / Python API or another direct application-level response.
2. For claims about a visible window or viewport, use Computer Use / real top-level-window discovery and a screenshot of the actual Painter window when visual proof is required.
3. Use OS process properties such as PID, `MainWindowHandle`, and `MainWindowTitle` only as supporting diagnostics.

A successful API/remote-scripting response proves automation readiness, not viewport appearance. A process existing proves only process existence. A visual PASS requires visual evidence.

### Failure classification

If Painter is running but window discovery or Computer Use cannot access its window, report exactly that boundary, for example:

`Painter is running, but the window/viewport could not be accessed through the available GUI automation path.`

Do not rewrite that as:

`Painter did not create a window.`

Likewise, do not issue a visual PASS without actual visual evidence.

### Retry / token discipline

- For one unchanged GUI-discovery method, make at most two meaningful attempts.
- If the same result repeats, change the evidence source or record the blocker. Do not enter long blind polling loops.
- Do not relaunch Painter repeatedly without new evidence that the previous process is unusable.
- Prefer one check -> result -> conclusion -> next check.
- Stop once the current success criterion is proven. Do not collect redundant evidence.

### Painter-specific startup sequence

For a task that needs Painter automation and then viewport validation, prefer this order:

1. Reuse an existing healthy Painter session when possible; otherwise launch Painter once.
2. Confirm application-level readiness through supported Painter automation when the task uses it.
3. Perform the scripted operation.
4. Only if the acceptance criterion is visual, locate the real Painter window and inspect/capture the viewport.
5. If visual access is unavailable while Painter remains alive, record a GUI-automation blocker and preserve the non-visual evidence separately.

### Repository searches and fixes

When a task exposes a false negative caused by GUI detection, inspect scripts/docs in scope for repeated use of the same weak predicate (`MainWindowHandle`, `MainWindowTitle`, one-shot window enumeration) and fix the reusable rule rather than adding a one-off exception for Painter.

## Validation language

Keep evidence classes explicit:

- confirmed by our pipeline/runtime;
- confirmed by current external source;
- hypothesis;
- blocked/unresolved.

Never promote `blocked/unresolved` to FAIL of the underlying application behavior unless the failing condition itself was directly observed.
