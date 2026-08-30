# Deadlimit Aggregator — Material routing and compatibility repairs

This file records material-routing behavior that affects authoring correctness. Keep project-specific evidence scoped until a generic rule is supported by a mechanism rather than by a hero name.

## Material ownership model

Deadlimit Aggregator distinguishes two intended material roles:

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

Deadlimit Aggregator does not hardcode `Ivy` or a fixed retail material path.

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

`PREPARE FOR CSDK` handles unresolved Wall Worm-style custom material references whose DMX value has the form `materials/<name>` without a `.vmat` extension.

For each such reference it:

1. allocates `materials/<addon>/<custom_name>.vmat` once and records the exact `DMX material reference → addon VMAT` assignment in the project registry;
2. adds a VMDL remap from the DMX custom reference to that addon-owned VMAT;
3. when the VMAT is missing, decompiles the uniquely inferred current retail body/skin/head/face material as the compatibility template;
4. preserves an existing addon-owned VMAT byte-for-byte on later PREPARE runs;
5. refreshes project-root PNG files into `content/citadel_addons/<addon>/materials/<addon>/textures/`.

The registry retains historical assignments after a material temporarily disappears. Existing VMAT names therefore stay attached to the same DMX material when new references with colliding sanitized names are added, removed, or reordered. New collisions receive the next unused suffix without taking a target reserved by an earlier material.

The retail-template strategy is deliberate: the custom material should inherit the hero/build-compatible shader and useful non-texture tuning instead of starting from an unrelated generic shader.

### V3 hybrid inheritance rule

The first live retail-template VMAT opened in Material Editor, but retail source texture paths were unresolved inside addon `content`. Missing/inappropriate NPR/rim/highlight masks could also make the custom costume surface glow aggressively.

The accepted rule is now:

- copy the retail character material's shader, feature flags, outline/NPR/rim/highlight colors, thicknesses, strengths, radii, scalar/vector tuning and other non-texture settings;
- do **not** carry unavailable retail texture-source paths into the new CUSTOM VMAT;
- if a project PNG matches the custom material and texture semantic, bind it automatically;
- if a standard PBR texture is absent, use the appropriate Source 2 default texture;
- if another inherited `Texture*` effect/mask source is absent, replace that source path with `materials/default/default_black_mask.tga` so the inherited numeric effect configuration cannot illuminate the full custom surface;
- preserve non-path `Texture*` vector/scalar values instead of converting them into textures.

The current material version is not hardcoded as Ivy `v3` or `v1`: Deadlimit Aggregator inherits whichever unique current retail surface material the hero/DMX pipeline resolves.

Detailed filename binding rules are in `TEXTURES.md`.

### Preservation contract

`PREPARE FOR CSDK` may overwrite derived copies of project-root PNG files, because the project root is authoritative for those inputs.

`PREPARE FOR CSDK` must **never overwrite an existing addon-owned CUSTOM VMAT**. The edited VMAT in CSDK content is authoritative after its first creation.

The current `PREPARE` cleanup still applies only to `game/citadel_addons/<current addon>`; the addon `content` tree remains persistent specifically so custom authoring survives repeated prepares.

### Current validation status

CUSTOM routing and VMAT persistence are live-confirmed on the current Ivy project: the material exists, opens in Material Editor, VMDL has five total remaps, and repeated PREPARE reported `Custom VMAT preserved: 1`.

The new V3 hybrid template sanitization/automatic texture binding is implemented but still needs one live regeneration test, because existing authored CUSTOM VMATs are intentionally not overwritten.

## Constraints

- Generic `materials/dev/...` paths are not rewritten indiscriminately.
- Existing retail material remaps remain authoritative and are preserved.
- The automatic custom-slot classifier currently covers the observed Wall Worm no-extension custom-reference form; other custom-reference encodings remain unproven until observed.
- The retail body/skin/head/face template is used only when one unique target can be inferred; ambiguous projects fail closed rather than silently choosing a shader.
- A project-specific observed mapping must not be generalized to a different hero unless the generic detection conditions succeed for that hero's own DMX/VMDL data.
