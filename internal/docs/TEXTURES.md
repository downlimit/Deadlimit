# Deadlimit — Custom texture binding

## 2026-08-23 — Stage 2 inherited character-material scaffold

### Pipeline evidence that triggered the rule

The first live CUSTOM material was created by decompiling Ivy's retail body material. That proved the retail material is a useful compatibility template, but copying it unchanged also copied many hero texture-source paths. Those source PNG/TGA files do not exist inside the addon `content` tree, so Material Editor reported missing-file errors for retail roughness/rim/AO/etc. The inherited NPR/rim/highlight settings could then light the new costume surface aggressively because their masks were missing or inappropriate.

The important distinction is therefore:

- **non-texture material tuning is valuable and should be inherited**;
- **retail texture-source paths must not be inherited blindly**.

### Fresh external state

Current CSDK12 still uses `.vmat` sources in `content/citadel_addons/<addon>` and Material Editor for authoring/compilation. Current Source 2 materials expose conventional inputs such as `TextureColor`, `TextureNormal`, `TextureRoughness`, `TextureAmbientOcclusion`, and `TextureMetalness`; each input accepts an inline numeric value when no texture is assigned.

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
TextureColor            -> [0.500000 0.500000 0.500000 0.000000]
TextureNormal           -> [0.501961 0.501961 1.000000 0.000000]
TextureRoughness        -> [0.800000 0.800000 0.800000 0.000000]
TextureAmbientOcclusion -> [1.000000 1.000000 1.000000 0.000000]
TextureMetalness        -> [0.000000 0.000000 0.000000 0.000000]
other Texture* masks/effect inputs -> [0.000000 0.000000 0.000000 0.000000]
```

Using numeric black for specialty effect/mask inputs prevents missing rim/self-illum/highlight/etc. masks from enabling the whole custom surface. Generated materials carry no dependency on shared `materials/default/*` placeholder textures.

### Vertex-color materials

A custom material whose name contains `vertexcolor` keeps `TextureColor1` neutral white so mesh Vertex Color remains the base-color source. Matching project-root normal, roughness, AO and metalness textures are still bound by the same filename rules.

Without matching maps, PREPARE writes inline values accepted by the PBR Material Editor:

```text
TextureColor1            -> [1.000000 1.000000 1.000000 0.000000]
TextureNormal1           -> [0.501961 0.501961 1.000000 0.000000]
TextureRoughness1        -> [0.800000 0.800000 0.800000 0.000000]
TextureAmbientOcclusion1 -> [1.000000 1.000000 1.000000 0.000000]
TextureMetalness1        -> [0.000000 0.000000 0.000000 0.000000]
```

PREPARE also removes the decompiled retail `Compiled Textures` cache and stale retail PNG/TGA/VTEX source references. A final managed-VMAT safety pass rejects any missing texture source that survives reconciliation.

### Repeat-PREPARE ownership contract

Generated V4 scaffolds contain:

```text
// DEADLIMIT_GENERATED_CUSTOM_VMAT_V4
```

Deadlimit records current generated custom-material ownership in:

```text
<project>/.deadlimit/managed-custom-materials.json
```

The registry survives Material Editor replacing the generated first-line comment. A registered VMAT remains **texture-managed** across later `PREPARE FOR CSDK` runs while its non-texture values stay artist-controlled.

For those managed files, the project-root PNG set is authoritative for texture slots:

```text
first PREPARE: builder_color.png only
→ TextureColor = builder_color.png
→ TextureMetalness = numeric 0

later add builder_metal.png
→ next PREPARE binds builder_metal.png to TextureMetalness
→ F_METALNESS_TEXTURE is enabled

later remove builder_metal.png from the project root
→ next PREPARE returns TextureMetalness to numeric 0
→ F_METALNESS_TEXTURE is disabled
→ the stale derived builder_metal.png copy is removed from addon content
```

The same add/remove reconciliation applies to standard color/normal/roughness/AO inputs and to inherited specialty `Texture*` source-path fields that have a semantic project PNG match.

Crucially, reconciliation rewrites **only managed texture source assignments plus required texture-enable combo state**. Material Editor edits to non-texture values — outline colors, strengths, widths, rim/highlight tuning, scalar/vector values, shader configuration, etc. — remain untouched.

Current custom-material remaps are registered during PREPARE, which migrates existing generated materials whose marker was already replaced by Material Editor. Files outside the registered custom-material targets remain untouched.

Preparation gestures:

- `PREPARE FOR CSDK`: preserve manual VMAT tuning, synchronize matching project textures, remove derived texture files whose project source disappeared, and restore the affected slots to numeric neutral values;
- `SHIFT + PREPARE FOR CSDK`: back up existing addon custom VMAT files under `<project>/.deadlimit/backups/materials/<timestamp>/`, then regenerate the currently referenced custom materials from the latest templates and project textures;
- `SHIFT + LAUNCH CSDK`: run the normal preserving PREPARE, start ONLINE PREPARATION, and launch CSDK. Repeating the gesture stops online synchronization without launching another CSDK instance.

ONLINE PREPARATION watches supported root textures, artist DMX files, and matching `*_vertexcolor.fbx` sidecars. A sidecar created after the DMX debounce is read as its own event: the prepared DMX target is refreshed from the artist DMX and Vertex Color is validated/applied immediately. A rejected sidecar marks the session as requiring normal PREPARE and reports the reason.

The authoritative project-root texture set includes `.png`, `.tga`, `.jpg`, `.jpeg`, `.tif`, and `.tiff`. Every supported derived copy absent from that set is removed. Cleanup still runs when the current DMX contains zero custom materials, so sources and VMATs belonging to the last removed managed material cannot survive into a later VPK.

### Validation status

Implementation: complete. Required live proof sequence:

```text
create with color only
→ add metal PNG and PREPARE
→ metal binds
→ remove metal PNG and PREPARE
→ metal reverts to safe default
→ non-texture Material Editor edits survive both PREPARE runs
```
