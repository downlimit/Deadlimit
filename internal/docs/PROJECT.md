# Deadlimit — Product Definition

## Purpose

Deadlimit should remove repetitive mechanical work from Deadlock character replacement modding while preserving an explicit authoring stage for model/material work.

## User workflow

### 1. New project

The user points Deadlimit at an existing artist project folder.

The current project-root convention is intentionally simple:

```text
<ProjectFolder>\
├─ *.dmx
├─ *.png
├─ 0source\     # current retail hero extraction; created only on request
├─ optional artist-owned folders
└─ .deadlimit\  # hidden Deadlimit metadata
```

At minimum the user provides:

- the existing project folder containing the current DMX model files and PNG textures in its root;
- the Deadlock hero;
- a working project name;
- optionally a release/VPK slot or target identifier, once the loader/deploy convention is finalized.

Deadlimit must not reorganize the artist's source files merely to initialize a project. The detailed workspace contract lives in `WORKSPACE.md`.

### 2. Authoring stage

Deadlimit produces an intermediate CSDK-ready result that the user can inspect and edit with Source 2 tools.

The user must be able to:

- inspect the compiled/preview model;
- reuse original Deadlock materials on preserved parts of the hero;
- use one or more custom materials on new costume geometry;
- open custom VMATs in Material Editor;
- connect color/normal/mask/other texture inputs;
- tune shader parameters;
- save and close the tools without Deadlimit overwriting authored material changes.

### 3. Release

The user returns to Deadlimit and presses a single release/build action.

Deadlimit should then:

- validate project inputs;
- compile changed Source 2 resources;
- apply required compiled-model post-processing;
- validate critical model references;
- package a VPK;
- place the VPK in the configured output/deploy location;
- optionally prepare the build for immediate in-game testing.

## Extract Hero module

Deadlimit should provide hero extraction as a project action rather than requiring manual VPK navigation.

Intended behavior:

- use the hero already associated with the project, or let the user select/change it;
- locate the hero's current retail resources rather than assuming one fixed directory layout;
- create `<ProjectFolder>\0source\` if it does not exist when extraction is requested;
- extract/decompile the main model and relevant render meshes into `0source`;
- extract materials and texture dependencies into the same extraction package;
- preserve original retail resource paths in project metadata;
- optionally include additional resources such as animations when explicitly requested;
- never overwrite or relocate the artist's DMX/PNG files in the project root as part of extraction.

The extraction should be refreshable so `0source` can represent a current retail reference package for the selected hero.

## Material model

Deadlimit must distinguish between:

- **REUSE** — an existing retail Deadlock material referenced by the model; no copy should be created in the addon;
- **CUSTOM** — a project-owned material that must exist in the addon and remain editable by the user.

For a new custom material, Deadlimit may create the initial VMAT and texture folder/metadata. Once a user has authored that VMAT, routine builds must not overwrite it.

## UX constraint

Low-level operations such as ResourceCompiler invocation, VMDL preprocessing, DeadlockTools calls, texture compilation, and VPK packaging should remain internal unless an error requires diagnostic output.

The normal interface should expose high-level actions such as:

```text
EXTRACT HERO
NEW PROJECT / PREPARE
OPEN AUTHORING TOOLS
RELEASE
RELEASE & TEST
```

## Scope discipline

Do not encode fixes that are only proven for one hero as universal rules.

Prefer discovering resource paths and dependencies from retail compiled resources over guessing them from hero names or folder templates.
