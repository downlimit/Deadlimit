# Deadlimit: CI minutes, storage and publication policy

Adopted 2026-09-29 from [MICROMACRO's portable agent policy](https://github.com/downlimit/MICROMACRO/blob/main/docs/AGENT_CI_POLICY_TEMPLATE.md). This repository-specific policy governs workflow and agent changes. It does **not** change the artist's in-app PREPARE / BUILD & TEST operation, which executes on their computer.

## Baseline and changes (main at e98496ce, 2026-09-26)

| Workflow / job | Before | After | Runner / timeout / concurrency |
| --- | --- | --- | --- |
| `build.yml` / build | main push + PR | manual dispatch | Windows / 40 min / cancel replaced check |
| `hero-texture-smoke.yml` / hero-texture-smoke | main push + PR; redundant restore/build | manual dispatch, existing tests kept | Windows / 25 min / cancel replaced check |
| `launch-game-fastpath-smoke.yml` / smoke | main push + PR; redundant restore/build | manual dispatch, existing tests kept | Windows / 20 min / cancel replaced check |
| `ui-agent-contract-smoke.yml` / ui-agent-contract | main push + PR | manual dispatch | Windows / 10 min / cancel replaced check |
| `dco.yml` / dco | PR | PR, unchanged validation | Ubuntu / 5 min / cancel superseded PR check |
| `branch-hygiene.yml` / delete-merged-pr-branch | PR closed | PR closed, only same-repo merged PR | Ubuntu / 5 min / do not cancel |
| `release.yml` / publish | automatic on completed Build and manual | manual with explicit `confirm_publish`; fail fast unless exact SHA has successful `build` and `smoke` checks | Ubuntu / 10 min / do not cancel |

Every workflow has top-level least-privilege permissions and a workflow/ref concurrency group. The source milestone publishes only an installer CMD and its SHA-256; it must never run automatically after a normal build. No job uses `upload-artifact` or an Actions cache. The full Build's original checks, including root-shortcut presentation and updater startup checks, remain in the manually dispatched hosted workflow. Other suites remain separately dispatchable.

Repository Actions history showed **3,548 workflow runs** when queried on 2026-09-29. A recent merged PR had four concurrent Windows runs on PR and another four on main push, followed by a release run. This is repo-specific run count, not account-level billable usage. The available GitHub connection could read run history and Releases but not its artifact/cache/settings endpoints: current retained artifact and cache counts, account-minute spend, and billing quotas are **unverified**. The Releases API returned `v0.1.0-beta.3` with `Install-Deadlimit.cmd` and its checksum (12,581 and 88 bytes). This is not an inventory of Actions artifacts. No historical objects were deleted.

## Local-first checks

On Windows with .NET 10 SDK and PowerShell (PowerShell 7 preferred), run from any directory:

```powershell
.\internal\Run-Local-Checks.ps1 -Scope Fast
.\internal\Run-Local-Checks.ps1 -Scope Full
```

`Fast` runs the CI cost guard, open-source/content/path/installer/critical-stabilization/UI checks. `Full` restores and builds Deadlimit Manager in Release, runs the available smoke scripts spanning Build, hero texture, launch fastpath and UI suites, then runs `--startup-smoke`. Both save timestamped transcripts under the OS temp directory in `Deadlimit-local-checks`, stop at the first failure, and return process exit code 1 for failure. They cannot prove real Deadlock/CSDK/3ds Max/Painter behavior; report integration validation separately. The hosted full Build retains its additional root-presentation assertions. Neither local mode dispatches Actions, uploads binaries or changes account settings.

Run Fast after routine edits. Run focused relevant tests or Full before publication and for native app changes. Manually dispatch `Build` and `Launch game fastpath smoke` on the exact intended commit. `Publish source milestone` requires their named checks to succeed and the publication checkbox. Never skip failing checks to reduce minutes.

## Storage and account-level limits

- Default: no Actions artifacts or caches. Never upload `dist/`, `node_modules/`, `target/`, `bin/`, `obj/`, complete archives, release binaries, VPK, retail/CSDK materials, source 3D projects, unredacted logs/screenshots, or credentials to CI. Preserve Git-based installation/updating; do not duplicate the same payload in Actions artifacts, Releases and other services.
- If a temporary artifact is necessary: identify a consumer, reduce contents, measure actual size, obtain owner approval, begin with **25 MiB total per run** as a proposed ceiling, and set `retention-days: 1` and a cleanup owner. Larger artifacts require a size × duration exception. Do not upload unbounded output via `if: always()`.
- No `actions/cache`, `setup-*` cache, NuGet or other hosted cache until actual storage cost and time saving have been measured and an exception recorded here. Packages require separate review.
- A new workflow, trigger, permission, timeout, artifact/cache, concurrency policy or release mechanism requires updating this inventory and fail-closed guard `internal/tests/ci-policy-smoke.ps1` in the same PR. Avoid routine cloud jobs on intermediate agent commits. If mandatory PR checks become necessary, keep them short, path-filtered and free of duplicated push runs.
- Replaceable manual checks use `cancel-in-progress: true`. Publication and branch deletion cannot cancel ongoing irreversible operations. Do not automatically retry failed expensive runs.

## Owner actions (not performed by this change)

1. Review Settings → Actions → General; consider **7 days** retention for new workflow logs and artifacts. Changing retention does not delete historical artifacts; inspect them separately.
2. Review Settings → Billing & Licensing → Budgets and alerts and account-wide usage. If paid overage is disallowed, set or retain a **$0 blocking budget**; review cache and Packages usage independently. Available allowances depend on plan and billing period.
3. Check quota before manually starting expensive builds. At organizational thresholds of approximately **70%** usage investigate and **85%** usage suspend optional jobs pending review. These are internal guidance, not GitHub-enforced limits.
4. Once each billing period inspect account-wide usage, workflow schedules, artifacts, caches, Packages and Releases. Delete only individually verified disposable objects after approval. Do not implement a paid daily cleanup workflow.
5. Inspect branch protection after removing automatically triggered Build checks. If a rule requires those job names on every PR, modify that rule with owner approval or restore a deliberately budgeted minimal PR check. The GitHub connector could not read branch protection (403) and returned no repository rulesets at the queried endpoint. No administrative setting was changed.

## Verification contract

YAML/static checks, a native Windows local run, a GitHub-hosted run, and real Deadlock/CSDK acceptance are distinct evidence. Report exact completed levels. Do not claim billing, retention, cleanup, branch protection or hosted runs have changed or passed without separate verification. Publishing, credentials and store actions always require owner approval.
