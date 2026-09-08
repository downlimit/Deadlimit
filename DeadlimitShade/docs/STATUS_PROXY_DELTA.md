# Deadlock opaque status-proxy delta

Date: 2026-09-08

## Scope and identity

This document isolates `D_USE_STATUS_EFFECTS_PROXY=1` from the completed
ordinary opaque graph. Both programs come from the same retail parent:

- resource: `shaders/vfx/pbr_vulkan_60_ps.vcs`;
- parent SHA-256:
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- static combo: `24` (`S_USE_NPR_LIGHTING=1`,
  `S_USE_STATUS_EFFECTS_PROXY=1`);
- ordinary dynamic combo `0`: file `0`, 76,732-byte SPIR-V,
  `03af15c800051c118177d27a99b6cbfdd39b7b491e6fc2216611030afd490229`;
- status dynamic combo `2`: file `1`, 88,520-byte SPIR-V,
  `ea7d0bdb7d89b07422366dcd64fbaa0ec25acd427039b0774668dab86ecc71b0`.

The two dynamic states have identical pixel render state. The status program
adds two interpolants and 26 named `_Globals_` fields. Its new work is confined
to material preparation before the shared NPR lighting graph.

## Added inputs

| Group | Retail fields | Role |
|---|---|---|
| color warp | `g_tSFXColorWarp3D`, `g_flSFXColorWarpAmount` | remaps prepared base color through a 3D LUT |
| projection | `g_flSFXUseModelUVs`, scale, XYZ scroll and XYZ offset | selects model UV or animated world-space triplanar coordinates |
| surface maps | `g_tSFXNormal`, `g_tSFXMetalness`, `g_tSFXRoughness` and their amount fields | blend replacement surface properties into the base material |
| self illumination | `g_tSFXSelfIllum`, `g_flSFXSelfIllumAmount` | replaces mask/tint and drives the self-illum scale toward one |
| detail | `g_tSFXDetail`, amount, scale, XYZ scroll and blend mode | adds, emits or multiplies an animated detail pattern |

The additional interpolants are used as world/model-space projection data:
`input_5.xyz` supplies the position used by triplanar coordinates;
`input_5.w` and `input_6` produce the three projection weights. Their upstream
vertex-stage source names remain **blocked/unresolved** in this pixel-only
trace.

## Color warp and coordinates

The status path begins after texture color correction, tint and vertex color:

```text
Cwarp = mix(Cprepared,
            sample3D(SFXColorWarp3D, Cprepared).rgb,
            colorWarpAmount)

offset = SFXScrollXYZ * time + SFXOffsetXYZ
Pstatus = (worldOrModelPosition - offset) / SFXScale
```

The 3D LUT is indexed directly by the prepared color. This operation is a
material-color transform, not a screen-space grade.

When `g_flSFXUseModelUVs > 0`, every surface map uses scaled and animated model
UVs. Otherwise normal, metalness, roughness, self illumination and detail are
sampled on the `ZY`, `XZ` and `XY` planes, then combined with the three
interpolated projection weights. The normal samples are transformed into each
projection frame before blending.

## Surface-property replacement

Each status map has an independent scalar amount:

```text
metalness' = mix(metalness, SFXMetalness.r, metalnessAmount)
roughness' = mix(roughness, SFXRoughness.r, roughnessAmount)
normal'    = normalize(mix(normal, projectedSFXNormal, normalAmount))
selfTint'  = mix(selfTint, SFXSelfIllum.rgb, selfIllumAmount)
selfMask'  = mix(selfMask, SFXSelfIllum.a, selfIllumAmount)
selfScale' = mix(selfScale, 1, selfIllumAmount)
```

The primed values replace the ordinary material inputs before Fresnel tint,
metal/F0 derivation, diffuse response, direct lights, probes, rim and final
composition. AO and the packed rim mask are unchanged.

## Detail blend modes

`g_flSFXDetailBlendMode <= 0` disables detail. Positive values are converted to
the zero-based branch index shown below:

| Authored value | Branch index | Recovered operation |
|---:|---:|---|
| `1` | `0` | add `detail.rgb` to warped base color |
| `2` | `1` | add `detail.rgb` to self-illum tint; self-illum mask is raised to at least detail luminance |
| `3` | `2` | multiply warped base color by `2 * detail.rgb` |
| other positive value | other | samples detail but leaves color/tint unchanged |

The triplanar branch applies `g_flSFXDetailAmount` while combining the three
detail projections. The model-UV branch samples `g_tSFXDetail` directly; no
separate amount multiplication is present in the reflected pixel program.

## Boundary to the base look

After these replacements, dynamic 2 rejoins the same opaque path documented in
`OPAQUE_OUTPUT_GRAPH.md`. It does not add a status-specific direct light,
specular lobe, rim lobe, environment highlight or final post-process term.

Ivy's ordinary hero preview should continue to use dynamic 0 semantics. A
Painter status implementation belongs in a separately gated optional mode and
requires captured status references plus textures/values for the selected
effect. It is not a missing component of the ordinary Deadlock character look.

## Evidence classification

- **Confirmed by static retail evidence:** all 26 added fields; color-warp
  placement; UV/triplanar branches; five independently blended material
  properties; detail blend operations; re-entry into the shared lighting graph.
- **Confirmed by pipeline/runtime:** current retail VCS identity, combo IDs,
  bytecode identities, program sizes and identical render states.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live SFX textures and values, vertex-stage semantic
  names for the two added interpolants, effect authoring conventions and visual
  references for individual gameplay statuses.

The base opaque family is now statically bounded. The next decomposition target
is the alpha-test family delta, followed by sheen, translucent, glass and
advanced translucency.
