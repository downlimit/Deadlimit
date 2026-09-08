# Deadlock alpha-test character delta

Date: 2026-09-08

## Scope and identity

This document reduces the alpha-test character family to its delta from the
ordinary opaque program in `OPAQUE_OUTPUT_GRAPH.md`.

- parent resource and SHA-256: `shaders/vfx/pbr_vulkan_60_ps.vcs`,
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- ordinary family: static combo `24`, dynamic `0`, file `0`, 76,732-byte
  SPIR-V;
- alpha-test family: static combo `56`, dynamic `0`, file `0`, 78,072-byte
  SPIR-V, SHA-256
  `c09682f7e8e8d5f0af078c29e0e8169eb4ef9cdd3c218c4897332183106b16c6`;
- active alpha-test axes: `S_ALPHA_TEST=1`, `S_USE_NPR_LIGHTING=1`,
  `S_USE_STATUS_EFFECTS_PROXY=1`.

Combo 56 retains combo 24's 104 permitted dynamic states and 72 shader files.
Its dynamic-0 pixel render state is identical to combo 24 dynamic 0.

## Channel-layout delta

The alpha-test family changes the material channel contract:

| Value | Opaque combo 24 | Alpha-test combo 56 |
|---|---|---|
| base color | `g_tColor.rgb` | `g_tColor.rgb` |
| metalness | `g_tColor.a` | `g_tMetalness.r` |
| cutout opacity | unavailable | `g_tColor.a` |

This split is required for alpha-tested character parts. Treating color alpha
as metalness in this family would corrupt both the cutout and the BRDF.

## Recovered discard equation

After base-color, AO and normal/roughness sampling, the alpha-test program
computes:

```text
angleBase = max(0.001,
                angleScale * abs(dot(viewAxis, shadingNormal)) + angleBias)
angleTerm = pow(angleBase, abs(anglePower))

distance01 = saturate(length(cameraRelativePosition / alphaBoostDistance))
distanceTerm = mix(1, alphaBoostStrength, distance01)

coverage = vertexColor.a
         * colorTexture.a
         * angleTerm
         * distanceTerm

if coverage < alphaTestReference:
    discard
```

The five added scalar/vector controls are:

- `g_flAlphaTestReference1`;
- `g_flAlphaAnglePower1`;
- `g_flAlphaAngleScaleBias1`;
- `g_flAlphaBoostStrength`;
- `g_flAlphaBoostDistance`.

`g_tMetalness` is the sixth added `_Globals_` field. The generic per-view name
behind `viewAxis` and the exact upstream coordinate name behind
`cameraRelativePosition` remain **blocked/unresolved**; their roles above are
assigned from direct dataflow.

## Boundary to shared lighting

The discard runs before highlight, self illumination, probe bounce, sun/barn
lights, direct NPR diffuse/specular, rim and environment/local-probe specular.
Surviving pixels use the same contribution topology as ordinary opaque, with
metalness supplied by `g_tMetalness.r`. The final alpha remains the ordinary
`1 - g_flIsAdditive`; coverage is implemented through pixel discard.

Combo 56 dynamic 2 adds the already isolated status-proxy material modifier on
top of this channel layout. Its alpha test still uses vertex alpha and
`g_tColor.a`; status metalness blends from the separate alpha-family
`g_tMetalness.r`.

## Painter consequence

Deadlimit's current retail texture bridge already keeps decoded source files
unchanged. A future alpha-tested Painter path must explicitly bind:

1. `Color.rgb` to base color;
2. `Color.a` to opacity/cutout coverage;
3. the separate metalness texture to metalness;
4. a controlled threshold, with angle and distance terms independently
   diagnosable before any calibration.

No alpha-test approximation is added to the current Ivy opaque preview by this
stage.

## Evidence classification

- **Confirmed by static retail evidence:** channel-layout split; six added
  fields; discard ordering and equation; shared post-discard lighting graph;
  status proxy composition over the alpha-test layout.
- **Confirmed by pipeline/runtime:** current retail resource identity, combo
  axes, 104/72 topology, bytecode identity/size and matching render state.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live threshold/angle/distance values, generic per-view
  source names, material-by-material alpha-test usage and visual parity.

The sheen family is continued in `SHEEN_DELTA.md`; translucency follows.
