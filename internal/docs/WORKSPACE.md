# Deadlimit Manager — Artist project workspace

This file defines the current project-folder contract used by the artist-facing workflow.

## Working-folder layout

A Deadlimit Manager project points at the artist's existing project folder. Deadlimit Manager does not require the artist to reorganize that folder into a tool-owned hierarchy.

Current expected shape:

```text
<ProjectFolder>\
├─ *.dmx
├─ *.png
├─ 0source\          # current retail hero extraction; generated on demand
├─ 1scene\           # optional artist-owned folder; Deadlimit Manager does not assume or manage it
├─ 6temp\            # optional artist-owned folder; Deadlimit Manager does not assume or manage it
└─ .deadlimit\       # hidden Deadlimit Manager metadata / staging / safety backup
   ├─ project.json
   └─ 0source.previous\   # previous extraction, when a refresh replaces an existing 0source
```

Only the conventions that affect Deadlimit Manager are normative. Folder names such as `1scene` and `6temp` are examples of artist-owned structure and must not be hardcoded as required directories.

## Root asset contract

The project root is the normal handoff point from the DCC/texturing workflow.

For the current Stage 1 implementation Deadlimit Manager scans only the top level of the selected project folder for:

- `*.dmx` model files;
- `*.png` textures.

It records relative file names in the project manifest. Other files and folders are ignored unless a later pipeline stage explicitly needs them.

Deadlimit Manager must not move, rename, overwrite, or copy these artist-owned root assets merely to create/open a project.

## `0source` contract

`0source` is reserved for a current extraction of the selected retail Deadlock hero.

Current intended/implemented behavior:

1. the user clicks `EXTRACT HERO SOURCE`;
2. Deadlimit Manager saves the current project metadata first;
3. Deadlimit Manager uses its embedded pinned ValveResourceFormat library to inspect the current retail Deadlock VPKs; no separate Source2Viewer CLI selection is required;
4. Deadlimit Manager discovers a matching hero `.vmdl_c` from current retail resources;
5. the hero resource folder is decompiled into a hidden staging directory;
6. only after a non-empty extraction does Deadlimit Manager publish the staging result as `<ProjectFolder>\0source\`;
7. if an older `0source` existed, it is moved to hidden `.deadlimit\0source.previous\` before the new extraction is published;
8. if publishing the new extraction fails, Deadlimit Manager attempts to restore the previous `0source`;
9. the selected retail model path, source VPK, ValveResourceFormat version, extraction timestamp, and extracted file count are persisted in `project.json`.

`0source` is generated data. Artist-authored DMX/PNG files remain in the project root and are not touched by extraction.

The first extraction slice decompiles the discovered retail hero resource folder. Full transitive dependency closure outside that folder remains to be validated from real extraction output before it is generalized.

## Deadlimit Manager metadata

Deadlimit Manager stores its own per-project state under:

```text
<ProjectFolder>\.deadlimit\project.json
```

The `.deadlimit` directory is hidden on Windows so it does not add normal visual clutter to the artist's project folder.

The manifest currently stores:

- project name;
- absolute project-folder path;
- selected hero;
- optional release target/ID;
- `0source` destination name;
- discovered root DMX files;
- discovered root PNG textures;
- timestamps;
- discovered retail main model and VPK;
- last hero extraction metadata;
- placeholders for later VMDL/AnimGraph2/NmSkeleton build data.

## Persistence

Deadlimit Manager remembers the last opened project in the legacy compatibility path `%LOCALAPPDATA%\Deadlimit\settings.json`.

On the next launch, if that project and its manifest still exist, the project is reopened automatically.

External Source2Viewer paths are not part of local settings because extraction now uses the embedded ValveResourceFormat library.

## Current implementation boundary

Confirmed locally:

```text
select existing artist folder
→ scan root DMX/PNG
→ enter project name + hero + optional release ID
→ save hidden manifest
→ close/reopen Deadlimit Manager
→ last project and metadata restore correctly
```

Implemented and awaiting the next local smoke test:

```text
EXTRACT HERO SOURCE
→ discover current retail hero model in VPKs
→ decompile hero resource folder through embedded ValveResourceFormat
→ publish into 0source
→ preserve previous extraction on refresh
```

Not yet implemented:

- validated extraction of every transitive material/texture/shared dependency outside the discovered hero folder;
- CSDK addon preparation;
- VMDL generation/preprocessing;
- custom/reused material processing;
- ResourceCompiler invocation from the GUI pipeline;
- AG2/NmSkeleton post-processing in the GUI pipeline;
- VPK packaging/deploy.

Each boundary is kept explicit so a real project can validate one transformation before the next layer is automated.
