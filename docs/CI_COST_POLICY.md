# GitHub Actions cost policy (Deadlimit)

This repository distributes Deadlimit from source through `Install-Deadlimit.cmd` and the Git updater. A hosted native build is a validation step, not a downloadable distribution artifact.

## Execution contract

- Open PRs receive the short Ubuntu `Build / build` policy/provenance check, DCO, and the existing required `Launch game fastpath smoke / smoke` Windows check. Do not re-run these on a merge push. The latter is retained because main branch protection requires the `smoke` check; remove it only after an administrator deliberately changes that requirement.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File internal/tests/validate-local.ps1` from a Windows development checkout before sending routine changes for review. Use `-Full` for a local .NET 10 Release build and the relevant deeper smoke tests. It writes its transcript to the local temporary directory and returns nonzero on failure; never upload the transcript.
- Invoke `Build` manually on the exact branch/ref when hosted native Windows validation is necessary. Its `full-windows` job preserves the prior full test and startup checks. The hero texture, launch-game and UI workflows remain manually available for targeted investigations.
- Source milestones require separate `Publish source milestone` workflow dispatch on current `main`, the explicit `publish-source-milestone` confirmation, and a successful manual `full-windows` check on that SHA. The release contains only the installer CMD and checksum; no ZIP/EXE. Do not publish releases or store/retail credentials automatically.
- Every workflow must have least-privilege permissions, per-job timeouts and a workflow-specific concurrency group. Replaced PR checks may be cancelled; the source publication and branch deletion tasks must not be cancelled in progress. Avoid automatic retries.
- No Actions artifacts, build-output uploads, node_modules/bin/obj/dist/target archives or caches by default. An exception requires an identified consumer, owner approval, actual size review (default combined budget <=25 MiB per run), retention-days: 1 and a documented deletion owner. Larger exceptions need explicit size-by-retention approval. Never upload all files on failure.
- Do not enable setup-node/NuGet/Cargo/Gradle/Actions caches before recording measured benefit and size. A Release attachment has a different retention model from an Actions artifact.
- `internal/tests/workflow-cost-policy-smoke.ps1` is mandatory in the local validation and PR Build checks. New workflows and changes to the approved event inventory require a deliberate update of that check and this policy, in the same PR. Never suppress failed checks to save minutes.

## Workflow inventory and change record (2026-09-29)

| Workflow | Previously | New trigger / runner / limit | Stored artifact or cache |
| --- | --- | --- | --- |
| Build | PR and main push; full Windows build both times | PR: Ubuntu policy/provenance, 7 min; manual: Windows full validation, 45 min | None |
| Hero texture pipeline smoke | PR and main push; Windows restore/build | Manual Windows, 35 min | None |
| Launch game fastpath smoke | PR and main push; Windows restore/build | Required PR Windows and optional manual Windows, 30 min; no main push | None |
| UI agent contract smoke | PR and main push; Windows | Manual Windows, 8 min | None |
| DCO | PR; Ubuntu | PR Ubuntu, 5 min | None |
| Branch hygiene | Merged PR close; Ubuntu | Same event Ubuntu, 5 min; non-cancelling | None |
| Publish source milestone | Successful Build workflow_run or manual; Ubuntu | Manual only, Ubuntu, 10 min; non-cancelling | Release installer CMD + checksum only |

All seven workflows have explicit permissions, concurrency and job timeouts. No active workflow uses upload-artifact or Actions cache. Existing runs and releases are not deleted or modified by this policy. GitHub API returned no artifacts for the two sampled workflow runs (Build run 36261269803 and Hero run 36261174697); that sample is **not** a repository-wide artifact inventory. The repository runs listing reported 3,548 historical runs at review time; this is not an account-level minute or storage measurement. The release listing showed v0.1.0-beta.3 with only the installer CMD and checksum. Package inventories, account billing and budget settings, cache inventory, repository-wide old artifacts, and branch protection were not verifiable with the connected GitHub app.

## Owner-only settings and operating procedure

1. In repository **Settings → Actions → General**, assess reducing default retention of new run logs and artifacts to 7 days. A per-artifact 1-day retention remains required for any approved exception. Lowering retention does not retrospectively clear old storage.
2. In account **Settings → Billing & Licensing → Budgets and alerts**, inspect Actions usage across all repositories. If paid overage is not approved, keep a $0 budget with blocking enabled, and separately review Packages/cache quotas. No agent may alter billing without owner approval.
3. Before discretionary heavy manual runs, inspect remaining account allowance. At indicative 70% and 85% consumption, investigate and pause non-essential runs. These are internal review thresholds, not enforced GitHub limits.
4. Each billing cycle, inspect Actions usage, artifacts, caches, Packages, release assets and old scheduled workflows. Obtain confirmation before deleting any past artifact, package or release. Deletion cannot reverse past billed storage use.
5. Verify branch-protection required check names after this PR: the PR `build` job name is preserved, while the hero texture and UI targeted Windows checks now run manually; required `smoke` remains on PR. The GitHub App could not read or edit the branch-protection rule. If any targeted checks are explicitly required by branch protection, adjust the protection to an approved PR gate before relying on the new route.

## Verification boundaries

A successful policy/local check demonstrates policy text, script contracts and/or Windows development build, respectively. A passing GitHub-hosted run is separate evidence. This policy does not assert any changes to account settings, historical storage or successful hosted validation when no such run was observed.
