# Deadlimit — Roadmap

## Stage 0 — Repository and technical baseline

Goal: preserve project context and establish reproducible development workflow.

- initialize repository and documentation;
- connect local `C:\WorkProjects\Deadlock\Deadlimit` workspace;
- define project manifest format;
- add a minimal CLI host and logging;
- detect configured CSDK/Deadlock/DeadlockTools paths;
- record tool versions in diagnostics.

## Stage 1 — Prepare + headless model build

Goal: replace the current manual mechanical sequence with one deterministic command.

- create/open a Deadlimit project from a source folder;
- discover DMX files and textures;
- support hero/project metadata;
- safely preprocess source VMDL where required;
- normalize the confirmed Wall Worm `materials/models/...` defect narrowly;
- invoke the validated CSDK12 `bin_cs2` ResourceCompiler;
- locate the resulting addon `.vmdl_c`;
- apply `DeadlockTools add ag2` with discovered original refs;
- make `fix unitstatus` conditional/no-op when not applicable;
- verify expected graph/skeleton references;
- produce a clear success/failure report.

Acceptance: the current known-good replacement model can be rebuilt from source with one Deadlimit command and without manually opening ModelDoc for compilation.

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

## Stage 4 — Extract Hero

Goal: create modding-ready source folders from retail Deadlock with minimal manual VRF work.

- enumerate/select heroes from current retail resources;
- discover each hero's actual main model and dependency paths;
- call ValveResourceFormat/Source 2 Viewer through an isolated adapter;
- extract/decompile model/render meshes;
- extract relevant materials and texture dependencies;
- preserve retail resource paths in an extraction manifest;
- make animations optional;
- allow `Create project from extraction`.

Acceptance: a user selects a hero and output folder and receives a consistent working source package suitable for 3ds Max/material work.

## Stage 5 — Desktop UI

Goal: expose the completed core workflow without requiring command-line knowledge.

Primary actions:

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
