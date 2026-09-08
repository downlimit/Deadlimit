# Deadlock opaque character output graph

Date: 2026-09-08

## Scope and identity

This document completes the contribution topology for the ordinary Ivy pixel
program: `pbr_vulkan_60_ps.vcs`, static combo `24`, dynamic combo `0`, shader
file `0`, 76,732-byte SPIR-V, SHA-256
`03af15c800051c118177d27a99b6cbfdd39b7b491e6fc2216611030afd490229`.
The parent VCS SHA-256 is
`eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`.

The graph covers every term added to `output_0.rgb`. Semantic names below are
assigned from direct dataflow and VCS variable names. Generic external-buffer
members retain an explicit unresolved label where retail metadata does not
publish a source name.

## Pipeline overview

```text
material textures + vertex inputs
        |
        v
base/tint/highlight/self-illum preparation -----> emissive add
        |
        +----> metalness, roughness, normal, AO, rim mask
                         |
                         +----> six-direction/local probes ----> NPR bounce
                         |
                         +----> sun shadow + sun BRDF ----------+
                         |                                      |
                         +----> barn lights/shadows/cookies -----+--> direct diffuse/specular
                         |                                      |
                         +----> rim from pre-material lighting --+
                         |
                         +----> environment BRDF/specular -------+
                                                                |
                                                                v
                                                          final RGB + alpha
```

## Material decode

| Semantic value | Retail source / operation | Reflected anchor |
|---|---|---|
| tint mask | `g_tTintMaskRimLightMask.R` | `_19372.x` / `_14784` |
| rim mask | `g_tTintMaskRimLightMask.G` | `_19372.y` / `_10391` |
| source color | `g_tColor.RGB` | `_19414.rgb` |
| metalness | `g_tColor.A` | `_19414.a` / `_10891` |
| AO | `g_tAmbientOcclusion.R` | `_19068.x` / `_17476` |
| roughness | `g_tNormalRoughness.B` | `_19718.z` / `_7776` |
| shading normal | hemi-octahedral decode of `g_tNormalRoughness.RG`, then tangent-frame transform | `_24566` |
| transmissive color | `g_tNprTransmissiveColor.RGB` | `_19415.rgb` |
| self-illum mask | `g_tSelfIllumMask.R` when enabled | `_13136` |

Color preparation occurs before lighting:

1. optional vertex-color multiplication is gated independently before or after
   texture tint;
2. `g_mAlbedoColorCorrect` and `g_mTextureColorTint` transform the color, with
   the tint mask selecting the second transform;
3. the material highlight controls can replace this color with
   `g_vHighlightTint1` using a normal/view ramp and an optional world-space
   sphere bound;
4. self-illum Fresnel tint is applied, then the lit base color is blended toward
   the self-illum tint by `selfIllumMask * saturate(selfIllumScale)`.

The highlight branch above is a pre-light material-color override. It is
separate from direct NPR specular. Ivy stores
`g_flHighlightCoverage1 = 0`, so this branch is inactive on the selected body
material. `D_DYNAMIC_HIGHLIGHT=1` also aliases the same combo-24 PS file and
render state; any remaining behavior can only arrive through changed uniforms
or another shader stage.

## Probe and indirect graph

The shader gathers local probe volumes selected by clustered cull bits. Each
volume can contribute:

- a normal-directed diffuse value;
- six directional diffuse values (`+X`, `+Y`, `+Z`, `-X`, `-Y`, `-Z`);
- a local cubemap specular value;
- blend/occlusion weights.

Uncovered weight falls back to the per-view ambient transform. The ordinary
normal-directed result is `B_regular`. When the per-view NPR gate and
`g_bNPRBounceDiffuse` are enabled, the shader constructs:

```text
probeDirection = normalize(
    up * g_vNPRLightWeights.x +
    viewToCamera * g_vNPRLightWeights.y +
    sunDirection * g_vNPRLightWeights.z)

dfaoWeight = min(AO, remap(DfAO, g_vNPRDfaoInfluenceRange))
hemisphere = quantize(0.5 + 0.5*dot(probeDirection, N),
                      dfaoWeight, g_flNPRDiffuseStepSharpness)
B_directional = evaluateSixDirections(hemisphereDirection)
B_up = evaluateSixDirections(up)
B_npr = exposureControl(B_directional)
      + exposureControl(B_up) * transmissiveColor * lowerHemisphereWeight
```

`B = B_npr` for the enabled NPR branch and `B = B_regular` otherwise. Exposure
control preserves sampled chromaticity while fitting luminance toward the
engine-provided targets. Its enable gate and numerical targets are runtime
buffer values.

## Direct-light graph

The main sun and every accepted barn light use the same material response:

```text
lambert = saturate(dot(N, L))
wrapped = saturate(0.5 + 2*((wrap - 0.5) + lambert - 0.5))
D_npr   = mix(normalization * triangularQuantize(wrapped, sharpness),
              lambert,
              diffusePbrBlend)
```

This trace corrects the earlier signed-input classification: both direct loops
saturate `dot(N,L)` before the wrap operation. The current Ivy approximation
uses `wrap = 0.48`; signed and saturated inputs produce the same clamped result
for that profile, so the recorded Milestone B viewport remains unchanged.

The stepped GGX-like direct-specular equation documented in `MaterialModel.md`
is evaluated once for the sun and once for every barn light. Sun radiance is
multiplied by its shadow/cascade visibility. Barn-light radiance can include:

