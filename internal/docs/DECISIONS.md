# Deadlimit — Decisions

This file records decisions that should remain stable unless new evidence justifies changing them.

## Product decisions

### Deadlimit is an artist-facing workflow tool

The tool exists to remove mechanical Source 2/Deadlock modding work while keeping artistic authoring explicit.

The user should not be required to manually invoke ResourceCompiler, edit compiled references, package VPKs, or repeatedly copy build files during normal iteration.

### Preserve an intermediate authoring stage

The workflow is not intended to be a completely opaque one-click converter from DMX to VPK.

The artist must be able to inspect the prepared result, edit custom materials/shaders in Source 2 tools, save those edits, and later perform a deterministic release build.

### Support both retail reuse and project-owned materials

Materials are classified as:

- `REUSE` — reference the existing retail resource;
- `CUSTOM` — owned by the addon/project and editable by the artist.

Routine builds must never overwrite an existing authored custom VMAT.

### Extraction is a first-class module

Deadlimit should eventually be able to select a retail Deadlock hero and create a modding-ready source folder containing the model and relevant dependencies.

Hero resource paths must be discovered from current retail data rather than assumed from a fixed template.

## Technical decisions

### Primary implementation

Use C#/.NET for the application/core.

External tools are accessed through isolated adapters rather than duplicating command-line syntax throughout the codebase.

Target adapters include:

- Reduced CSDK ResourceCompiler;
- DeadlockTools;
- ValveResourceFormat / Source 2 Viewer;
- CSDK VPK packaging;
- optional deploy/test launcher integration.

### Use the validated compiler path

The current known-good compiler for the tested pipeline is:

```text
C:\WorkProjects\Deadlock\Reduced_CSDK_12\game\bin_cs2\win64\resourcecompiler.exe
```

The `game\bin_tools\win64` compiler must not be substituted automatically because it failed in the current environment with a schema mismatch.

### VMDL preprocessing must be structural

Nested ModelDoc/KV3 data must not be modified with blind regex removal.

Transformations should understand enough structure to remove complete known-incompatible nodes while preserving balanced objects, arrays, strings, and unrelated data.

### Material path repair must be narrow

The currently demonstrated Wall Worm repair candidate is:

```text
materials/models/...
→ models/...
```

Only matching cases supported by evidence should be normalized. Valid `materials/...` paths must remain untouched.

### AG2/NmSkeleton repair is post-compile and evidence-driven

After model compilation, Deadlimit should verify required graph/skeleton references and invoke DeadlockTools `add ag2` when the compiled model needs them restored.

`fix unitstatus` is conditional and must not be treated as universally required.

### Originals are immutable inputs

Deadlimit should perform generated/preprocessed work inside project/CSDK workspaces rather than destructively editing the artist's original source folder.

### Project state is persistent

Each project should persist discovered paths and configuration in a manifest so later builds do not depend on rediscovering or manually re-entering the same data.

The manifest should eventually include:

- project name;
- source folder;
- hero identity;
- discovered retail main model;
- source and compiled VMDL paths;
- original AnimGraph2/NmSkeleton references;
- material ownership/classification;
- custom material paths;
- release/deploy configuration.

## Repository/local UX decisions

The local repository root is intentionally presented as a minimal user-facing folder.

The visible normal entry points are:

```text
Deadlimit
Updater
```

Implementation, documentation, icons, and source code live under the hidden `internal` directory. Git metadata and technical launchers are also hidden locally.

The repository itself remains complete; hiding files in Explorer is only a local presentation choice.

## Decision rule for future fixes

Every proposed workaround should be classified before being generalized:

1. confirmed by our pipeline;
2. confirmed by current external tool/source evidence;
3. hypothesis requiring a targeted test.

A hero-specific or one-file workaround must remain scoped until a separate test demonstrates that it is general.
