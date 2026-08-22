# Deadlimit — Prepare / compile evidence

This file records concrete local build results. Hero-specific observations remain scoped to the tested project until separately validated.

## 2026-08-22 — first successful `PREPARE + COMPILE`

### Confirmed by our pipeline

A real artist project completed the earlier headless Stage 1B experiment from the Deadlimit GUI.

Observed result:

```text
Addon: ivymason
Top-level artist DMX inputs: 1
Automatic material-path remaps: 3
AG2/NmSkeleton: restored
Skeleton reference used:
models/heroes_staging/tengu/tengu_v2/dmx/mesh/ivy.vnmskel
```

The `.vnmskel` path above is evidence for the tested Ivy project only. Its `heroes_staging/tengu/tengu_v2/...` location must not be generalized to other heroes.

This successful direct compile remains useful evidence, but the authoring workflow has since been changed: `PREPARE FOR CSDK` no longer compiles runtime output itself.

## 2026-08-22 — CSDK visual inspection

### Confirmed by our pipeline

The generated model opens and renders in CSDK12. Base/retail materials are broadly visible and the geometry is usable for authoring inspection.

Repeated visual tests established two unresolved issues:

1. the character eyes still render black;
2. the current generated model has no usable animation/sequence preview, while an older manually prepared addon did expose selectable animation and a much larger source/dependency set.

The new costume has no project-owned custom VMAT yet; that belongs to Stage 2 and is not treated as a build regression.

## Black-eye investigation

### Skeleton-retention hypothesis — tested and rejected as sufficient fix

Deadlimit added a generic `BoneMarkupList` with `bone_cull_type = "None"`. The live visual test showed that the eyes remained black.

Deadlimit then preserved the retail `Skeleton` and `AttachmentList` from the decompiled retail VMDL. The eyes still remained black.

Therefore both of these statements are confirmed:

```text
BoneMarkupList / bone_cull_type=None
≠ sufficient fix for the observed black eyes

retail Skeleton + AttachmentList inheritance
≠ sufficient fix for the observed black eyes
```

The retention rules remain useful character-rig invariants. The next eye investigation must focus on data lost at the mesh/material boundary or another retail ModelDoc node rather than repeating skeleton-culling fixes.

Fresh ValveResourceFormat renderer code is relevant here: current character-eye rendering requires `eyeball_l`, `eyeball_r`, and `eye_target` and then resolves those model-level bones through each mesh's bone-remapping table before supplying eyeball shader bind indices/positions. This means a model can contain the correct skeleton while an exported render mesh still lacks the eye bones in its mesh-local remapping table. That is now an active hypothesis, not yet a confirmed diagnosis of the Wall Worm DMX.

Wall Worm's current public Source 2 documentation also still describes advanced eye/QC behavior as incomplete territory. Do not assume a Wall Worm DMX round-trip preserves every retail eye-specific datum merely because the visible skeleton is present.

## Retail VMDL source-node experiment

### Confirmed failed approach

Copying `NmSkeletonList` and `AnimGraph2List` directly into the authoring VMDL fails with current Reduced CSDK12:

```text
Failed to allocate an instance of class 'NmSkeletonList'
Failed to allocate an instance of class 'AnimGraph2List'
```

Current Deadlock modding guidance independently requires removing those two nodes from source ModelDoc before compilation. Runtime AG2/NmSkeleton restoration remains a post-compile operation through DeadlockTools.

## Authoring-context strategy — current implementation

The earlier minimal-VMDL approach preserved too little authoring context. The older successful manual experiment showed that retaining the retail model's broader source context matters, especially for animation preview.

Deadlimit now uses the extracted retail hero source as the template for the CSDK addon:

```text
0source retail model folder
→ copy complete decompiled model-source tree into addon content
→ preserve every retail root ModelDoc node that current CSDK12 can accept
→ strip only known-incompatible NmSkeletonList / AnimGraph2List
→ replace RenderMeshList with artist DMX
→ replace MaterialGroupList with Deadlimit/project material routing
→ leave runtime compilation to CSDK12
```

The artist mesh is stored in a dedicated generated `deadlimit_mesh` folder so refreshing the retail source tree cannot erase retail animation DMX/source files and retail source files cannot overwrite the artist input.

This change is specifically intended to preserve nodes such as `AnimationList`, `ModelModifierList`, `PoseParamList`, hitboxes, weights, body/LOD context, attachments, and skeleton data when they exist in the current extracted retail VMDL, without hardcoding Ivy-specific node names.

A newly exposed unsupported ModelDoc class must be removed only after a concrete CSDK compiler error demonstrates that requirement.

## `content` vs `game` ownership — corrected architecture

CSDK12 documentation states that an addon has two trees:

```text
content\citadel_addons\<addon>\...
= raw/editable authoring source

game\citadel_addons\<addon>\...
= compiled runtime output generated/read by CSDK/game
```

The previous Deadlimit implementation deleted the current addon's `game` tree and immediately recompiled it during `PREPARE + COMPILE`. That was unnecessary duplication of CSDK12's normal authoring behavior and has been removed.

Current rule:

```text
PREPARE FOR CSDK
→ touches content only
→ does not delete game
→ does not invoke ResourceCompiler
→ does not apply post-compile binary patches

Launch/use CSDK12
→ CSDK compiles prepared content into game as needed
```

A later explicit `RELEASE` / `RELEASE & TEST` transaction may deliberately own clean runtime compilation, post-compile AG2 fixes, VPK packaging, and deployment. That is a different operation from authoring preparation.

## Animation-preview direction

The old manual experiment exposed many animation source assets and a selectable sequence; the current minimal source did not. The first corrective step is to copy the complete already-decompiled retail model source folder from `0source` into addon `content` and preserve compatible `AnimationList`/related ModelDoc nodes.

This is intentionally tested before implementing a more expensive VPK-wide AG2 dependency crawler. If the existing extracted retail source tree is sufficient to restore the sequence picker, no extra recursive dependency system should be added. If it remains insufficient, the next evidence-driven step is to discover and materialize referenced `vnmskel`, `vnmgraph`, and `vnmclip` resources under their original paths.

## Still unvalidated

- whether full compatible retail ModelDoc/source-tree preservation restores animation preview;
- whether the next CSDK build exposes another current-CSDK-incompatible retail ModelDoc node;
- exact cause of the observed black eyes; current strongest hypothesis is mesh-local eye-bone/remapping or eye-material data lost in the DMX round-trip;
- custom material/texture creation and persistence;
- retail Deadlock loading;
- VPK packaging/deployment;
- AG2/skeleton discovery on another hero.
