# Deadlimit — Prepare / compile evidence

This file records concrete local build results. Hero-specific observations remain scoped to the tested project until separately validated.

## 2026-08-22 — first successful `PREPARE + COMPILE`

### Confirmed by our pipeline

A real artist project completed the current Stage 1B action from the Deadlimit GUI.

Observed result:

```text
Addon: ivymason
Top-level artist DMX inputs: 1
Automatic material-path remaps: 3
Compiled model:
C:\WorkProjects\Deadlock\Reduced_CSDK_12\game\citadel_addons\ivymason\models\heroes_wip\ivy\ivy.vmdl_c
AG2/NmSkeleton: restored
Skeleton reference used:
models/heroes_staging/tengu/tengu_v2/dmx/mesh/ivy.vnmskel
```

The build therefore confirmed, for this project, that Deadlimit can perform the following sequence without manual CMD work:

```text
artist DMX in project root
→ generated CSDK addon source
→ minimal generated VMDL
→ narrow Wall Worm material remaps
→ bin_cs2 ResourceCompiler
→ expected VMDL_C discovery
→ DeadlockTools add ag2 / NmSkeleton restoration
```

The success dialog reported three material remaps. This validates that the current non-destructive remap mechanism was exercised in a real build; it does not prove that every Wall Worm material-path case is covered.

The `.vnmskel` path above is evidence for the tested Ivy project only. Its `heroes_staging/tengu/tengu_v2/...` location must not be generalized to other heroes.

## 2026-08-22 — first CSDK visual inspection

### Confirmed by our pipeline

The generated model opens and renders in CSDK12. Base/retail materials are broadly visible and the geometry is usable for authoring inspection.

Observed issues:

1. the character eyes render black;
2. the new costume has no project-owned custom VMAT yet; custom material creation belongs to Stage 2;
3. the generated model has no usable animation/sequence preview in Asset Browser, while an older manually prepared experiment did expose many animation clips and a selectable sequence.

The older manual experiment also showed a much larger dependency set. This is evidence that animation preview depends on more than simply adding AG2/NmSkeleton references to the compiled model.

## Black-eye investigation

### Skeleton-retention hypothesis — tested and rejected as sufficient fix

Deadlimit added a generic `BoneMarkupList` with `bone_cull_type = "None"` so ResourceCompiler would not discard helper bones. This is still a valid character-rig preservation rule, but the live visual test showed that Ivy's eyes remained black.

Therefore:

```text
BoneMarkupList / bone_cull_type=None
≠ sufficient fix for the observed black eyes
```

The retention rule remains in the pipeline because helper-bone preservation is independently useful. The next eye investigation must look at missing retail skeleton data, eye shader/material parameters, eye occlusion data, or another retail model dependency rather than repeating the same culling experiment.

## Retail VMDL inheritance experiment

### Attempt

To restore richer authoring context, Deadlimit temporarily inherited these nodes from the decompiled retail VMDL:

```text
BoneMarkupList
AttachmentList
NmSkeletonList
AnimGraph2List
Skeleton
```

### Result — compile failure, confirmed 2026-08-22

The current Reduced CSDK12 ResourceCompiler rejected the generated VMDL before compilation:

```text
Failed to allocate an instance of class 'NmSkeletonList'
Failed to allocate an instance of class 'AnimGraph2List'
```

The failing build otherwise had the same artist DMX and three material remaps. This is a direct local result, not a hypothesis.

Fresh external Deadlock modding guidance independently reports the same practical constraint: `NmSkeletonList` / `AnimGraph2List` nodes must be removed from VMDL sources for current CSDK12 workflows, and AG2 creation/restoration happens through the dedicated AnimGraph2 workflow rather than by keeping those nodes in the source ModelDoc.

### Decision

Deadlimit now preserves only source-compiler-compatible retail authoring nodes:

```text
BoneMarkupList
Skeleton
AttachmentList
```

`NmSkeletonList` and `AnimGraph2List` are explicitly excluded from generated authoring VMDL files. Runtime AG2/NmSkeleton references continue to be restored after model compilation through DeadlockTools.

This restores a compilable authoring source while preserving the fuller retail skeleton. It does not yet solve animation preview.

## Animation-preview direction

The missing sequence picker is now treated as a separate dependency-closure problem.

Evidence from the older manually prepared addon shows many `vnmclip` assets from external hero/staging paths and a large dependency count. The current generated addon does not materialize that AG2 dependency closure.

The likely generic solution is:

```text
retail model AG2/NmSkeleton references
→ discover referenced vnmskel/vnmgraph resources
→ recursively discover required vnmclip / related AG2 dependencies
→ materialize them into the addon under their original resource paths
→ keep unsupported NmSkeletonList/AnimGraph2List out of authoring VMDL
→ compile model
→ restore runtime AG2 refs
→ verify sequence preview
```

This remains a hypothesis until implemented and tested. Do not hardcode the dependency paths observed on Ivy to other heroes.

## Content vs game paths

The build uses both CSDK trees deliberately:

```text
Authoring source:
CSDK content\citadel_addons\<addon>\...\model.vmdl

Compiled runtime output:
CSDK game\citadel_addons\<addon>\...\model.vmdl_c
```

The Deadlimit success dialog now labels both paths separately. Showing only the `game` path previously was technically incomplete UX even though the runtime output path itself was correct.

## Still unvalidated

- exact cause of the observed black eyes after full retail Skeleton inheritance;
- AG2 animation dependency materialization and sequence preview;
- custom material/texture creation and persistence;
- retail Deadlock loading;
- VPK packaging/deployment;
- AG2/skeleton discovery on another hero.
