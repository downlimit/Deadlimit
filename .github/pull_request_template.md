## Summary

Describe the problem and the user-visible result.

## Validation

- [ ] I ran the relevant automated checks and listed them below.
- [ ] I disclosed checks that require external tools and could not be run.
- [ ] I added or updated tests for behavior changes where practical.

Checks run (specify `Fast` / `Full`, exit status, local log path, manually dispatched Actions, and unavailable game/CSDK checks):

```text

```

- [ ] I ran `internal/tests/ci-policy-smoke.ps1` for workflow changes or disclosed why it could not run.
- [ ] I introduced no unapproved Actions artifacts, cache, expensive automatic jobs, or publication triggers. See `internal/docs/CI_POLICY.md`.

## Provenance and safety

- [ ] My commits include a DCO `Signed-off-by` trailer.
- [ ] I have the right to submit every code, documentation, fixture, and artwork change under the repository's MIT license.
- [ ] This pull request contains no retail game content, Reduced CSDK content, extracted `0source`, VPK/compiled Source 2 resources, third-party binaries, personal projects, credentials, or private data.
- [ ] I documented any new download source, executable invocation, or external trust boundary.

## Compatibility

List the Windows, Deadlimit, Deadlock, CSDK, MAXScript host, Wall Worm, and other
versions used for manual validation when they are relevant.
