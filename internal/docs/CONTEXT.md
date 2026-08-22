# Deadlimit — Context

## What we are building

Deadlimit is a Windows tool that automates the repetitive mechanical steps required to create, iterate, package, and test Deadlock character replacement mods.

The intended user workflow is deliberately high-level:

```text
source folder with DMX + textures
→ create/open Deadlimit project
→ prepare intermediate Source 2 authoring workspace
→ edit materials/shaders if needed
→ Release
→ test in Deadlock
```

A separate extraction workflow should let the user select a retail Deadlock hero and produce a modding-ready source folder without manually navigating VPKs and decompiling dependencies one by one.

## Why we are building it

The current manual workflow contains too many repeated low-level operations:

- locating retail hero resources and dependencies;
- decompiling/extracting source assets;
- fixing exporter/path defects;
- preparing VMDL/ModelDoc source for Reduced CSDK;
- compiling resources with the correct toolchain binary;
- restoring compiled model references that are lost or changed by the compile step;
- creating and maintaining custom materials and textures;
- packaging VPKs;
- copying/deploying the result for testing;
- repeating most of the above after every iteration.

Deadlimit exists to make those operations deterministic and internal while preserving the authoring decisions that actually require an artist.

## Target UX

The normal interface should expose a small number of actions:

```text
EXTRACT HERO
NEW PROJECT
PREPARE FOR AUTHORING
OPEN AUTHORING TOOLS
RELEASE
RELEASE & TEST
```

The user should normally provide only:

- a source folder containing DMX and textures;
- the hero being replaced;
- a project name;
- release/package identity when the final loading convention is settled.

Technical commands, paths, ResourceCompiler invocation, VMDL preprocessing, DeadlockTools post-processing, texture compilation, VPK extraction, and VPK packaging should remain internal unless diagnostics are required.

## Authoring model

Deadlimit must preserve a deliberate intermediate authoring stage.

A replacement can contain both:

- `REUSE` materials — existing retail Deadlock materials used by preserved hero geometry;
- `CUSTOM` materials — project-owned materials used by new costume/skin geometry.

Deadlimit may create the initial scaffold for missing custom materials and texture resources. Once an artist has edited a custom VMAT, routine prepare/release operations must never overwrite it.

## Problems we are solving

### 1. Exported material paths can be invalid for Source 2

In the tested Wall Worm workflow, a valid material reference stored in Max as:

```text
models/heroes_staging/.../material.vmat
```

was exported to DMX as:

```text
materials/models/heroes_staging/.../material.vmat
```

ResourceCompiler rejected that form. A narrow normalization rule is therefore required for the demonstrated `materials/models/...` defect.

This must not become a global `materials/` stripping rule because valid paths such as `materials/dev/...` and addon-owned material paths exist.

### 2. Reduced CSDK cannot necessarily consume retail/source model data unchanged

Some source blocks/resources produced from retail Deadlock are not accepted directly by the available Reduced CSDK toolchain. Deadlimit therefore needs a structural preprocessor that can remove or bypass only known-incompatible nodes while preserving valid ModelDoc/KV3 structure.

Blind regex editing is not acceptable for nested VMDL/ModelDoc data.

### 3. The ResourceCompiler binaries are not interchangeable

In the current environment, the tested `game\bin_cs2\win64\resourcecompiler.exe` successfully compiled the replacement model.

The tested `game\bin_tools\win64\resourcecompiler.exe` aborted during startup with a particle schema mismatch.

Deadlimit must use a validated compiler adapter rather than assuming every ResourceCompiler found in the CSDK installation is equivalent.

### 4. Compiled models can require post-processing

The tested compiled replacement model required DeadlockTools `add ag2` to restore expected AnimGraph2 and NmSkeleton references.

`fix unitstatus` is conditional. In the tested build it reported that the target data was not an array, indicating that the specific defect it repairs was absent. Deadlimit should treat this as a conditional/no-op path rather than a mandatory fatal step.

### 5. Material creation must coexist with retail material reuse

The tool must distinguish ownership. It must not duplicate retail materials just because they are referenced by imported geometry, and it must create project-owned materials only when the artist actually needs them.

### 6. Texture/material authoring must remain editable

The automation should prepare custom textures/materials so that the artist can open them in Source 2 authoring tools, connect masks/maps, tune shader parameters, save, close the tools, and later rebuild without losing those edits.

