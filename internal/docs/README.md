# Deadlimit

Deadlimit is a Windows desktop tool for building and testing Deadlock character replacement mods with minimal manual Source 2/CSDK work.

## Target workflow

1. **Extract** — choose a Deadlock hero and export/decompile the relevant source assets into a working folder.
2. **Author** — point Deadlimit at a folder containing DMX model files and textures; create a project; prepare a CSDK workspace; open the model/material authoring stage for shader and texture setup.
3. **Iterate in game** — after authoring is established, use one `BUILD & TEST` action to prepare changes, compile, restore required model post-processing, package the VPK, and deploy it directly to retail Deadlock addons. If retail Deadlock is currently running, Deadlimit must close it first because the loaded VPK is file-locked by the game.

The user-facing normal iteration loop is:

```text
select edited geometry in 3ds Max
→ MAX EXPORT (Deadlimit Wall Worm helper)
→ save textures if changed
→ BUILD & TEST
→ if Deadlock was running: allow Deadlimit to close it
→ compile/deploy
→ launch/test in Deadlock
```

The Max helper exports text DMX22 to the project root. When Max vertex color channel 0 is present, it preserves that data through a temporary Wall Worm `ChannelMod` bridge and writes `color$0/color$0Indices` without modifying the artist node.

## Documentation map

- `CONTEXT.md` — what Deadlimit is for, which workflow problems it is solving, confirmed evidence, working hypotheses, open questions, and current development focus.
- `DECISIONS.md` — durable product/technical decisions and rules for when a workaround is allowed to become generic behavior.
- `PROJECT.md` — product definition and intended user workflow.
- `WORKSPACE.md` — artist project-folder contract: root DMX/PNG inputs, hidden `.deadlimit` metadata, and `0source` extraction behavior.
- `WALLWORM_EXPORT.md` — project-specific Max/Wall Worm DMX22 exporter and the confirmed channel-0 vertex-color bridge.
- `EXTRACTION.md` — current retail hero discovery/decompilation implementation, Source 2 Viewer integration, safety rules, evidence, and dependency-closure hypothesis.
- `MATERIALS.md` — REUSE/CUSTOM material routing, confirmed VMDL remap evidence, and automatic compatibility-repair rules such as the generic eye fallback detector.
- `TEXTURES.md` — inherited CUSTOM VMAT scaffolding, project-root PNG naming conventions, automatic texture rebinding, managed add/remove behavior, and safe fallbacks.
- `OUTPUT_LIFECYCLE.md` — authoritative `content` vs disposable compiled `game` contract, clean authoring PREPARE behavior, and incremental BUILD & TEST stale-output handling.
- `BUILD_TEST.md` — accepted one-click daily iteration transaction: incremental prepare/compile, AG2 restoration, VPK packaging and direct retail addons deployment.
- `RUNNING_GAME.md` — live-confirmed retail VPK file-lock behavior when Deadlock is running and the resulting close-before-deploy contract.
- `ARCHITECTURE.md` — environment roots, architecture, pipeline structure, and confirmed technical facts.
- `ROADMAP.md` — implementation stages and acceptance criteria.

When there is a conflict, current experimental evidence takes priority over assumptions recorded in older documentation. External tool compatibility should be revalidated when Deadlock, Reduced CSDK, DeadlockTools, Wall Worm, or ValveResourceFormat changes.

Powered in part by [Source 2 Viewer](https://s2v.app) ([ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)).
