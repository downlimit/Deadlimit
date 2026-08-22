# Deadlimit — Roadmap

## Stage 0 — Repository and technical baseline

Goal: preserve project context and establish reproducible development workflow.

- initialize repository and documentation;
- connect local `C:\WorkProjects\Deadlock\Deadlimit` workspace;
- define project manifest format;
- add a minimal host and environment diagnostics;
- detect configured CSDK/Deadlock/DeadlockTools paths;
- record tool versions in diagnostics.

## Stage 1 — Project ingestion and CSDK authoring preparation

Goal: replace the current manual mechanical setup with one deterministic artist-facing flow.

### Stage 1A — Artist project shell — ACCEPTED

Validated locally on 2026-08-22:

- create/open a Deadlimit project from an existing artist folder;
- scan top-level DMX files and PNG textures without moving or modifying them;
- persist project name, hero, optional release ID, discovered assets, and future pipeline metadata;
- keep Deadlimit metadata in hidden `.deadlimit\project.json`;
- remember and reopen the last project;
- reserve `0source` as the on-demand retail hero extraction destination without creating it during ordinary project creation.

Acceptance was confirmed with a real project: project metadata survived close/relaunch and the same project reopened automatically.

The exact project-folder contract is documented in `WORKSPACE.md`.

### Stage 1A.1 — Hero extraction validation slice — ACCEPTED FOR MODEL-FOLDER EXTRACTION

Validated locally on 2026-08-22:

- `EXTRACT HERO SOURCE` runs from the desktop UI;
- pinned in-process `ValveResourceFormat 20.0.6980` integration works; no separate Source2Viewer CLI is required;
- current retail Deadlock VPK discovery found the selected hero;
- extraction published a non-empty `<ProjectFolder>\0source\`;
- the resulting hero source folder contained the decompiled main VMDL and many DMX resources;
- existing artist root assets were not part of the extraction transaction.

This accepts the extraction mechanism and destination contract needed by Stage 1B. It does not yet claim complete material/texture/shared dependency closure. That broader extraction completeness remains a Stage 4 concern unless an earlier stage exposes a concrete missing dependency first. See `EXTRACTION.md`.

### Stage 1B — Prepare for CSDK authoring — CURRENT

The earlier headless `PREPARE + COMPILE` experiment successfully proved that the current project's DMX can compile and that the Wall Worm material-remap mechanism is exercised. That experiment is retained as evidence in `BUILD.md`, but it is no longer the intended authoring workflow.

Current architecture:

```text
artist project root
→ Deadlimit PREPARE FOR CSDK
→ Reduced_CSDK_12\content\citadel_addons\<addon>
→ launch CSDK12
→ CSDK tools compile content into game automatically
```

`PREPARE FOR CSDK` must:

- take artist-owned top-level DMX files from the saved project;
- derive the addon name and replacement model resource path;
- prepare/update only `content\citadel_addons\<addon>`;
- copy required retail authoring source context from `0source`;
- generate the project-owned render-mesh node(s);
- generate the narrow Wall Worm material remaps without rewriting artist DMX;
- keep current CSDK12-incompatible `NmSkeletonList` and `AnimGraph2List` out of source ModelDoc;
- persist the prepared source VMDL path and logs;
- never delete or compile `game\citadel_addons\<addon>` during this action.

CSDK12 owns normal authoring compilation from `content` to `game`. Direct runtime compilation and post-compile AG2 patching move to the later Release/Test transaction, where Deadlimit can control the full operation intentionally.

Acceptance for Stage 1B:

```text
PREPARE FOR CSDK
→ launch CSDK12
→ addon source appears correctly
→ CSDK compiles it
→ model is inspectable in authoring tools
```

Current visual issues under investigation are recorded in `BUILD.md`: black eyes and missing sequence preview.

## Stage 2 — Authoring workspace and materials — NEXT

Goal: support the intended intermediate shader/material authoring stage.

Immediate next validation:

```text
open prepared addon in CSDK12
→ inspect generated VMDL/model
→ verify reused retail materials
→ identify custom material slots
→ establish custom VMAT + texture authoring behavior
→ preserve authored VMAT changes across later prepare/release operations
```

Then automate:

- classify imported model materials as REUSE or CUSTOM;
- preserve retail material references;
- create addon-owned VMAT scaffolding for new custom materials;
- create/organize texture source directories and descriptors where required;
- never overwrite existing authored custom VMAT files;
- provide an action to open the relevant Source 2 authoring tools.

Acceptance: a project containing original hero body materials plus a custom costume material can be prepared, edited in Material Editor, saved, closed, and reopened without losing authored material changes.

## Stage 3 — Release / test pipeline

Goal: one explicit Release action from authored `content` workspace to testable package.

- invoke the appropriate CSDK compile/build mechanism for the addon;
- ensure runtime `game` output is fresh for this explicit release transaction;
- apply evidence-backed post-compile AG2/NmSkeleton fixes where required;
- validate packaged resources;
- invoke the current CSDK VPK packer;
- implement explicit release naming/target configuration;
- avoid inventing automatic pak-slot policy until actual loader requirements are confirmed;
- output the final VPK to a configured build directory;
- optionally deploy to the configured Deadlock addons/test location.

Acceptance: `Release` produces the same testable result as the known-good manual packaging workflow while keeping the authoring `content` tree authoritative.

## Stage 4 — Extract Hero completion

Goal: complete the extraction slice into a robust modding-ready retail-source module.

- enumerate/select heroes from current retail resources;
- validate main-model discovery across multiple heroes;
- resolve dependency paths beyond the main hero folder when real output proves this is necessary;
- extract/decompile model/render meshes;
- extract relevant materials and texture dependencies;
- preserve retail resource paths in extraction metadata;
- make animations optional;
- allow the extracted source to coexist with artist-owned DMX/PNG files in the project root.

Acceptance: a user selects a hero and requests source extraction, and `0source` receives a consistent current retail source package suitable for reference/3ds Max/material work.

## Stage 5 — Desktop UI completion

Goal: expose the completed core workflow without requiring command-line knowledge.

Final primary actions:

```text
EXTRACT HERO
NEW PROJECT
PREPARE FOR CSDK
OPEN CSDK
RELEASE
RELEASE & TEST
```

The UI should surface technical logs only when needed for diagnosis.

## Later work

- incremental/cache-aware builds;
- project templates;
- automatic tool update compatibility checks;
- richer dependency visualization;
- multi-model/bodygroup support;
- optional mod deployment management once loader constraints are proven.
