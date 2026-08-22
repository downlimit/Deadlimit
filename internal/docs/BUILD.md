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
→ narrow Wall Worm material remaps
→ bin_cs2 ResourceCompiler
→ expected VMDL_C discovery
→ DeadlockTools add ag2 / NmSkeleton restoration
```

The success dialog reported three material remaps. This validates that the current non-destructive remap mechanism was exercised in a real build; it does not prove that every Wall Worm material-path case is covered.

The `.vnmskel` path above is evidence for the tested Ivy project only. Its `heroes_staging/tengu/tengu_v2/...` location must not be generalized to other heroes.

## 2026-08-22 — first CSDK ModelDoc visual inspection

### Confirmed by our pipeline

The generated model opens and renders in CSDK12 ModelDoc. The preserved/base character materials are broadly visible and the geometry is usable for authoring inspection.

Two visible conditions were observed:

1. the character eyes render black;
2. the new costume has no project-owned custom VMAT yet; custom material creation belongs to Stage 2 and is not a build regression.

## 2026-08-22 — bone-retention-only eye fix rejected as sufficient

A rebuild with a generated `BoneMarkupList` using `bone_cull_type = "None"` compiled successfully, but the eyes remained black in ModelDoc.

Therefore:

- preserving already-imported bones from culling is a useful character-rig invariant;
- that invariant alone does not solve the observed black-eye defect;
- the specific hypothesis "the eyes are black only because ModelDoc culled helper bones" is rejected for this live project.

The rule is retained as a defensive rig-preservation measure because current ValveResourceFormat ModelDoc export also emits `BoneMarkupList` with culling disabled.

## 2026-08-22 — authoring parity regression discovered

Comparison with an older manually prepared addon exposed a second concrete regression in the minimal generated VMDL.

Old manually prepared model:

- Asset Browser offered character animations/sequences for preview;
- the asset had a large dependency set and animation resources visible in the addon.

Current minimal Deadlimit model:

- geometry renders;
- animation/sequence selection is effectively absent;
- the generated authoring model carries only a very small dependency set.

This demonstrates that post-compiling `add ag2` into the runtime `.vmdl_c` is not sufficient for authoring parity. ModelDoc/Asset Browser needs the relevant source/model metadata present in the generated VMDL itself.

Fresh ValveResourceFormat source confirms that current ModelDoc decompilation explicitly emits separate `Skeleton`, `BoneMarkupList`, `NmSkeletonList`, `AnimGraph2List`, and attachment structures. AnimGraph2 graph references and NmSkeleton references are represented as ModelDoc nodes, not only as opaque runtime post-process data.

## Implemented architecture change — pending local acceptance

Deadlimit no longer treats the generated VMDL as purely minimal geometry plus material remaps.

The build now structurally reads the decompiled retail VMDL in `0source` and preserves a narrow allowlist of character-authoring nodes:

```text
BoneMarkupList
Skeleton
NmSkeletonList
AnimGraph2List
AttachmentList
```

It still regenerates the project-owned portions:

```text
MaterialGroupList / material-path remaps
RenderMeshList / artist DMX
```

This is a hybrid inheritance model:

```text
current retail character model metadata
+ artist replacement render mesh
+ project material policy
→ generated authoring VMDL
→ compile
→ runtime AG2/NmSkeleton verification/post-process
```

The retail nodes are copied structurally as complete balanced root-child objects; Deadlimit does not regex-delete nested VMDL blocks. The allowlist is intentionally narrow to avoid reintroducing unrelated retail ModelDoc structures that may be incompatible with Reduced CSDK.

This architecture is intended to address both observed losses:

1. restore the full retail skeleton/helper-bone definitions that a DMX round-trip may not contain, which is the next generic black-eye candidate;
2. restore source-level NmSkeleton/AnimGraph2 references so ModelDoc/Asset Browser can expose the character's animation context again.

These effects are not yet accepted until the next live rebuild is inspected.

## `content` versus `game` output clarification

The successful build produces both layers:

```text
CSDK source/authoring VMDL:
Reduced_CSDK_12\content\citadel_addons\<addon>\...\model.vmdl

compiled runtime VMDL_C:
Reduced_CSDK_12\game\citadel_addons\<addon>\...\model.vmdl_c
```

The earlier success dialog displayed only the compiled `game` path, which was technically correct but misleading for an authoring workflow. The UI now labels and displays both paths explicitly.

### Next live acceptance check

After updating and rebuilding the same project, inspect exactly two things in ModelDoc/Asset Browser:

1. whether the eyes render normally;
2. whether the sequence/animation selector is populated again.

If animation references are present in the VMDL but the selector remains empty, the next isolated issue is dependency materialization: Deadlimit must copy/mount the required retail AnimGraph2/NmSkeleton/clip resources into the addon search path. Do not add that extra mechanism until this test shows it is required.

### Still unvalidated

- whether full retail `Skeleton` inheritance resolves the current black eyes;
- whether inherited `NmSkeletonList` / `AnimGraph2List` are sufficient for ModelDoc sequence preview in Reduced CSDK12;
- whether animation dependencies must also be materialized into the addon;
- custom material/texture creation and persistence;
- retail Deadlock loading;
- VPK packaging/deployment;
- AG2/skeleton discovery on another hero.
