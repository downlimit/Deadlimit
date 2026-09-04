# Deadlimit Manager — Decisions

This file records decisions that should remain stable unless new evidence justifies changing them.

## Product decisions

### Deadlimit Aggregator is an artist-facing workflow tool

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

Deadlimit Aggregator should eventually be able to select a retail Deadlock hero and create a modding-ready source folder containing the model and relevant dependencies.

Hero resource paths must be discovered from current retail data rather than assumed from a fixed template.

## Technical decisions

### Primary implementation

Use C#/.NET for the application/core.

External tools are accessed through isolated adapters rather than duplicating command-line syntax throughout the codebase.

Target adapters include:

- Reduced CSDK authoring/compile integration;
- DeadlockTools;
- ValveResourceFormat;
- CSDK VPK packaging;
- optional deploy/test launcher integration.

### CSDK owns `content → game` during authoring

The authoritative editable workspace is:

```text
Reduced_CSDK_12\content\citadel_addons\<addon>\
```

`Reduced_CSDK_12\game\citadel_addons\<addon>\` is generated runtime output owned by CSDK12. Current CSDK12 documentation explicitly states that files placed in addon `content` are compiled by the tools and their compiled `_c` resources are written into the corresponding addon `game` folder.

Therefore `PREPARE FOR CSDK` must:

- prepare/update only addon `content`;
- never delete the addon `game` folder;
- never invoke ResourceCompiler itself;
- never apply post-compile binary patches during this authoring-preparation action.

Compilation, runtime-output cleanup/rebuild policy, and any post-compile patching belong to a later explicit release/test action, where Deadlimit Aggregator can control the entire transaction intentionally.

### Embed ValveResourceFormat for extraction

Deadlimit Aggregator must not require the artist to install or locate the separate `Source2Viewer-CLI.exe` merely to extract retail resources.

The normal Source 2 Viewer executable is the GUI application; the official command-line utility is a separate binary named `Source2Viewer-CLI`. Because the official CLI interface explicitly does not guarantee argument stability, Deadlimit Aggregator uses ValveResourceFormat as an in-process library instead of automating the CLI.

The current pinned dependency is:

```text
ValveResourceFormat 20.0.6980
```

This version targets .NET 10 and was current when the integration was made on 2026-08-22. Upgrading ValveResourceFormat is an explicit compatibility change: extraction must be rebuilt and smoke-tested against the current Deadlock retail resources before the pinned version is changed.

The Source 2 Viewer GUI remains useful as a manual inspection/reference tool, but it is not a runtime prerequisite for Deadlimit Aggregator extraction.

### Validated ResourceCompiler path is retained as release-stage evidence

The current known-good direct compiler from the earlier headless experiment is:

```text
<CSDKRoot>\game\bin_cs2\win64\resourcecompiler.exe
```

The `game\bin_tools\win64` compiler must not be substituted automatically because it failed in the current environment with a schema mismatch.

This direct compiler path is no longer used by `PREPARE FOR CSDK`. It remains evidence for a future controlled release/test pipeline if direct compilation is still needed there.

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

After a controlled runtime compilation step, Deadlimit Aggregator may verify required graph/skeleton references and invoke DeadlockTools `add ag2` when the compiled model needs them restored.

`fix unitstatus` is conditional and must not be treated as universally required.

AG2/NmSkeleton post-processing does not belong to `PREPARE FOR CSDK`, because that action no longer owns compiled `game` output.

### Originals are immutable inputs

Deadlimit Aggregator should perform generated/preprocessed work inside project/CSDK workspaces rather than destructively editing the artist's original source folder.

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
Deadlimit Manager
Deadlimit Updater
```

Implementation, documentation, icons, and source code live under the hidden `internal` directory. Git metadata and technical launchers are also hidden locally.

The repository itself remains complete; hiding files in Explorer is only a local presentation choice.

`Deadlimit.cmd` remains as a neutral compatibility shim for older local
shortcuts. The obsolete `DeadlimitAggregator*` entry points were retired before
the first public release. New user-facing launch and shortcut surfaces use the
Deadlimit Manager name.

## Documentation continuity

Project knowledge must not depend on one chat remaining available.

Whenever a discussion produces information that could materially affect later implementation, diagnosis, architecture, product scope, workflow, compatibility handling, or testing, that information should be written into the repository documentation during the same work session.

Examples of information worth persisting include:

- confirmed experimental results;
- rejected approaches and why they were rejected;
- important hypotheses that still require testing;
- newly discovered external-tool constraints or compatibility issues;
- decisions that change the intended user workflow;
- known bugs and their current status;
- assumptions that future code depends on;
- hero-specific exceptions that must not be generalized;
- exact commands or paths when they are part of a proven pipeline;
- next-step rationale when it would otherwise be lost with chat context.

Information should be placed by role rather than accumulated in one giant file:

- `CONTEXT.md` — current project state, known problems, active hypotheses, and what has been learned;
- `DECISIONS.md` — decisions intended to remain stable until new evidence overturns them;
- `ARCHITECTURE.md` — technical structure, invariants, adapters, data flow, and implementation constraints;
- `ROADMAP.md` — sequencing, stage goals, acceptance criteria, and unresolved work;
- `PROJECT.md` — product purpose and user-facing workflow.

Documentation updates should preserve the distinction between:

1. confirmed by our pipeline;
2. confirmed by current external evidence;
3. hypothesis requiring a targeted test.

Do not record speculation as fact. Do not erase older confirmed context merely because a newer idea exists; superseded conclusions should either be updated with the new evidence or explicitly marked as superseded when the history matters.

## Decision rule for future fixes

Every proposed workaround should be classified before being generalized:

1. confirmed by our pipeline;
2. confirmed by current external tool/source evidence;
3. hypothesis requiring a targeted test.

A hero-specific or one-file workaround must remain scoped until a separate test demonstrates that it is general.
