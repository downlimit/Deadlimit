# Deadlimit — Product Definition

## Purpose

Deadlimit should remove repetitive mechanical work from Deadlock character replacement modding while preserving an explicit authoring stage for model/material work.

## User workflow

### 1. New project

The user provides, at minimum:

- a working folder containing DMX model files and textures;
- the Deadlock hero;
- a working project name;
- optionally a release/VPK slot or target identifier, once the loader/deploy convention is finalized.

Deadlimit prepares the CSDK workspace and project metadata automatically.

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

Deadlimit should also provide a separate extraction workflow:

- choose a Deadlock hero;
- choose an output folder;
- locate the hero's actual retail resources rather than assuming one fixed directory layout;
- extract/decompile the main model and relevant render meshes;
- extract materials and texture dependencies;
- preserve original resource paths in project metadata;
- optionally include additional resources such as animations when explicitly requested.

The extracted folder should be usable as the starting point for a new Deadlimit project.

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
