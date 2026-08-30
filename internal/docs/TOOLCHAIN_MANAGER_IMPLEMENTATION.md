# Toolchain manager implementation note

The Settings dependency-manager implementation is covered by `SETTINGS.md`; this file intentionally does not duplicate the user-facing contract.

Implementation source:

- `App/SettingsForm.cs`
- `Core/ToolchainDependencyService.cs`

The current network sources are intentionally read at runtime so CSDK generation and documented depot manifests can advance without a Deadlimit UI rename.
