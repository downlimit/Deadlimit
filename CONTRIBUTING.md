# Contributing to Deadlimit

Thank you for improving Deadlimit. Independent forks are welcome, and focused
upstream pull requests are encouraged.

## Before contributing

- Use code, documentation, test fixtures, and artwork that you have the right to
  submit under the repository's MIT license.
- Never commit or attach Valve/Deadlock retail content, Reduced CSDK content,
  Wall Worm or Autodesk binaries, extracted `0source` folders, VPK archives,
  compiled Source 2 resources, personal projects, credentials, or account data.
- Reduce bug reports to text logs and original minimal fixtures. Redact user
  names, Steam identifiers, local paths, tokens, and proprietary content.
- AI-assisted contributions are allowed. The contributor remains responsible
  for provenance, correctness, licensing, testing, and review of the result.

## Development environment

The currently tested developer environment is Windows 11 x64 with the .NET 10
SDK. Features that integrate with 3ds Max, Wall Worm, Deadlock, or Reduced CSDK
also require your own properly installed copies of those external tools.

```powershell
git clone https://github.com/downlimit/Deadlimit.git
Set-Location Deadlimit
dotnet restore internal/src/Deadlimit/Deadlimit.csproj
dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore
```

Run the checks used by `.github/workflows/build.yml` before opening a pull
request. At minimum, run the checks relevant to the changed area and report any
check that could not be run because it needs a local application or game.

```powershell
internal/tests/open-source-content-policy-smoke.ps1
internal/tests/prepare-behavior-smoke.ps1
```

## Pull-request workflow

1. Fork the repository and create a short-lived branch from the latest `main`.
2. Keep each pull request focused on one fix or feature.
3. Add or update automated tests for behavior changes.
4. Keep English documentation authoritative. Update Russian documentation when
   the same user-facing guidance exists in both languages.
5. Explain the user-visible effect, validation performed, external tools used,
   and remaining limitations in the pull request.
6. Respond to review and keep the branch mergeable.

## DCO sign-off

Every commit must certify the [Developer Certificate of Origin](DCO). Add a
`Signed-off-by` trailer with Git's `--signoff` option:

```powershell
git commit --signoff -m "Describe the change"
```

The sign-off uses the name and email associated with the commit. It confirms
that you have the right to submit the contribution under the project's license;
it is separate from cryptographic commit signing.

## Review and acceptance

`downlimit` is the initial maintainer and code owner. Passing automation does
not guarantee acceptance. Changes must fit the project's scope, preserve safe
handling of user content, and avoid creating a redistribution channel for
third-party assets.
