# Deadlock sheen character delta

Date: 2026-09-08

## Scope and identity

This document reduces the sheen family to its delta from ordinary opaque.

- parent VCS SHA-256:
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- static combo `152`: `S_SHEEN=1`, `S_USE_NPR_LIGHTING=1`,
  `S_USE_STATUS_EFFECTS_PROXY=1`;
- dynamic `0`, file `0`: 81,856-byte SPIR-V, SHA-256
  `2566cb636c7a76a79d690497786ce14ce7fd7cafd5d87ecc266745fb7b0e73a0`;
- topology: 104 permitted dynamic states and 72 shader files;
- dynamic-0 pixel render state: identical to ordinary combo 24.

## Added material contract

The family adds `g_tSheen`, `g_mSheenTextureColorTint`,
`g_bSheenMaskColorTint1`, `g_bSheenMaskVertexColorTint1` and
`g_fSheenVertexColorStrength1`.

```text
sheenColorRaw = g_tSheen.rgb
sheenRoughness = max(g_tSheen.a, 0.05)
```

The color passes through its own tint matrix and vertex-color multiplication.
Both can be masked by `g_tTintMaskRimLightMask.r`.

## Direct-light lobe

For the sun and every accepted barn light, with `H = normalize(L + V)`:

```text
alpha = sheenRoughness^2
Dsheen = (2 + 1/alpha) / (2*pi)
       * pow(max(0, 1 - dot(N,H)^2), 0.5/alpha)
Vsheen = saturate(0.25 / (NdotL + NdotV - NdotL*NdotV))
sheenDirect = sheenColor * Dsheen * Vsheen * NdotL
            / max(sheenEnergyLut, 1)
regularEnergy = max(0, 1 - max(sheenColor) * sheenEnergyLut)
```

`sheenEnergyLut` comes from layer 2 of the engine BRDF lookup. Regular direct
diffuse and specular are attenuated by `regularEnergy`; `sheenDirect` is added
to regular specular before shared radiance, shadow and cookie multiplication.
The sheen lobe bypasses the NPR specular step quantizer.

## Probe and environment lobe

The environment path samples BRDF-LUT layer 2 using sheen roughness and
`sqrt(1 - NdotV)`. It attenuates ordinary environment specular, evaluates a
sheen-colored local/fallback probe contribution, applies the shared exposure
and occlusion path plus a roughness/view/AO grazing factor, and adds the result
as a separate final RGB contribution.

Generic probe-buffer source names remain unresolved. The distinct lobe, LUT
layer, energy attenuation and final placement are confirmed by dataflow.

## Painter consequence

A faithful sheen preview needs its own packed RGB+roughness input, tint/vertex
controls, direct lobe, energy compensation and compatible probe/environment
lobe. Ordinary specular and roughness controls cannot reproduce this family
consistently across camera and light motion. No sheen approximation is added to
the current Ivy opaque preview by this stage.

## Evidence classification

- **Confirmed by static retail evidence:** five added fields and channel
  layout; tint path; direct distribution/visibility; energy attenuation;
  BRDF-LUT layer 2; separate environment/probe contribution.
- **Confirmed by pipeline/runtime:** current retail VCS identity, combo axes,
  104/72 topology, bytecode identity/size and matching render state.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live sheen textures/tint matrices, probe-buffer source
  names, compatible Painter environment radiance and pixel parity.

The next material-family target is translucency.
