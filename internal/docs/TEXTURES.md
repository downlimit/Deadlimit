# Deadlimit — Custom texture binding

## 2026-08-23 — Stage 2 inherited character-material scaffold

### Pipeline evidence that triggered the rule

The first live CUSTOM material was created by decompiling Ivy's retail body material. That proved the retail material is a useful compatibility template, but copying it unchanged also copied many hero texture-source paths. Those source PNG/TGA files do not exist inside the addon `content` tree, so Material Editor reported missing-file errors for retail roughness/rim/AO/etc. The inherited NPR/rim/highlight settings could then light the new costume surface aggressively because their masks were missing or inappropriate.

The important distinction is therefore:

- **non-texture material tuning is valuable and should be inherited**;
- **retail texture-source paths must not be inherited blindly**.

### Fresh external state

Current CSDK12 still uses `.vmat` sources in `content/citadel_addons/<addon>` and Material Editor for authoring/compilation. Current Source 2 materials still expose conventional inputs such as `TextureColor`, `TextureNormal`, `TextureRoughness`, `TextureAmbientOcclusion`, and `TextureMetalness`, with default resources under `materials/default/`.

### V3 creation rule

A missing CUSTOM VMAT is now created from the uniquely inferred current retail character material (for example the body/skin/head/face material actually referenced by the current hero/model), then Deadlimit sanitizes **only its texture inputs**.

This intentionally preserves the retail material's:

- shader;
- NPR/outline/highlight/rim feature configuration;
- outline colors and other color/vector values;
- outline/rim/highlight strengths, thicknesses, radii and similar scalar tuning;
- other non-texture flags and parameters.

Deadlimit does not hardcode a specific `v1`/`v3` material generation. It inherits whichever defensible current retail character material is resolved by the current hero/DMX pipeline.

### Automatic project-root PNG binding

Project-root PNG files are copied to:

```text
content/citadel_addons/<addon>/materials/<addon>/textures/
```

When the CUSTOM VMAT is first created, matching filenames replace the inherited texture path automatically.

Supported standard suffixes:

```text
Color:      _color, _diffuse, _basecolor, _base_color, _albedo
Normal:     _normal, _norm
Roughness:  _rough, _roughness
AO:         _ao, _occlusion, _ambientocclusion, _ambient_occlusion
Metalness:  _metal, _metalness, _metallic
```

Example:

```text
DMX custom material: materials/builder
builder_color.png       -> TextureColor
builder_normal.png      -> TextureNormal
builder_roughness.png   -> TextureRoughness
builder_ao.png          -> TextureAmbientOcclusion
builder_metal.png       -> TextureMetalness
```

For specialty inherited `Texture*` fields Deadlimit also supports semantic-name matching. Example: a retail parameter `TextureRimLightMask` can bind `builder_rimlightmask.png` (or `builder_rimlight.png`) when the match is unambiguous.

If there is exactly one CUSTOM material and exactly one candidate for a semantic slot, a unique-project fallback may be used even if the filename prefix differs. Ambiguous matches fail closed.

### Missing texture inputs

Retail texture-source paths are never left pointing at unavailable hero PNG/TGA files in a newly generated CUSTOM VMAT.

When no project texture matches:

```text
TextureColor            -> materials/default/default_color.tga
TextureNormal           -> materials/default/default_normal.tga
TextureRoughness        -> materials/default/default_rough.tga
TextureAmbientOcclusion -> materials/default/default_ao.tga
TextureMetalness        -> materials/default/default_black_mask.tga
other Texture* masks/effect inputs -> materials/default/default_black_mask.tga
```

Using a black fallback for specialty effect/mask textures is deliberate: it preserves the inherited numeric/color tuning but prevents missing rim/self-illum/highlight/etc. masks from enabling the whole custom surface and producing the "red/glowing Warframe" failure class.

### Preservation contract

Generated V3 scaffolds contain:

```text
// DEADLIMIT_GENERATED_CUSTOM_VMAT_V3
```

The marker is diagnostic only. The authoritative rule remains: **PREPARE FOR CSDK never overwrites an existing addon-owned CUSTOM VMAT.** Once Material Editor has saved it, that file is artist-owned.

Existing V1/V2/custom-edited VMATs are not silently migrated. To test a newer generator for one material, that specific generated VMAT must be intentionally deleted/reset first.

### Validation status

Implementation: complete.

Live validation of V3 on the current Ivy project: pending.
