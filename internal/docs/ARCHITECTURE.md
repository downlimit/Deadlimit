# Deadlimit Manager — Architecture

## Environment roots

Deadlimit does not require a fixed workstation layout. The paths below are
examples; the user selects the actual locations in **Settings**.

Deadlock modding workspace:

```text
<workspace>\
```

Reduced CSDK12:

```text
<workspace>\Reduced_CSDK_12\
```

DeadlockTools:

```text
<workspace>\DeadlockTools\
```

Retail Deadlock:

```text
<SteamLibrary>\steamapps\common\Project8Staging\
```

Deadlimit local repository:

```text
<workspace>\Deadlimit\
```

Always distinguish:

```text
Retail:
<SteamLibrary>\steamapps\common\Project8Staging\

CSDK source:
<CSDKRoot>\content\

CSDK compiled output:
<CSDKRoot>\game\
```

## Proposed implementation

Primary application: C#/.NET desktop/CLI core.

Reasoning:

- DeadlockTools is already C#/.NET;
- process execution and exit-code handling are straightforward;
- JSON project manifests are easy to maintain;
- structured VMDL/KV3 processing is safer than regex-based rewriting;
- a GUI can be layered over the same build core later.

`.cmd` files may exist only as convenience launchers. Build logic belongs in the application/core library.

## Project manifest

Each mod project should have persistent metadata containing at least:

- project name;
- source working folder;
- hero identity;
- discovered retail main model;
- source VMDL path;
- compiled VMDL path;
- original AnimGraph2 references;
- original NmSkeleton reference;
- material classification and custom material paths;
- release/deploy configuration.

Hero-specific paths should be discovered and persisted rather than hardcoded into generic build logic.

## Pipeline

### Extraction

Retail VPK/resources
→ resource discovery
→ ValveResourceFormat/Source 2 Viewer CLI adapter
→ decompiled/extracted working source
→ extraction manifest

### Prepare / authoring

User DMX + textures
→ validate
→ normalize known exporter path defects where the rule is demonstrably general
→ prepare source VMDL
→ remove/bypass source blocks that current CSDK cannot deserialize when required
→ classify REUSE vs CUSTOM materials
→ create only missing custom material scaffolding
→ compile intermediate resources
→ allow Material Editor / ModelDoc authoring

### Release

Authored source workspace
→ validate
→ ResourceCompiler
→ compiled model post-processing
→ reference verification
→ VPK packaging
→ optional deploy/test

## Confirmed technical facts from current experiments

These are implementation evidence, not universal assumptions beyond their stated scope.

### Headless model compilation

For the current CSDK12 installation, this compiler successfully compiled the tested replacement model from the command line:

```text
<CSDKRoot>\game\bin_cs2\win64\resourcecompiler.exe
```

with `-i <source.vmdl> -nop4`.

The tested compile completed with `1 compiled, 0 failed`.

The `game\bin_tools\win64\resourcecompiler.exe` variant aborted at startup in the same environment with a particles schema mismatch, so Deadlimit Aggregator must not treat the two binary sets as interchangeable without validation.

### AG2 post-processing

The tested compiled replacement model required DeadlockTools `add ag2` after compilation to restore original gameplay/UI AnimGraph2 and NmSkeleton references.

The current `fix unitstatus` command is conditional rather than universally required. In the tested `bin_cs2` output it reported `Data is not an array! Aborting...`, which means the specific structural defect it fixes was not present in that build.

Deadlimit Aggregator should inspect/attempt the fix conditionally and treat an already-correct representation as a no-op, not as a fatal build failure.

### Wall Worm material path normalization

With Wall Worm Pro 7.35.1, a tested PBR material stored correctly in Max as:

```text
models/heroes_staging/.../material.vmat
```

was exported into DMX as:

```text
materials/models/heroes_staging/.../material.vmat
```

and ResourceCompiler identified that form as an illegal/missing material path.

Changing `Full Material Names` did not alter the exported result in the test.

A generic normalization candidate is therefore:

```text
materials/models/...
→ models/...
```

This rule must be implemented narrowly; valid paths such as `materials/dev/...` or project-owned `materials/<project>/...` must not be globally stripped.

## Source transformation safety

Do not use blind regex for nested VMDL/ModelDoc object removal.

The preprocessor must understand enough of the KV3/ModelDoc structure to remove complete nodes while preserving balanced objects/arrays and quoted strings.

Every destructive/preprocessing operation should:

- operate only inside the current project workspace/CSDK addon;
- preserve the user's original source folder;
- create deterministic logs;
- fail closed when the target path or structure is ambiguous.

## Material ownership

REUSE materials remain references to retail resources.

CUSTOM materials belong to the addon source tree. Deadlimit Aggregator may initialize them once, but must never overwrite an existing authored VMAT during normal prepare/release operations.

## External tool adapters

Deadlimit Aggregator should isolate external commands behind adapters so tool/version changes are localized:

- ValveResourceFormat / Source 2 Viewer CLI;
- Reduced CSDK ResourceCompiler;
- CSDK VPK packer;
- DeadlockTools;
- optional launch/deploy integration.

External CLI syntax should not be duplicated throughout the application.
