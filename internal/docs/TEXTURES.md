# Deadlimit — Custom texture binding

## 2026-08-23 — Stage 2 inherited character-material scaffold

### Pipeline evidence that triggered the rule

The first live CUSTOM material was created by decompiling Ivy's retail body material. That proved the retail material is a useful compatibility template, but copying it unchanged also copied many hero texture-source paths. Those source PNG/TGA files do not exist inside the addon `content` tree, so Material Editor reported missing-file errors for retail roughness/rim/AO/etc. The inherited NPR/rim/highlight settings could then light the new costume surface aggressively because their masks were missing or inappropriate.

The important distinction is therefore:

- **non-texture material tuning is valuable and should be inherited**;
- **retail texture-source paths must not be inherited blindly**.

### Fresh external state

Current CSDK12 still uses `.vmat` sources in `content/citadel_addons/<addon>` and Material Editor for authoring/compilation. Current Source 2 materials still expose conventional inputs such as `TextureColor`, `TextureNormal`, `TextureRoughness`, `TextureAmbientOcclusion`, and `TextureMetalness`, with default resources under `materials/default/`.

Current Source 2 import settings also associate `TextureMetalness` with `F_METALNESS_TEXTURE` (and, for the complex shader, specular support), so Deadlimit-managed materials reconcile the metalness texture-enable combo when the project-root metal map appears or disappears.

### V4 creation rule

A missing CUSTOM VMAT is created from the uniquely inferred current retail character material (for example the body/skin/head/face material actually referenced by the current hero/model), then Deadlimit sanitizes **only its texture inputs**.

This intentionally preserves the retail material's:

- shader;
- NPR/outline/highlight/rim feature configuration;
- outline colors and other color/vector values;
- outline/rim/highlight strengths, thicknesses, radii and similar scalar tuning;
- other non-texture flags and parameters.

Deadlimit does not hardcode a specific `v1`/`v3` material generation. It inherits whichever defensible current retail character material is resolved by the current hero/DMX pipeline.

### Automatic project-root PNG binding

Project-root PNG files are synchronized to:

```text
content/citadel_addons/<addon>/materials/<addon>/textures/
```

Matching filenames are bound into Deadlimit-managed CUSTOM VMAT texture slots.

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

Retail texture-source paths are never left pointing at unavailable hero PNG/TGA files in a generated CUSTOM VMAT.

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

### Vertex-color materials

A custom material whose name contains `vertexcolor` keeps `TextureColor1` neutral white so mesh Vertex Color remains the base-color source. Matching project-root normal, roughness, AO and metalness textures are still bound by the same filename rules.

Without matching maps, PREPARE writes inline values accepted by the PBR Material Editor:

```text
TextureColor1            -> [1.000000 1.000000 1.000000 0.000000]
TextureNormal1           -> [0.501961 0.501961 1.000000 0.000000]
TextureRoughness1        -> [0.964706 0.964706 0.964706 0.000000]
TextureAmbientOcclusion1 -> [1.000000 1.000000 1.000000 0.000000]
TextureMetalness1        -> [0.000000 0.000000 0.000000 0.000000]
```

PREPARE also removes the decompiled retail `Compiled Textures` cache and stale retail PNG/TGA/VTEX source references. A final managed-VMAT safety pass rejects any missing texture source that survives reconciliation.

### V4 source-of-truth and repeat-PREPARE contract

Generated V4 scaffolds contain:

```text
// DEADLIMIT_GENERATED_CUSTOM_VMAT_V4
```

A VMAT carrying a Deadlimit generated marker remains **texture-managed** by Deadlimit across later `PREPARE FOR CSDK` runs.

For those managed files, the project-root PNG set is authoritative for texture slots:

```text
first PREPARE: builder_color.png only
→ TextureColor = builder_color.png
→ TextureMetalness = default black mask

later add builder_metal.png
→ next PREPARE binds builder_metal.png to TextureMetalness
→ F_METALNESS_TEXTURE is enabled

later remove builder_metal.png from the project root
→ next PREPARE returns TextureMetalness to the default black mask
→ F_METALNESS_TEXTURE is disabled
→ the stale derived builder_metal.png copy is removed from addon content
```

The same add/remove reconciliation applies to standard color/normal/roughness/AO inputs and to inherited specialty `Texture*` source-path fields that have a semantic project PNG match.

Crucially, reconciliation rewrites **only managed texture source assignments plus required texture-enable combo state**. Material Editor edits to non-texture values — outline colors, strengths, widths, rim/highlight tuning, scalar/vector values, shader configuration, etc. — remain untouched.

An existing addon-owned VMAT **without** a Deadlimit generated marker is artist-owned/unmanaged and remains byte-for-byte protected from PREPARE.

This replaces the older blanket rule that every existing VMAT is immutable. The new distinction is:

- generated-marker VMAT: texture slots remain synchronized with project-root source textures;
- unmarked VMAT: fully artist-owned and never rewritten.

Older unmarked VMATs created before this managed contract are not silently adopted because Deadlimit cannot prove they are safe to rewrite. A one-time intentional delete/recreate is required to opt such a material into V4 management.

### Validation status

Implementation: complete.

Live V4 validation on the current Ivy project is pending. Required proof sequence:

```text
create with color only
→ add metal PNG and PREPARE
→ metal binds
→ remove metal PNG and PREPARE
→ metal reverts to safe default
→ non-texture Material Editor edits survive both PREPARE runs
```
