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

## 2026-08-22 — first CSDK ModelDoc visual inspection

### Confirmed by our pipeline

The generated model opens and renders in CSDK12 ModelDoc. The preserved/base character materials are broadly visible and the geometry is usable for authoring inspection.

Two visible conditions were observed:

1. the character eyes render black;
2. the new costume has no project-owned custom VMAT yet; custom material creation belongs to Stage 2 and is not a build regression.

### Black-eye diagnosis — external evidence checked 2026-08-22

Fresh Source 2 evidence points to skeleton retention as a generic failure mode worth fixing before any hero-specific eye patch:

- current ValveResourceFormat model export emits a `BoneMarkupList` with `bone_cull_type = "None"` for decompiled ModelDoc sources;
- the current ValveResourceFormat character-eye renderer identifies eyeball materials and requires the model skeleton to retain the eye-related bones used for bind-pose eye parameters; its current implementation looks for `eyeball_l`, `eyeball_r`, and `eye_target`;
- current Source 2 ModelDoc guidance states that `Bone Cull Type = None` or `Leaf Only` is the mechanism used when required/helper bones are being discarded during compile;
- Source 2 porting guidance likewise recommends BoneMarkup / Do Not Discard when bones disappear after compilation.

This evidence does not prove that every black-eye artifact has the same cause. It does support a general character-build invariant: a replacement model intended to inherit the retail character rig should not allow ModelDoc/ResourceCompiler to discard helper bones merely because they are not directly vertex-weighted.

### Implemented generic fix — pending local visual acceptance

Deadlimit now emits this node in every generated character VMDL:

```text
{
    _class = "BoneMarkupList"
    children = [ ]
    bone_cull_type = "None"
}
```

The build log records the skeleton-retention policy explicitly.

This is deliberately broader and cleaner than an Ivy-specific eye material override:

- it preserves all imported rig/helper bones instead of naming individual eye bones;
- it does not alter artist DMX data;
- it does not alter retail eye materials;
- it applies equally to other character replacements that depend on unweighted attachment, gaze, facial, procedural, or helper bones;
- it remains compatible with the existing AG2/NmSkeleton post-process.

The next local test is a rebuild of the same live project followed by ModelDoc visual inspection. If the eyes become correct, record this as confirmed pipeline evidence. If they remain black, retain the skeleton-preservation invariant and investigate the next layer: eye material/shader parameters or other retail model data. Do not remove the generic bone-retention rule merely because a second independent eye problem may exist.

### Still unvalidated

- whether `bone_cull_type = None` fixes the observed black eyes in the current live project;
- whether another eye-material/occlusion issue remains after skeleton retention;
- custom material/texture creation and persistence;
- retail Deadlock loading;
- VPK packaging/deployment;
- AG2/skeleton discovery on another hero.
