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

### Stage 1A.1 — Hero extraction validation slice — IMPLEMENTED, PENDING REAL OUTPUT TEST

Implemented immediately after 1A because later Prepare requires reliable retail model/reference discovery:

- `EXTRACT HERO SOURCE` button in the desktop UI;
- isolated Source 2 Viewer CLI adapter;
- persisted one-time CLI location;
- scan current retail Deadlock VPKs instead of hardcoding one hero path;
- prioritize current `game\citadel\pak01_dir.vpk` and exact hero-model filename matches;
- decompile the discovered hero resource folder into hidden staging;
- publish to `<ProjectFolder>\0source\` only after successful non-empty extraction;
- preserve the previous extraction as hidden `.deadlimit\0source.previous` during refresh;
- persist discovered retail main model/VPK, Source 2 Viewer version, extraction time, and file count.

The first real-project output test must establish whether folder-level decompilation already yields the complete useful model/render-mesh/material/texture set or whether dependency closure must be expanded. See `EXTRACTION.md`.

### Stage 1B — Prepare + compile

After the extraction validation test:

- safely preprocess source VMDL where required;
- normalize the confirmed Wall Worm `materials/models/...` defect narrowly;
- create the generated CSDK addon workspace without modifying artist originals;
- invoke the validated CSDK12 `bin_cs2` ResourceCompiler;
- locate the resulting addon `.vmdl_c`;
- apply `DeadlockTools add ag2` with discovered original refs;
- make `fix unitstatus` conditional/no-op when not applicable;
- verify expected graph/skeleton references;
- produce a clear success/failure report.

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
