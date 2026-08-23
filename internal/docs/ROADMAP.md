# Deadlimit — Roadmap

## Stage 0 — Repository and technical baseline

Goal: preserve project context and establish reproducible development workflow.

- initialize repository and documentation;
- connect local `C:\WorkProjects\Deadlock\Deadlimit` workspace;
- define project manifest format;
- add a minimal host and environment diagnostics;
- configure CSDK/Deadlock/DeadlockTools paths in local settings;
- record tool versions in diagnostics;
- expose convenience actions for opening the project folder and launching CSDK12.

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

The exact project-folder contract is documented in `WORKSPACE.md`.

### Stage 1A.1 — Hero extraction validation slice — ACCEPTED FOR MODEL-FOLDER EXTRACTION

Validated locally on 2026-08-22:

- `EXTRACT HERO SOURCE` runs from the desktop UI;
- pinned in-process `ValveResourceFormat 20.0.6980` integration works; no separate Source2Viewer CLI is required;
- current retail Deadlock VPK discovery found the selected hero;
- extraction published a non-empty `<ProjectFolder>\0source\`;
- the resulting hero source folder contained the decompiled main VMDL and many DMX resources;
- existing artist root assets were not part of the extraction transaction.

`0source` is the pristine read-only retail baseline for authoring reconstruction. Deadlimit patches only copies in CSDK content.

This accepts the extraction mechanism and destination contract needed by Stage 1B. It does not yet claim complete material/texture/shared dependency closure. That broader extraction completeness remains a Stage 4 concern unless an earlier stage exposes a concrete missing dependency first. See `EXTRACTION.md`.

### Stage 1B — Prepare for CSDK authoring — ACCEPTED FOR CURRENT IVY PIPELINE

Current architecture:

```text
artist project root
→ Deadlimit PREPARE FOR CSDK
→ delete only stale game/citadel_addons/<current addon>
→ reconstruct authoring source from pristine 0source
→ Reduced_CSDK_12\content\citadel_addons\<addon>
→ launch CSDK12
→ CSDK tools compile content into game automatically
```

Validated locally on the current Ivy project:

- artist DMX overlays the intended retail render mesh;
- retail VMDL document/version/root structure is preserved;
- current CSDK12-incompatible `NmSkeletonList` and `AnimGraph2List` source nodes are excluded;
- retail render meshes/bodygroups/LODs remain intact;
- retail `AnimationList` and source animation files survive, restoring sequence preview;
- Wall Worm material-path repairs are generated without modifying the artist DMX;
- the generic dev eye fallback is remapped to the uniquely inferred body material and visually fixes Ivy's black eyes;
- CSDK12 owns normal compilation from `content` to `game`;
- `PREPARE FOR CSDK` does not invoke ResourceCompiler or compiled-binary AG2 fixes.

Stage 1B is accepted for the current Ivy pipeline. Cross-hero validation remains future compatibility work rather than a blocker for Stage 2.

## Stage 2 — Authoring workspace and materials — CURRENT

Goal: support the intended intermediate shader/material authoring stage without destroying artist edits.

### Stage 2A — CUSTOM VMAT scaffold and preservation — IMPLEMENTED, LIVE VALIDATION NEXT

`PREPARE FOR CSDK` now:

- keeps retail materials as REUSE through the existing compatibility routing;
- detects the observed Wall Worm custom-slot form `materials/<custom_name>` with no `.vmat` extension;
- routes each detected custom slot to an addon-owned `materials/<addon>/<custom_name>.vmat`;
- creates a missing custom VMAT by decompiling the uniquely inferred retail body/skin/head/face material as a character-compatible starting scaffold;
- copies project-root PNG inputs to `materials/<addon>/textures/` inside CSDK content;
- never overwrites an existing addon-owned custom VMAT;
- keeps the addon content tree persistent across repeated prepares while cleaning only compiled game output.

Immediate live validation:

```text
Updater
→ PREPARE FOR CSDK
→ expect one CUSTOM material and one newly created VMAT
→ LAUNCH CSDK
→ inspect model/custom material in Material Editor
→ edit + save VMAT
→ close CSDK
→ PREPARE FOR CSDK again
→ expect created 0 / preserved 1
→ reopen CSDK and verify authored VMAT changes survived
```

Acceptance for Stage 2A: the current project can route the custom costume material into a valid editable VMAT, CSDK can compile it, and a second prepare preserves user-authored VMAT changes.

### Remaining Stage 2 work after 2A acceptance

- validate texture source visibility/selection in Material Editor;
- decide whether filename heuristics are reliable enough for optional automatic texture-slot assignment;
- expand CUSTOM classification only when another real DMX encoding is observed;
- provide a direct action to open the relevant material in the Source 2 authoring tools if useful.

Full Stage 2 acceptance: a project containing original hero body materials plus a custom costume material can be prepared, edited in Material Editor, saved, closed, prepared again, and reopened without losing authored material changes.

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
LAUNCH CSDK
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
