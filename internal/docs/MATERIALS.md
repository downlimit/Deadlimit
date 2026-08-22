# Deadlimit — Material routing and compatibility repairs

This file records material-routing behavior that affects authoring correctness. Keep project-specific evidence scoped until a generic rule is supported by a mechanism rather than by a hero name.

## Material ownership model

Deadlimit distinguishes two intended material roles:

- `REUSE` — the prepared model continues to reference an existing retail material;
- `CUSTOM` — the addon owns an editable VMAT and its texture sources.

The current Stage 1 authoring path handles retail reuse and compatibility remaps. Full CUSTOM VMAT generation/edit persistence remains Stage 2.

## 2026-08-22 — Ivy black-eye comparison

### Confirmed from supplied files

A known-good manually prepared `ivy.vmdl` and `ivy_ivy.dmx` were compared with the current Deadlimit-generated `ivy.vmdl` that still rendered black eyes.

The generated and known-good VMDLs now match in the major retained authoring structure, including:

```text
BoneMarkupList
RenderMeshList
BodyGroupList
LODGroupList
AttachmentList
WeightListList
AnimationList
GameDataList
HitboxSetList
Skeleton
PhysicsShapeList
```

The sequence picker also returned after preserving the retail `AnimationList` and source tree, confirming that the broader template-preserving authoring strategy is correct.

The material difference relevant to the black eyes is direct:

Current generated VMDL contains only the retail/decompiler path redirects:

```text
materials/models/.../ivy_wingsv3.vmat -> models/.../ivy_wingsv3.vmat
materials/models/.../ivy_bodyv3.vmat  -> models/.../ivy_bodyv3.vmat
materials/models/.../ivy_gearv3.vmat  -> models/.../ivy_gearv3.vmat
```

The known-good VMDL additionally contains:

```text
materials/dev/vertcolor_pbr_basic.vmat
-> models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat
```

The supplied known-good DMX string table places `ivy_eyes` / `ivy_eyes_mesh` immediately before the `materials/dev/vertcolor_pbr_basic.vmat` material reference. This is concrete evidence that the prior working eye fix was a VMDL material redirect for a generic dev fallback material.

Therefore the earlier skeleton-only hypotheses are not the active explanation for this case.

## Generic automatic repair rule implemented

Deadlimit does not hardcode `Ivy` or a fixed retail material path.

During `PREPARE FOR CSDK`, it now adds a generic eye fallback remap only when all of the following are true:

1. an artist DMX contains the exact generic fallback material:

```text
materials/dev/vertcolor_pbr_basic.vmat
```

2. the DMX string table contains an eye-related identifier (`eye`, `eyes`, `eyeball`, `pupil`, or `iris`) within the four string-table entries immediately preceding that material reference;
3. the copied retail VMDL does not already contain a remap for that generic material;
4. the existing retail material remaps expose one unique plausible character-surface target whose material filename identifies `body`, `skin`, `head`, or `face`;
5. ambiguous candidates cause no automatic repair.

The target chooser strongly prefers body/skin/head/face materials and penalizes wing/gear/weapon/gun materials. The selected material must be unique at the best score.

This deliberately preserves a fail-closed invariant: if Deadlimit cannot infer one defensible target, it leaves the material unresolved and records the condition in the prepare log instead of silently guessing.

### Current status

Implementation: complete.

Validation: pending one live CSDK rebuild on the current project.

Expected current-project result after the next `PREPARE FOR CSDK` is an additional remap equivalent to the known-good manual fix:

```text
materials/dev/vertcolor_pbr_basic.vmat
-> <uniquely inferred retail body/skin/head/face material>
```

For the supplied Ivy evidence, the uniquely inferred target is the existing retail `ivy_bodyv3.vmat` target. This outcome follows from the generic scoring rule and is not encoded as an Ivy-specific constant.

## Constraints

- Generic `materials/dev/...` paths are not rewritten indiscriminately.
- CUSTOM project material identifiers are not affected by this rule.
- Existing retail material remaps remain authoritative and are preserved.
- A project-specific observed mapping must not be generalized to a different hero unless the generic detection conditions succeed for that hero's own DMX/VMDL data.
