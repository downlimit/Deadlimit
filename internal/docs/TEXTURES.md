# Deadlimit — Custom texture binding

## 2026-08-23 — Stage 2 clean PBR scaffold

### Pipeline evidence that triggered the change

The first live CUSTOM-material scaffold was created by decompiling the retail Ivy body material. It opened in CSDK12 Material Editor, but it retained many hero-specific texture references and feature settings (NPR/rim/highlight/etc.). Those retail texture source files were not present inside the addon content tree, so Material Editor reported missing files such as Ivy body rim/rough/AO textures. The inherited feature state also made the custom costume material visually noisy/glowy.

This means a whole retail hero material is a bad default scaffold for a new artist-owned costume material even though it is shader-compatible.

### Fresh external state

Current CSDK12 documentation still uses Material Editor + `.vmat` sources in `content/citadel_addons/<addon>`. Current Source 2/CS2 shader import settings use conventional semantic texture inputs such as `TextureColor`, `TextureNormal`, `TextureRoughness`, `TextureAmbientOcclusion`, and `TextureMetalness`, with `_color`, `_normal`, `_rough`, `_ao`, and `_metal` filename conventions. Current Source 2 core content also exposes default fallback textures under `materials/default/`.

### New creation rule

A missing CUSTOM VMAT is now created as a **clean PBR scaffold**, not as a copy of the hero body material.

Deadlimit first attempts to read the shader name from the installed CSDK core `materials/default/default.vmat`. If unavailable it falls back to `shaders/complex.shader`.

The initial scaffold contains only ordinary PBR inputs and neutral/default values. It intentionally does not inherit hero-specific NPR, rim-light, highlight, self-illum, tint-mask, or other specialty texture references.

Default fallbacks:

```text
TextureColor            -> materials/default/default_color.tga
TextureNormal           -> materials/default/default_normal.tga
TextureRoughness        -> materials/default/default_rough.tga
TextureAmbientOcclusion -> materials/default/default_ao.tga
Metalness               -> scalar 0 unless a metalness map is supplied
```

### Automatic project-root PNG binding

Project-root PNG files continue to be copied to:

```text
content/citadel_addons/<addon>/materials/<addon>/textures/
```

When a CUSTOM VMAT is first created, Deadlimit matches texture filenames to the custom material name.

Supported suffixes:

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

If the project has exactly one CUSTOM material and exactly one PNG candidate for a semantic slot, Deadlimit may use that unique candidate even when the filename prefix differs. This is deliberately limited to the unambiguous single-material case.

Ambiguous matches fail closed and use the default fallback instead of guessing.

### Preservation contract

Generated V2 scaffolds contain this marker:

```text
// DEADLIMIT_GENERATED_CUSTOM_VMAT_V2
```

This marker is diagnostic only. The authoritative rule remains: **PREPARE FOR CSDK never overwrites an existing addon-owned CUSTOM VMAT.** Once created, Material Editor owns that VMAT.

Therefore existing CUSTOM VMATs created by the older retail-template implementation are preserved rather than silently migrated. Recreating one with the new clean scaffold requires deleting/resetting that specific generated VMAT intentionally; this avoids destroying artist edits.

### Validation status

Implementation: complete.

Live validation of the new V2 scaffold in the current Ivy project: pending.
