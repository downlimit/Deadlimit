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

The supplied working DMX contains five distinct material references relevant to the current model:

```text
materials/ivy_biulder
materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat
materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_gearv3.vmat
materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3.vmat
materials/dev/vertcolor_pbr_basic.vmat
```

The first Deadlimit implementation reported `3` because that number was the count of generated/retained VMDL path remaps matching `materials/models/...`, not the number of sub-materials in the artist DMX. This distinction is now explicit in both the code and the UI.

The known-good VMDL additionally contains the experimentally confirmed eye redirect:

```text
materials/dev/vertcolor_pbr_basic.vmat
-> models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat
```

The supplied known-good DMX contains `ivy_eyes` / `ivy_eyes_mesh` together with the `materials/dev/vertcolor_pbr_basic.vmat` reference. After adding the redirect above in the earlier manual pipeline, the user confirmed that the eyes rendered correctly. Therefore this mapping is confirmed for the current Ivy export; it must not be hardcoded as a universal hero rule.

## 2026-08-22 — failed eye detector and repair

The first generic eye detector required the eye identifier to occur within four NUL-delimited string-table entries immediately before the generic dev material. The live prepare run still reported only three remaps and the eyes remained black. That proves the detector did not recognize the actual current DMX layout.

The detector has been replaced with a less format-fragile rule:

1. enumerate distinct material references from the entire artist DMX byte stream;
2. require the exact generic fallback material `materials/dev/vertcolor_pbr_basic.vmat` to be one of those references;
3. independently require an eye-related identifier (`eye`, `eyes`, `eyeball`, `pupil`, or `iris`) somewhere in the same artist DMX set;
4. preserve existing retail remaps;
5. infer a target only when one unique body/skin/head/face material is available from either preserved retail remaps or the compatibility path-remaps being generated in the same prepare pass;
6. ambiguous targets remain unresolved and are logged instead of guessed.

This keeps the useful fail-closed behavior while removing the incorrect assumption about Wall Worm DMX string ordering.

## Diagnostics contract

`PREPARE FOR CSDK` now reports these separately:

```text
DMX material references detected
VMDL remaps preserved
Compatibility remaps added
Total VMDL remaps
```

These values must not be conflated. A Multi/Sub-Object material may expose several DMX material references while the VMDL needs a different number of remap entries.

For the current supplied Ivy DMX, the expected diagnostic material-reference count is five. The expected VMDL remap count before CUSTOM material routing is four: three `materials/models/...` path repairs plus the eye redirect. The old known-good fifth redirect for `materials/ivy_biulder` belongs to the project-owned CUSTOM material path and is handled separately from the eye compatibility repair.

## Generic automatic eye repair rule

Deadlimit does not hardcode `Ivy` or a fixed retail material path.

The current automatic repair is allowed only when:

- the artist DMX references the exact generic dev fallback material;
- the same DMX set contains an eye-related identifier;
- the copied retail VMDL has no existing remap for that generic material;
- exactly one defensible body/skin/head/face target can be inferred from the union of preserved retail remaps and compatibility path-remaps generated from the same DMX.

The target chooser strongly prefers body/skin/head/face materials and penalizes wing/gear/weapon/gun materials. The selected material must be unique at the best score.

## 2026-08-22 — live prepare and visual confirmation after detector fix

A local `PREPARE FOR CSDK` run on the current Ivy project reported:

```text
DMX overlays: 1
DMX material references detected: 5
VMDL remaps preserved: 0
Compatibility remaps added: 4
Total VMDL remaps: 4
Retail source files copied: 272
```

After launching CSDK12 and rebuilding from the prepared `content`, the user confirmed that the eyes render correctly. The animation list also remained restored from the preceding template-preservation fix.

This confirms the current Ivy pipeline end-to-end for the eye compatibility layer:

```text
artist DMX
→ detect five material references
→ generate three materials/models path repairs
→ infer one generic-eye fallback redirect to the unique body material
→ preserve retail authoring/animation structure
→ CSDK12 rebuild
→ eyes render correctly
```

Status: **CONFIRMED BY LIVE PIPELINE for the current Ivy export.**

The generic detection mechanism is implemented without an Ivy-specific hardcoded path, but cross-hero generality remains unproven until another hero with the same failure class is tested. Do not claim hero-independent validation yet.

## Constraints

- Generic `materials/dev/...` paths are not rewritten indiscriminately.
- CUSTOM project material identifiers are not affected by the eye rule.
- Existing retail material remaps remain authoritative and are preserved.
- A project-specific observed mapping must not be generalized to a different hero unless the generic detection conditions succeed for that hero's own DMX/VMDL data.