- clustered spatial culling and distance/shape attenuation;
- a projected or spherical cookie;
- a 16x4 gathered stochastic shadow filter;
- a DfAO/shadow blend;
- per-light minimum roughness.

The accumulators are:

```text
Ld = engineDirectBase
   + sunRadiance * sunVisibility * directDiffuseResponse
   + sum(barnRadiance_i * barnVisibility_i * directDiffuseResponse_i)

Ls = sunRadiance * sunVisibility * directSpecularResponse
   + sum(barnRadiance_i * barnVisibility_i * directSpecularResponse_i)
```

The semantic identity of `engineDirectBase`
(`PerViewLightingConstantBufferGpu_t._m3`) is **blocked/unresolved**. Its
placement at the initial value of `Ld` is confirmed by static code.

## Rim graph

The non-depth rim factor is:

```text
viewRamp = pow(saturate((dot(N, cameraToPixel) + wrap)/(1 + wrap)^2), falloff)
upRamp   = saturate((N.z - upRampMin)/(upRampMax - upRampMin))
rimFactor = viewRamp * upRamp * strength * AO * rimMask
rim = (Ld + B) * rimFactor
```

If `g_bNPRRimLightingDepthOcclusion` is enabled, a projected offset point is
compared against the scene-depth texture and multiplies `rimFactor`. Painter's
surface shader has no equivalent scene-depth input, so this branch remains
unimplemented there.

The retail shader uses Z as world up. Painter's Y-up conversion is an explicit
pipeline adaptation.

## Final composition

Let:

- `C` be the prepared base color;
- `M` be metalness;
- `R` be roughness;
- `A = AO * DfAO`;
- `P(C,A)` be the recovered cubic NPR diffuse response;
- `Frough` be the optional near-full-roughness specular fade;
- `E = selfIllumMask * selfIllumScale`;
- `LenvSpec` be the standard environment/local-probe specular term.

The final dynamic-0 RGB topology is:

```text
diffuseResponse = NPR gate
    ? mix(1, P(C,A), g_flNPRDiffusePbrBlend)
    : P(C,A)

diffuseColor = C * (1 - M * Frough)

output.rgb = (Ld + B * diffuseResponse) * diffuseColor
           + rim
           + C * E
           + Ls
           + LenvSpec

output.a = 1 - g_flIsAdditive
```

`LenvSpec` is a distinct retail contribution. It combines a roughness/view BRDF
lookup, local and fallback environment radiance, probe-derived specular
occlusion, Fresnel/metal tint, and a roughness/grazing visibility factor.
Painter's stock panorama specular did not match this path and produced the
previous double-highlight failure. A compatible environment term requires its
own controlled reconstruction before it can return to Deadlimit Shaded.

## Constant-buffer and resource ownership

| Owner | Confirmed code role | Remaining semantic boundary |
|---|---|---|
| `_Globals_` | named material textures, material switches, all NPR controls | actual per-draw NPR values |
| `PerViewConstantBuffer_t` | transforms, camera origin, time, viewport/depth conversion, exposure scale | several generic `_mN` source names |
| `PerViewConstantBufferCitadel_t` | NPR gate, exposure gate, DfAO/depth/BRDF handles | runtime values and exact engine writer |
| `PerViewLightingConstantBufferGpu_t` | fallback ambient transform, sun, cluster grids, shadows, probe and cookie handles | generic member names and live contents |
| `PerViewLightProbeVolumeConstantBuffer_t` | probe transforms, volume bounds, six-direction textures | live probe coefficients/textures |
| `g_CullBits` | clustered probe and barn-light selection | live cluster contents |
| `g_BarnLights` | barn-light geometry, radiance, attenuation, cookies and shadows | live light list and authoring values |

## Painter coverage after this graph

| Retail contribution | Current Painter state |
|---|---|
| base/tint/metalness/roughness/AO/normal | connected from retail inputs |
| NPR direct diffuse | recovered equation with calibrated controls |
| NPR direct specular | recovered equation with calibrated controls |
| non-depth rim | recovered equation/masks with calibrated controls |
| self illumination | material-local approximation available |
| six-direction probe bounce | fixed-color/directional calibrated approximation |
| sun/barn attenuation and shadowing | unavailable |
| depth-occluded rim | unavailable |
| compatible environment/local-probe specular | unresolved and intentionally excluded |
| engine tone mapping/post-processing | unresolved |

## Evidence classification

- **Confirmed by static retail evidence:** material decode order; highlight and
  self-illum placement; six-direction probe topology; saturated direct input;
  repeated sun/barn NPR equations; rim source; final contribution order; the
  separate environment-specular term.
- **Confirmed by pipeline/runtime:** the inspector reproduced the selected
  shader file and its 1,827-line reflection from current retail bytes.
- **Calibrated approximation:** existing Painter light directions/intensities,
  bounce replacement, NPR controls and Y-up conversion.
- **Blocked/unresolved:** bound buffer values, unnamed external-buffer member
  semantics, live shadow/probe/light resources, depth rim, compatible
  environment specular, tone mapping and pixel parity.

The dynamic-2 status-proxy delta is isolated in `STATUS_PROXY_DELTA.md`. It
remains separate from the ordinary-look implementation unless a captured
reference actually activates it. The next material-family target is alpha
test.
