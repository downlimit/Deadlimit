# Deadlimit Manager — internal documentation

Deadlimit Manager is the Windows desktop application in the Deadlimit tool family for
building, iterating, repairing, packaging, and testing Deadlock character replacement
mods with minimal manual Source 2/CSDK work.

The repository also contains **Deadlimit Scripts** and **Deadlimit Shade**. See the
root `README.md` for the public product overview.

## Current workflow

```text
Deadlock resources
→ Deadlimit Manager extraction
→ 1authoring model / texture work
→ PREPARE FOR CSDK
→ CSDK / ModelDoc
→ LIVE SYNC during supported iteration
→ BUILD FOR TEST
→ compile / binding repair / VPK verification
→ Deadlock
```

Existing VPKs can instead be imported as compiled-payload projects. That path preserves
the imported payload, inspects current model animation bindings against the current
retail resources, repairs supported binding differences, verifies the rebuilt VPK, and
deploys through the guarded VPK slot workflow.

## Documentation map

- `CONTEXT.md` — product context, evidence, assumptions, and current technical focus.
- `DECISIONS.md` — durable product and architecture decisions.
- `PROJECT.md` — product definition and intended artist workflow.
- `ARCHITECTURE.md` — environment roots, service boundaries, and pipeline structure.
- `WORKSPACE.md` — `0source`, `1authoring`, and hidden project metadata contract.
- `EXTRACTION.md` — current hero/model extraction behavior and safety rules.
- `HERO_TEXTURE_PIPELINE.md` — optional retail hero texture extraction and replacement.
- `MATERIALS.md` — REUSE/CUSTOM material routing and compatibility repairs.
- `TEXTURES.md` — custom texture binding, reconciliation, and LIVE SYNC behavior.
- `VERTEX_COLOR.md` — Vertex Color sidecar export and transfer contract.
- `OUTPUT_LIFECYCLE.md` — CSDK content/game ownership and stale-output cleanup.
- `BUILD.md` — concrete prepare/compile evidence and scoped hero observations.
- `BUILD_TEST.md` — current build, verify, package, and deploy transaction.
- `RUNNING_GAME.md` — current game-process and deployed-VPK behavior.
- `VPK_IMPORT_REPAIR.md` — implemented compiled-VPK import and animation-binding repair path.
- `SETTINGS.md` — machine-local paths, dependency setup, and preferences.
- `UI.md` — current user-visible Manager behavior.
- `UI_GUIDELINES.md` — reusable UI rules for future changes.
- `NETWORK_TRUST_AUDIT.md` — external download/executable trust boundaries.
- `ROADMAP.md` — current product state and remaining validation work.

When documents disagree, confirmed current implementation and reproducible evidence take
priority over older observations. Compatibility claims must be revalidated after
relevant Deadlock or toolchain updates.

Powered in part by [Source 2 Viewer](https://s2v.app)
([ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)).
