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

### Stage 1B — Prepare + compile — IMPLEMENTED, PENDING LOCAL COMPILE TEST

The first end-to-end build slice is now implemented behind `PREPARE + COMPILE`.

Current behavior:

- takes artist-owned top-level DMX files from the current saved project;
- derives the addon name deterministically from the project name;
- derives the replacement VMDL/VMDL_C resource path from the retail main model discovered by extraction;
- creates only the required generated subtree under CSDK `content\citadel_addons\<addon>` and does not modify the artist's root files;
- refreshes the generated DMX copy under the target model directory;
- creates a minimal ModelDoc29 VMDL containing the project's DMX render meshes instead of copying the full retail VMDL with potentially unsupported nodes;
- scans binary/text DMX bytes for the confirmed Wall Worm `materials/models/...` material-path defect and emits narrow VMDL material remaps to `models/...` without rewriting the artist DMX;
- invokes the experimentally validated `game\bin_cs2\win64\resourcecompiler.exe -i <vmdl> -nop4`;
- requires the expected compiled `.vmdl_c` to exist before reporting compile success;
- searches the decompiled `0source` VMDL data for an original `.vnmskel` reference;
- when that reference and DeadlockTools are available, invokes the proven `DeadlockTools add ag2` shape using the selected hero, the family inferred from the skeleton path, and `--override-skeleton`;
- stores generated source/compiled model paths in the project manifest;
- writes a deterministic build log under `.deadlimit\logs`.

The local smoke test is deliberately the next gate. It must establish one concrete result before additional fixes are added:

```text
artist DMX in project root
→ PREPARE + COMPILE
→ ResourceCompiler result
→ expected VMDL_C
→ AG2/NmSkeleton post-process result
```

Known boundary of this first implementation:

- `fix unitstatus` is not run yet. It remains conditional because the previous proven compile reported `Data is not an array! Aborting...`, indicating the fix was not applicable to that output;
- a separate structural retail-VMDL node remover is not invoked in this first path because the generated minimal VMDL avoids importing those retail-only ModelDoc nodes. If the local compile demonstrates that a required node is missing, add only the specific evidence-backed structure next;
- full material/texture authoring remains Stage 2.

Acceptance for Stage 1 overall: the current known-good replacement model can be rebuilt from artist source with one Deadlimit action and without manually opening ModelDoc for compilation.

## Stage 2 — Authoring workspace and materials

Goal: support the intended intermediate shader/material authoring stage.

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
