# Deadlimit

Deadlimit is a Windows desktop tool for building and testing Deadlock character replacement mods with minimal manual Source 2/CSDK work.

## Target workflow

1. **Extract** — choose a Deadlock hero and export/decompile the relevant source assets into a working folder.
2. **Author** — point Deadlimit at a folder containing DMX model files and textures; create a project; prepare a CSDK workspace; open the model/material authoring stage for shader and texture setup.
3. **Release** — compile changed resources, apply required post-processing, validate the build, package a VPK, and place it in the configured test/deploy location.

The user-facing goal is that routine iteration becomes:

```text
Export DMX / save textures
→ Deadlimit: Release
→ test in Deadlock
```

## Documentation map

- `CONTEXT.md` — what Deadlimit is for, which workflow problems it is solving, confirmed evidence, working hypotheses, open questions, and current development focus.
- `DECISIONS.md` — durable product/technical decisions and rules for when a workaround is allowed to become generic behavior.
- `PROJECT.md` — product definition and intended user workflow.
- `WORKSPACE.md` — artist project-folder contract: root DMX/PNG inputs, hidden `.deadlimit` metadata, and `0source` extraction behavior.
- `EXTRACTION.md` — current retail hero discovery/decompilation implementation, Source 2 Viewer integration, safety rules, evidence, and dependency-closure hypothesis.
- `MATERIALS.md` — REUSE/CUSTOM material routing, confirmed VMDL remap evidence, and automatic compatibility-repair rules such as the generic eye fallback detector.
- `ARCHITECTURE.md` — environment roots, architecture, pipeline structure, and confirmed technical facts.
- `ROADMAP.md` — implementation stages and acceptance criteria.

When there is a conflict, current experimental evidence takes priority over assumptions recorded in older documentation. External tool compatibility should be revalidated when Deadlock, Reduced CSDK, DeadlockTools, Wall Worm, or ValveResourceFormat changes.

Powered in part by [Source 2 Viewer](https://s2v.app) ([ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)).
