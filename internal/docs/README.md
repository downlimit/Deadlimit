# Deadlimit

Deadlimit is a Windows desktop tool for building and testing Deadlock character replacement mods with minimal manual Source 2/CSDK work.

## Target workflow

1. **Extract** — choose a Deadlock hero and export/decompile the relevant source assets into a working folder.
2. **Author** — point Deadlimit at a folder containing DMX model files and textures; create a project; prepare a CSDK workspace; open the model/material authoring stage for shader and texture setup.
3. **Release** — compile changed resources, apply required post-processing, validate the build, package a VPK, and place it in the configured test/deploy location.

The user-facing goal is that routine iteration becomes:

```text
Export DMX / save textures
→ Deadlimit: Release
→ test in Deadlock
```

Technical details and current constraints are documented in `PROJECT.md`, `ARCHITECTURE.md`, and `ROADMAP.md`.
