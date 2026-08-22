# Deadlimit — Roadmap

## Stage 0 — Repository and technical baseline

Goal: preserve project context and establish reproducible development workflow.

- initialize repository and documentation;
- connect local `C:\WorkProjects\Deadlock\Deadlimit` workspace;
- define project manifest format;
- add a minimal host and environment diagnostics;
- detect configured CSDK/Deadlock/DeadlockTools paths;
- record tool versions in diagnostics.

## Stage 1 — Prepare + headless model build

Goal: replace the current manual mechanical sequence with one deterministic project/build flow.

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

This accepts the extraction mechanism and destination contract needed by Stage 1B. It does not yet claim complete material/texture/shared dependency closure. That broader extraction completeness remains a Stage 4 concern unless Stage 1B or Stage 2 exposes a concrete missing dependency first. See `EXTRACTION.md`.

### Stage 1B — Prepare + compile — ACCEPTED FOR CURRENT PROJECT

Validated locally on 2026-08-22 with a real artist DMX:

- `PREPARE + COMPILE` completed from the Deadlimit GUI;
- generated the CSDK addon workspace without modifying the artist root input;
- generated a minimal VMDL;
- exercised three automatic `materials/models/... → models/...` material remaps;
- the validated `bin_cs2` ResourceCompiler produced the expected addon `.vmdl_c`;
- DeadlockTools post-processing completed and restored AG2/NmSkeleton references;
- the exact hero-specific skeleton path used by this test is recorded in `BUILD.md` and must not be generalized to other heroes;
- a deterministic build log was written under the project `.deadlimit\logs` folder.

This accepts the current project's headless prepare/compile/post-process slice. Rendering/material correctness still requires the intermediate CSDK authoring inspection, and retail loading still requires the later VPK/deploy stages.

The current implementation behavior remains:

- take artist-owned top-level DMX files from the current saved project;
- derive the addon name deterministically from the project name;
- derive the replacement VMDL/VMDL_C resource path from the retail main model discovered by extraction;
- create only the required generated subtree under CSDK `content\citadel_addons\<addon>`;
- refresh the generated DMX copy under the target model directory;
- create a minimal ModelDoc29 VMDL containing the project's DMX render meshes;
- scan DMX bytes for the confirmed Wall Worm `materials/models/...` material-path defect and emit narrow VMDL material remaps without rewriting the artist DMX;
- invoke `game\bin_cs2\win64\resourcecompiler.exe -i <vmdl> -nop4`;
- require the expected compiled `.vmdl_c` to exist before reporting compile success;
- search `0source` VMDL data for an original `.vnmskel` reference;
- apply the proven `DeadlockTools add ag2` path when sufficient references are discovered;
- persist generated source/compiled paths and build logs.

Known boundary:

- `fix unitstatus` remains conditional and is not run by the current path because the earlier tested output showed the fix was not applicable;
- full material/texture authoring remains Stage 2;
- cross-hero AG2/skeleton discovery is not yet proven.

## Stage 2 — Authoring workspace and materials — NEXT

Goal: support the intended intermediate shader/material authoring stage.

Immediate next validation:

```text
open generated addon in CSDK12
→ inspect generated VMDL/model
→ verify reused retail materials
→ identify custom material slots
→ establish custom VMAT + texture authoring behavior
→ preserve authored VMAT changes across rebuilds
```

Then automate:

- classify imported model materials as REUSE or CUSTOM;
- preserve retail material references;
- create addon-owned VMAT scaffolding for new custom materials;
- create/organize texture source directories and descriptors where required;
- never overwrite existing authored custom VMAT files;
- provide an action to open the relevant Source 2 authoring tools;
- compile an intermediate preview result for inspection.

Acceptance: a project containing original hero body materials plus a custom costume material can be prepared, edited in Material Editor, saved, closed, and rebuilt without losing authored material changes.

## Stage 3 — VPK release pipeline

Goal: one Release action from authored workspace to testable package.

- invoke the current CSDK VPK packer;
- validate packaged resources;
- implement explicit release naming/target configuration;
- avoid inventing automatic pak-slot policy until actual loader requirements are confirmed;
- output the final VPK to a configured build directory;
- optionally deploy to the configured Deadlock addons/test location.

Acceptance: `Release` produces the same testable result as the known-good manual packaging workflow.

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

A minimal Windows desktop shell started during Stage 1A so the project-folder contract could be tested through the intended user interaction rather than through manual CLI commands.

Final primary actions:

```text
EXTRACT HERO
NEW PROJECT
PREPARE FOR AUTHORING
OPEN AUTHORING TOOLS
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
