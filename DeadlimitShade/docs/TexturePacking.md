# Painter roughness/export contract

This document locks the first authoring-facing texture contract for the current
Ivy `pbr.vfx` evidence. It describes the export boundary only. The hero shader
continues to consume Painter's semantic Roughness directly.

## Retail evidence

The reference capture in
[`reference/ivy/ivy-bodyv3/manifest.md`](../reference/ivy/ivy-bodyv3/manifest.md)
was taken from Steam app `1422450`, retail buildid `24882156`. The selected
material is
`models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat_c`.

Its current `pbr.vfx` channel contract is:

```text
TextureRoughness1 source
    -> Inverse image processor
    -> g_tNormalRoughness.B
```

The `Inverse` processor is a Source 2 compile step. It establishes the source
representation required by this material family; it does not mean that the
authoring viewport should display inverted roughness. Evidence: Confirmed by
retail / our pipeline.

## Painter ownership

Painter owns the conventional authoring semantic:

```text
Painter viewport Roughness = R
```

The export preset therefore uses Painter's Converted `Glossiness` map. Current
Painter documentation defines Converted Glossiness as the inverse of the
Roughness channel:

- [Creating output templates](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/export/output-templates/creating-export-presets)
- [Output templates](https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/getting-started/export/export-window/output-templates)

```text
Glossiness = 1 - R
```

This keeps the authoring control conventional while satisfying the current
retail compiler contract:

```text
Painter Roughness                 = R
Converted Glossiness export      = 1 - R
Source 2 TextureRoughness1 input = 1 - R
Source 2 Inverse processor       = 1 - (1 - R) = R
Runtime processed roughness      = R
```

Evidence for the Converted Glossiness operation: Confirmed by current external
source. Evidence for its use here: Confirmed by retail / our pipeline.

`Deadlock_Hero.glsl` must keep using `channel_roughness` as semantic roughness
directly. Do not add `1 - roughness` to the shader and do not add the compiled
`HemiOctIsoRoughness_RG_B` decoder to the authoring normal path. A forensic
packed-decoder mode can be considered later under a separate validation issue.

## Export preset

[`presets/Deadlock_Hero_Export.spexp`](../presets/Deadlock_Hero_Export.spexp) is
an actual Painter 9.1.0 binary export preset. Its structure was derived from
the installed shipped preset
`PBR Specular Glossiness (converted from Metallic Roughness).spexp` and reduced
to one output map using the same `DataExportPreset`/`DataExportMap` format.
The committed preset contains only the output needed for this slice:

| Field | Value | Evidence |
|---|---|---|
| Output filename token | `$textureSet_roughness` | Confirmed by our structural parser |
| Source map | Converted `Glossiness` (`channelSrc=4`, `mapId=Glossiness`, `mapIdType=2`) | Confirmed by our structural parser and current Painter shipped preset |
| File format | `png` | Confirmed by our structural parser |
| Output channels | One grayscale channel | Confirmed by our structural parser |
| Bit depth | 8-bit setting (serialized value `0`, matching the shipped Glossiness output) | Confirmed by our structural parser |
| Dithering | Disabled | Confirmed by our structural parser |

The filename intentionally ends in `_roughness`. The existing Deadlimit Manager
suffix binding recognizes `_roughness` as `TextureRoughness`; the file contents
are the Converted Glossiness representation required to pass the Source 2
`Inverse` compile step. This naming/content distinction is part of the contract.

The exported scalar is non-color data. Keep sRGB disabled for the resulting
roughness texture in any downstream import or comparison setup. Evidence:
Confirmed by retail / our pipeline for the scalar VMAT input; the final Painter
export color-management toggle remains a manual smoke check.

## Scope and confidence

This rule is confirmed for the current Ivy `pbr.vfx` channel processor and is
not automatically generalized to every Deadlock shader family. Other material
families require their own source/processor evidence.

- Retail VMAT/VCS mapping: **Confirmed by retail / our pipeline**.
- Converted Glossiness definition: **Confirmed by current external source**.
- Double-inversion equation: **Confirmed by retail / our pipeline**.
- Visual parity after Painter export: **Hypothesis** until the smoke action below
  is completed.

## Validation

Automated export validation: **unavailable**. Painter 9.1.0 is installed and
its shipped `.spexp` format and preset structure were inspected read-only. The
available automation surface did not expose a native Painter window or a safe
headless project/export runner, and no user project was opened or created.

Automated structural validation passed for the committed file:

- root byte length equals its serialized length field;
- exactly one `DataExportMap` is present;
- the map is sourced from Converted `Glossiness`;
- the output token is exactly `$textureSet_roughness`;
- the output is PNG and has one grayscale channel;
- no Valve, retail, CSDK, extracted texture, or temporary project data is present.

Remaining manual smoke: open `Deadlock_Hero_Export.spexp` in the installed
Painter 9.1.0 Export Textures configuration, export one temporary test texture
set containing known Roughness values `0`, `0.5`, and `1`, and verify that the
resulting `_roughness` pixels are approximately `1-R` within PNG quantization.
This is one concrete smoke action; it does not modify a production Painter
project.
