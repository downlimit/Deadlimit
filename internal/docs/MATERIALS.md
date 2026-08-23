# Deadlimit — Material routing and compatibility repairs

This file records material-routing behavior that affects authoring correctness. Keep project-specific evidence scoped until a generic rule is supported by a mechanism rather than by a hero name.

## Material ownership model

Deadlimit distinguishes two intended material roles:

- `REUSE` — the prepared model continues to reference an existing retail material;
- `CUSTOM` — the addon owns an editable VMAT and its texture sources.

Stage 1 established retail reuse and compatibility remaps. Stage 2 now owns CUSTOM VMAT authoring and preservation.

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

`PREPARE FOR CSDK` reports these separately:

```text
DMX material references detected
VMDL remaps preserved
Compatibility remaps generated
VMDL remaps added
Total VMDL remaps
Custom materials detected
Custom VMAT created / preserved
Texture PNG sources refreshed
```

These values must not be conflated. A Multi/Sub-Object material may expose several DMX material references while the VMDL needs a different number of remap entries.

## Generic automatic eye repair rule

Deadlimit does not hardcode `Ivy` or a fixed retail material path.

The automatic repair is allowed only when:

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

Status: **CONFIRMED BY LIVE PIPELINE for the current Ivy export.**

The generic detection mechanism is implemented without an Ivy-specific hardcoded path, but cross-hero generality remains unproven until another hero with the same failure class is tested.

## 2026-08-23 — Stage 2 CUSTOM material authoring slice

### External state checked before implementation

Current CSDK12 documentation still defines `content/citadel_addons/<addon>` as the editable source workspace, `game/citadel_addons/<addon>` as compiled output, and Material Editor as the editor/compiler path for `.vmat` assets. No current external change invalidates the authoring design used here.

### Implemented behavior

`PREPARE FOR CSDK` now handles unresolved Wall Worm-style custom material references whose DMX value has the form `materials/<name>` without a `.vmat` extension.

For each such reference it now:

1. allocates a deterministic addon-owned resource path:

```text
materials/<addon>/<custom_name>.vmat
```

2. adds a VMDL remap from the DMX custom reference to that addon-owned VMAT;
3. when the VMAT does not yet exist, decompiles the uniquely inferred retail body/skin/head/face material from the configured retail Deadlock VPK and uses that as a character-compatible starting scaffold;
4. when the VMAT already exists, preserves it byte-for-byte and does not regenerate it;
5. refreshes project-root PNG files into:

```text
content/citadel_addons/<addon>/materials/<addon>/textures/
```

6. leaves texture-slot assignment inside the authored VMAT under Material Editor control.

The retail-template strategy is deliberate: Deadlimit does not guess a generic Source 2 shader name when it can inherit a character material already proven compatible with the current hero/build.

### Preservation contract

`PREPARE FOR CSDK` may overwrite derived copies of project-root PNG source files, because the project root is authoritative for those inputs.

`PREPARE FOR CSDK` must **never overwrite an existing addon-owned CUSTOM VMAT**. The edited VMAT in CSDK content is authoritative after its first creation.

The current `PREPARE` cleanup still applies only to `game/citadel_addons/<current addon>`; the addon `content` tree remains persistent specifically so custom authoring survives repeated prepares.

### Current validation status

Implementation is complete in code. Live validation is pending on the current Ivy project.

Expected first-run result for the current project:

```text
DMX material references detected: 5
Compatibility remaps generated: 4
Custom materials detected: 1
Custom VMAT created: 1
Custom VMAT preserved: 0
VMDL remaps added: 5
Total VMDL remaps: 5
```

Expected second `PREPARE FOR CSDK` after editing/saving the custom VMAT:

```text
Custom VMAT created: 0
Custom VMAT preserved: 1
```

That second run is the critical proof that author-authored Material Editor changes are not destroyed.

## Constraints

- Generic `materials/dev/...` paths are not rewritten indiscriminately.
- Existing retail material remaps remain authoritative and are preserved.
- The automatic custom-slot classifier currently covers the observed Wall Worm no-extension custom-reference form; other custom-reference encodings remain unproven until observed.
- The retail body/skin/head/face template is used only when one unique target can be inferred; ambiguous projects fail closed rather than silently choosing a shader.
- A project-specific observed mapping must not be generalized to a different hero unless the generic detection conditions succeed for that hero's own DMX/VMDL data.
