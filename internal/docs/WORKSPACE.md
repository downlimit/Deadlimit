# Deadlimit Manager — Artist project workspace

This file defines the current project-folder contract used by the artist-facing workflow.

## Working-folder layout

A Deadlimit Manager authoring project uses this managed artist-facing structure:

```text
<ProjectFolder>\
├─ 0source\          # generated retail extraction
│  └─ glTFpipeline\  # isolated glTF extraction and PREPARE fallback root
├─ 1authoring\       # recursive DMX/FBX/glTF/GLB and texture inputs
│  └─ portraits\     # flat, non-destructive portrait/UI working copies from extraction
├─ 2concept\
├─ 3scene\
├─ 4texture\
├─ 5promo\
├─ 6temp\
└─ .deadlimit\       # hidden Deadlimit Manager metadata / staging / safety backup
   ├─ project.json
   └─ 0source.previous\   # previous extraction, when a refresh replaces an existing 0source
```

Deadlimit creates these folders when an authoring project is saved or extracted.

## `1authoring` asset contract

`1authoring` is the only handoff point from the DCC/texturing workflow. The project root is not scanned for authoring inputs. Deadlimit scans `1authoring` recursively for:

- `*.dmx`, `*.fbx`, `*.gltf`, and `*.glb` model files;
- supported image sources including `*.tga`, `*.png`, and `*.psd`.

Subfolder names have no routing meaning. When duplicate authoring basenames exist, image priority is TGA, then PNG, then PSD; ties use the alphabetically first relative path. Multiple Deadlock resources with the same basename require an explicit user selection, which can be persisted per authoring file.

Deadlimit Manager does not overwrite artist files in `1authoring`. Portrait/UI extraction flattens missing files into `1authoring\portraits` by filename and preserves existing edited copies. If extraction produces the same filename at multiple resource paths, the alphabetically first source path supplies the initial working copy.

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

`0source` is generated data. Artist-authored model and texture files live under `1authoring` and are not overwritten by extraction. PREPARE first resolves a resource from the DMX tree in `0source`, then from `0source\glTFpipeline` when the primary resource is absent.

DMX, FBX and glTF/GLB files found recursively in `1authoring` are compile inputs with different adapters. DMX is overlaid directly. FBX is a ModelDoc-supported render-mesh source. glTF/GLB is converted into the decompiled DMX companion extracted alongside the glTF package, preserving primitive boundaries, `COLOR_0`, four skin influences and the retail skeleton contract.

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
- discovered `1authoring` model files;
- discovered `1authoring` texture files;
- remembered authoring-file to Deadlock-resource texture bindings;
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
→ create and scan the recursive 1authoring workspace
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