### 7. Packaging and deployment should become one action

The final release path should compile changed resources, validate the model, perform required post-processing, package the VPK, and place it in the configured test/deploy destination without manual file shuffling.

### 8. Hero extraction should not rely on hardcoded hero paths or a separate CLI install

Retail resource layouts differ and can change. Deadlimit should discover the main model and dependencies from current retail resources, persist the discovered paths, and avoid converting one hero-specific workaround into a universal assumption.

The Source 2 Viewer GUI and `Source2Viewer-CLI` are separate binaries. Requiring the artist to locate the CLI would add exactly the kind of setup/mechanical step Deadlimit is intended to remove. Extraction therefore uses the ValveResourceFormat NuGet library in-process.

## Evidence status

### Confirmed by our current pipeline

- Stage 1A project persistence works locally: a real saved project restored its folder, project name, hero and Release ID after Deadlimit was closed and relaunched;
- `bin_cs2\win64\resourcecompiler.exe` compiled the tested replacement model successfully;
- `bin_tools\win64\resourcecompiler.exe` failed in the same installation with a particles schema mismatch;
- the tested compiled model required DeadlockTools `add ag2` to restore graph/skeleton references;
- `fix unitstatus` was not applicable to that tested compiled output;
- Wall Worm exported the demonstrated `materials/models/...` material path defect;
- changing Wall Worm `Full Material Names` did not fix that defect in the test.

### Confirmed by current external sources — checked 2026-08-22

- Source 2 Viewer is the GUI application and `Source2Viewer-CLI` is a separate command-line binary;
- the official Source2Viewer CLI documentation does not guarantee CLI argument stability;
- ValveResourceFormat `20.0.6980` is the current NuGet package checked for this integration, published 2026-08-17 and targeting .NET 10;
- ValveResourceFormat/ValvePak expose in-process VPK reading and resource decompilation APIs used by the current extraction implementation.

### Working hypotheses to validate before generalizing

- the embedded ValveResourceFormat extraction path works on the current local Deadlock install and produces a useful `0source`;
- hero candidate scoring identifies the intended current main model across heroes;
- folder-local extraction is sufficient for the first artist-source package, or missing shared dependencies can be discovered generically;
- the `materials/models/... → models/...` normalization is general enough to automate when matched narrowly;
- incompatible VMDL blocks can be identified structurally and removed deterministically without hero-specific hardcoding;
- retail graph/skeleton references can be discovered automatically for every supported hero;
- custom VMAT scaffolding can be generated in a generic form that remains compatible with the current Deadlock material pipeline;
- the complete release pipeline can be reduced to one deterministic action after the authoring stage.

### Open questions

- exact texture source/compile conventions that should be generated for custom Deadlock materials;
- which material templates/shaders should be offered by default and which must remain user-selected;
- final VPK naming/slot/loading convention for reliable testing;
- exact deploy location and launch/test automation policy;
- which retail ModelDoc blocks are broadly incompatible with the current Reduced CSDK versus specific to individual assets;
- how much extraction should be included by default: model/render meshes/materials/textures only, or additional animation resources.

## Product constraints

- preserve the user's original source folder;
- fail closed when a destructive transformation is ambiguous;
- keep deterministic logs for preprocessing, compilation, post-processing, packaging, and deployment;
- never overwrite an existing authored custom VMAT during routine builds;
- do not encode hero-specific fixes as universal behavior without evidence;
- avoid user-facing prerequisites that can be embedded or discovered reliably by Deadlimit;
- treat external tool/version compatibility as changeable and revalidate adapters/dependencies when Deadlock, Reduced CSDK, Wall Worm, ValveResourceFormat, or DeadlockTools changes.

## Current development focus

The immediate next check is the first real local `EXTRACT HERO SOURCE` run using embedded ValveResourceFormat.

If the extraction succeeds, inspect the generated `0source` and determine exactly which expected model/render-mesh/material/texture resources are present or missing. That result decides whether dependency discovery must be expanded before Stage 1B.

Stage 1B then continues with:

```text
root DMX/textures
→ generated CSDK addon workspace
→ safe VMDL preprocessing
→ narrow material-path normalization
→ validated ResourceCompiler invocation
→ compiled model discovery
→ required DeadlockTools post-processing
→ verification
```
