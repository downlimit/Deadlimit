# Deadlimit — Artist project workspace

This file defines the current project-folder contract used by the artist-facing workflow.

## Working-folder layout

A Deadlimit project points at the artist's existing project folder. Deadlimit does not require the artist to reorganize that folder into a tool-owned hierarchy.

Current expected shape:

```text
<ProjectFolder>\
├─ *.dmx
├─ *.png
├─ 0source\          # retail hero extraction destination; created on demand
├─ 1scene\           # optional artist-owned folder; Deadlimit does not assume or manage it
├─ 6temp\            # optional artist-owned folder; Deadlimit does not assume or manage it
└─ .deadlimit\       # hidden Deadlimit metadata
   └─ project.json
```

Only the conventions that affect Deadlimit are normative. Folder names such as `1scene` and `6temp` are examples of artist-owned structure and must not be hardcoded as required directories.

## Root asset contract

The project root is the normal handoff point from the DCC/texturing workflow.

For the current Stage 1 implementation Deadlimit scans only the top level of the selected project folder for:

- `*.dmx` model files;
- `*.png` textures.

It records relative file names in the project manifest. Other files and folders are ignored unless a later pipeline stage explicitly needs them.

Deadlimit must not move, rename, overwrite, or copy these artist-owned root assets merely to create/open a project.

## `0source` contract

`0source` is reserved for a current extraction of the selected retail Deadlock hero.

Intended behavior:

1. the user requests hero source extraction from Deadlimit;
2. Deadlimit creates `<ProjectFolder>\0source\` if it does not exist;
3. Deadlimit discovers the hero's current retail resources;
4. the extraction adapter writes the decompiled/source package into `0source`;
5. repeated extraction should refresh the extraction deterministically without touching the artist's DMX/PNG files in the project root.

`0source` must not be created simply because a project is opened. It is created when extraction is actually requested.

The extraction implementation is not part of the initial New Project milestone. The folder destination is nevertheless persisted now so later extraction does not require a workspace migration.

## Deadlimit metadata

Deadlimit stores its own per-project state under:

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
- placeholders for later discovered retail/VMDL/AnimGraph2/NmSkeleton data.

## Persistence

Deadlimit remembers the last opened project in `%LOCALAPPDATA%\Deadlimit\settings.json`.

On the next launch, if that project and its manifest still exist, the project is reopened automatically.

## Current implementation boundary

Implemented now:

```text
select existing artist folder
→ scan root DMX/PNG
→ enter project name + hero + optional release ID
→ save hidden manifest
→ reopen last project automatically
```

Not implemented by this milestone:

- retail hero extraction into `0source`;
- CSDK addon preparation;
- VMDL generation/preprocessing;
- custom/reused material processing;
- ResourceCompiler invocation;
- AG2/NmSkeleton post-processing;
- VPK packaging/deploy.

Those remain separate stages so the project-folder contract can be validated before build automation starts modifying/generated CSDK workspaces.
