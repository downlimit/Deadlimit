# Deadlimit Aggregator — Roadmap

## Stage 0 — Repository / environment baseline — ACCEPTED

- private `downlimit/Deadlimit` repository and durable project docs;
- machine-local CSDK12 / DeadlockTools / retail Deadlock paths;
- project manifest + hidden `.deadlimit` metadata;
- Deadlimit Aggregator desktop launcher/updater/icon workflow.

## Stage 1 — Project ingestion and CSDK authoring preparation — ACCEPTED FOR CURRENT IVY PIPELINE

### 1A — Project shell / hero extraction

Validated live:

- create/open/reopen project;
- scan artist root DMX/PNG without moving original inputs;
- extract current retail hero model source into pristine `0source` through embedded ValveResourceFormat;
- preserve artist folders/assets.

### 1B — PREPARE FOR CSDK

Validated live on Ivy:

- reconstruct the retail VMDL authoring tree from `0source`;
- overlay artist DMX only on the matching retail render mesh;
- preserve retail render meshes/bodygroups/LODs/AnimationList;
- remove only current-CSDK-incompatible source nodes;
- repair the proven Wall Worm path form without modifying artist DMX;
- generic eye fallback repair restored Ivy's eyes without losing animations;
- standalone PREPARE keeps `content` authoritative and cleans only the current addon's compiled `game` output.

Cross-hero validation remains compatibility work; Ivy-specific evidence must not be generalized without a live check.

## Stage 2 — CUSTOM materials / texture lifecycle — IMPLEMENTED, CURRENT PIPELINE IN USE

Current contract:

```text
REUSE material
→ retain current retail resource

unresolved Wall Worm materials/<name> slot
→ CUSTOM addon-owned VMAT
```

Implemented behavior:

- missing CUSTOM VMAT inherits the uniquely defensible current retail character-surface material;
- useful non-texture shader/NPR/outline/rim values are retained;
- unavailable retail texture-source paths are sanitized;
- project-root PNG naming binds standard and compatible specialty Texture* slots;
- adding a matching texture on a later PREPARE binds it;
- removing that texture later restores the safe default and removes the stale derived PNG;
- Deadlimit Aggregator-managed VMAT texture slots reconcile on every PREPARE;
- user-authored/custom VMAT settings outside Deadlimit Aggregator-managed texture reconciliation remain preserved.

The current Ivy pipeline has already produced the custom `ivy_mason` material in CSDK and reached compiled model preview. Additional cross-hero/material variants remain compatibility validation rather than blockers for the normal iteration workflow.

## Stage 3 — One-click in-game iteration — IMPLEMENTED, FINAL LIVE ACCEPTANCE NEXT

Primary daily action:

```text
artist changes DMX / PNG
→ BUILD & TEST
→ prepare/sync
→ incremental compile
→ AG2 restoration when VMDL was rebuilt
→ verified VPK
→ direct deploy to retail game/citadel/addons/pak##_dir.vpk
→ test in Deadlock
```

Implemented safeguards/UX:

- first build is clean/full; later builds use `.deadlimit/build-test-state.json` hashes;
- changed DMX invalidates VMDL; changed images invalidate VMATs;
- known removed outputs are deleted; ambiguous removals force clean rebuild;
- `SHIFT + BUILD & TEST` is the hidden forced-full-rebuild escape hatch and is documented by tooltip;
- overall 0–100 progress + animated title spinner + status-bar progress;
- VPK created in-process through ValvePak and verified before final deployment;
- automatic retail `gameinfo.gi` guard ensures `Game citadel/addons`, creates a backup before a safe patch, and fails closed on unknown layout;
- project-owned VPK slot/hash tracking prevents silently overwriting another mod;
- if Release ID changes, an old slot is removed only when its file still matches Deadlimit Aggregator's recorded hash;
- completion dialog offers OK / launch Deadlock when the game is not running;
- if Deadlock is already running, Deadlimit Aggregator does not force a restart while hot-reload behavior is still unproven;
- English / Russian UI preference is stored in machine-local settings;
- user-facing build buttons have tooltips.

Already live-proven before the latest UX/safety layer:

- BUILD & TEST compiled the current Ivy project and produced a VPK directly in retail addons.

Final acceptance for Stage 3 requires one run after Deadlimit Aggregator Updater that proves the **current** implementation:

```text
Deadlimit Aggregator Updater
→ BUILD & TEST
→ no external CSDKCfgVPK Success popup
→ progress UI works
→ gameinfo guard does not add bureaucracy when already configured
→ existing own pak slot is accepted and ownership state is recorded
→ final Deadlimit Aggregator dialog appears
→ VPK works in Deadlock
```

Then run one ordinary incremental texture/DMX tweak and one `SHIFT + BUILD & TEST` smoke test.

### Hot-reload experiment

Do not assume a restart is required.

Test once with Deadlock left open:

```text
make an obvious skin change
→ BUILD & TEST
→ switch away from the hero / recreate the relevant preview
→ select the hero again
```

If the new asset appears consistently, ordinary iteration stays restart-free. If it remains cached, add an explicit restart fallback later; do not make restart mandatory without this evidence.

## Stage 4 — Desktop interface improvement — NEXT AFTER STAGE 3 ACCEPTANCE

Goal: improve the artist-facing Deadlimit Aggregator application now that the functional pipeline is closed.

Focus:

- information hierarchy and button grouping;
- clearer separation of AUTHORING vs TEST workflow;
- compact project/header state;
- consistent progress/status presentation;
- cleaner success/error dialogs;
- full English/Russian polish and terminology consistency;
- tooltip/help affordances without clutter;
- visual spacing, sizing and iconography;
- keep logs/technical details available but secondary.

The interface phase must not destabilize the accepted build pipeline.

## Later compatibility work

- validate extraction/material/model rules on heroes beyond Ivy;
- expand `0source` dependency closure only when real missing dependencies prove it necessary;
- validate additional Wall Worm DMX material encodings before expanding CUSTOM detection;
- adapt to future Deadlock / CSDK12 / DeadlockTools / ValveResourceFormat format changes;
- optional richer mod/deployment management only if the product actually needs it.
