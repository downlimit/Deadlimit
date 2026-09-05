# Reference Identity

This note records the first code-backed Deadlock material slice for issue #133.
It is a recovery document, not a claim of a final Painter match.

| Field | Value |
|---|---|
| Retail capture | Steam app `1422450`, buildid `24882156` |
| Selected material | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat_c` |
| Material SHA-256 | `74815a...406c` |
| Pixel module | `shaders/vfx/pbr_vulkan_60_ps.vcs` |
| Pixel-module SHA-256 | `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930` |
| Feature module | `shaders/vfx/pbr_vulkan_60_features.vcs` |
| Feature-module SHA-256 | `563eaa979a6741d9f300f5ef9aa2d3c5c543164648e64167bdf4ac7bdd20e1a3` |
| VCS platform/version | Vulkan / 70 |
| Inspector | local ValveResourceFormat `20.0.6980` (package commit `a06886f7d06049052d32a7381ec05523064a2ca0`) |

The model draw call and the complete material/texture provenance are recorded in
`DeadlimitShade/reference/ivy/ivy-bodyv3/manifest.md`. Retail packages, VCS
payloads, SPIR-V, and reflected source remain local and are intentionally not
part of this repository.

# Selected Shader Permutation

The material features select this feature-program state:

| Feature-program combo | Value | Pixel-program consequence |
|---|---:|---|
| `F_RENDER_BACKFACES` | 1 | raster-state/two-sided feature; absent from this PS static key |
| `F_SOLID_COLOR_OUTLINE` | 1 | permits the separate outline dynamic path; ordinary shading keeps `S_MODE_OUTLINE=0` |
| `F_USE_NPR_LIGHTING` | 1 | `S_USE_NPR_LIGHTING=1` |
| `F_USE_STATUS_EFFECTS_PROXY` | 1 | `S_USE_STATUS_EFFECTS_PROXY=1` |
| all other PS-relevant material flags | 0/default | `S_MODE_DEPTH`, `S_MODE_OUTLINE`, `S_MODE_TOOLS_WIREFRAME`, `S_ALPHA_TEST`, `S_TRANSLUCENT`, `S_SHEEN`, `S_GLASS`, `S_ENABLE_TEXTURE_TRANSFORMS`, `S_DETAIL`, `S_MODE_TOOLS_VIS`, `S_CLOAK`, `S_COSMIC_VEIL`, `S_UNLIT`, and `S_ADVANCED_TRANSLUCENCY` are zero |

The feature program computes feature ID `616574976`. The actual main forward
pixel permutation projects the two PS-relevant enabled flags to static-combo
ID **24** (`8 + 16`):

```text
S_USE_NPR_LIGHTING          = 1  (stride 8)
S_USE_STATUS_EFFECTS_PROXY  = 1  (stride 16)
all other PS static combos  = 0
```

Static combo 24 has 104 dynamic render states. Its all-default dynamic state
has ID `0`, selects shader file `0`, and has MD5
`f9a71b34-1a16-8b1e-5342-781d4c7f39ea` (76,732-byte SPIR-V payload). This is
the ordinary, no-extra-dynamic-feature inspection state used below.

`F_USE_STATUS_EFFECTS_PROXY` does not assert that a captured frame has an
effect applied. It makes the status-capable static family available. The
runtime switch is `D_USE_STATUS_EFFECTS_PROXY`: value `0` selects dynamic ID
`0`; value `1` produces dynamic ID `2` and selects shader file `1` (MD5
`9205cdf0-8ace-98bb-fd9c-a814a29d2d9f`). Its reflection adds two interpolants.
The static material capture has no engine frame-state evidence that could set
that dynamic switch, so status code is excluded from the first slice.

# NPR Inputs

The selected ordinary dynamic state reflects these material-local inputs and
their direct consumers.

| Input | Ivy value/source | Confirmed use in the selected PS |
|---|---|---|
| `g_tColor` | authored color VTEX, sRGB RGB | tinted base color |
| `g_tNormalRoughness` | `RG` hemi-octahedral normal, `B` roughness | reconstructed shading normal and roughness |
| `g_tAmbientOcclusion.R` | authored AO | NPR bounce weighting and final occlusion terms |
| `g_tNprTransmissiveColor` | sRGB constant texture from `TextureNprTramsissiveColor1 = [0.321569, 0.388235, 0.176471]` | added only to the NPR bounce/indirect term |
| `g_tTintMaskRimLightMask` | packed tint/rim texture | base/tint and optional rim paths |
| material flags/values | `g_bNPRBounceDiffuse`, `g_bNPRDirectDiffuse`, `g_bNPRDirectSpecular`, `g_bNPRRimLighting`, sharpness, wrap, step, exposure, and rim controls | fields in `_Globals_`; their current engine-populated values were not present in the VMAT capture |

The generated code also consumes data not authored by this material:

| Engine input | Role |
|---|---|
| view position and transforms | view vector, position, normal transforms, depth projection |
| light-probe volume / six directional probe coefficients | NPR bounce direction and irradiance |
| clustered, sun, and barn-light buffers | direct diffuse/specular loops |
| environment lookup array | indirect specular / BRDF response |
| screen depth | optional NPR rim-depth occlusion |
| status dynamic interpolants | only `D_USE_STATUS_EFFECTS_PROXY=1` |

# NPR Instruction Path

The selected SPIR-V was reflected locally with VRF/SPIRV-Cross. Its HLSL
backend rejected the `_Globals_.g_vNPRLightWeights` packing; the GLSL backend
completed reflection. The following is an instruction-path summary, rather
than committed reflected source.

1. The PS samples color, AO, packed normal/roughness, and
   `g_tNprTransmissiveColor` at UV0. It reconstructs the normal from packed
   `RG`; roughness is the sampled `B` channel.
2. `S_USE_NPR_LIGHTING` retains the regular PBR setup and introduces the
   `PerViewConstantBufferCitadel` NPR gate. A false gate takes the standard
   bounce path even in static combo 24.
3. With the gate true, `g_bNPRBounceDiffuse` selects a quantized directional
   probe evaluation. Its direction depends on the view vector,
   `g_vNPRLightWeights`, the main-light direction, reconstructed normal, AO,
   DfAO-like attenuation, and six probe directions. Optional exposure control
   adjusts that result.
4. The transmissive sample is multiplied by the upward probe result and by a
   hemisphere factor, then added to the NPR bounce result. It is therefore an
   **indirect diffuse/bounce tint**, not a standalone emission, direct-light
   tint, or final screen-color multiply.
5. `g_bNPRDirectDiffuse` can quantize direct `N·L` with wrap, sharpness, and
   `g_flNPRDiffusePbrBlend`. `g_bNPRDirectSpecular` similarly changes
   roughness, steps, sharpness, tint, and reflectance for direct specular.
   The shader repeats these branches for clustered/barn lights.
6. `g_bNPRRimLighting` optionally adds a rim term; its depth-occlusion branch
   samples scene depth. The final composition still includes ordinary
   material, ambient/IBL, specular, self-illumination, and rim contributions.

This placement resolves the key scope question: NPR modifies diffuse bounce,
direct diffuse, direct specular, and optional rim subpaths. It does not replace
the entire PBR final-composition stage with a single toon ramp.

# Reconstructed Equation

The notation below preserves confirmed operations while naming reflected
temporaries by role. `sat(x)` is clamp to `[0, 1]`; `Q(x, sharp)` is the
triangular fractional quantizer present in the instruction stream:

```text
f          = fract(x)
wing       = f > 0.5 ? 1 - f : f
p          = 1 / (1 - sharp)
Q(x,sharp) = floor(x) + (f > 0.5 ? 1 - pow(0.5,1-p)*pow(wing,p)
                                     :     pow(0.5,1-p)*pow(wing,p))

nRaw       = sat(0.5 + 2 * ((directWrap - 0.5) + NdotL - 0.5))
nNpr       = Q(nRaw, diffuseStepSharpness)
directDiff = mix(ooDirectLightNormalization * nNpr,
                 NdotL,
                 NPRDiffusePbrBlend)
```

The direct branch uses `directDiff` only when both the per-view NPR gate and
`g_bNPRDirectDiffuse` are true; otherwise it uses `NdotL`. The bounce branch
has this confirmed structure:

```text
probeDir       = normalize(zWeight*up + viewWeight*viewDir + lightWeight*sunDir)
aoWeighted     = min(AO, DfAOInfluence(...))
bounceCoord    = quantizedHemisphere(probeDir, N, aoWeighted, diffuseStepSharpness)
probeBounce    = SixDirectionalProbe(bounceCoord)
probeUp        = SixDirectionalProbe(up)
nprBounce      = exposureControl(probeBounce) +
                 exposureControl(probeUp) * sample(g_tNprTransmissiveColor).rgb *
                 (1 - hemisphereBlend)
```

The final expression is a structured composition, not a safe literal rewrite:

```text
final = standardDirectAndShadowedTerms
      + nprBounce * diffuseColor * NPRDiffuseResponse
      + optionalNprRim
      + standardIBLSpecular + selfIllumination + other enabled material terms
```

`NPRDiffuseResponse` is a polynomial diffuse response blended with one by
`g_flNPRDiffusePbrBlend`. The missing engine control values and probe buffers
prevent a calibrated final numerical equation; the material-local subequations
above are sufficient to bound the first Painter slice.

# Painter Availability Matrix

| Requirement | Painter availability | First-slice disposition |
|---|---|---|
| base color, metallic, roughness, AO channels | available in the current shader | usable |
| tangent frame, view vector, normal-map evaluation | available through Painter shader libraries | usable |
| Ivy packed normal/roughness source contract | available as an import/preprocess responsibility | usable after a dedicated packed-input convention is defined |
| transmissive constant color | available as a static exposed color value | usable |
| material-local NPR booleans and numeric controls | current values unavailable from the VMAT capture | defer until a values capture exists |
| a single artist-provided key light | can be represented as an approximation | useful only as a clearly labelled diagnostic |
| six-direction probe volumes, clustered lights, barn lights | unavailable | do not emulate as a parity claim |
| scene depth for NPR rim occlusion | unavailable in this Painter material context | defer |
| exact game environment/BRDF and shadow data | unavailable | retain Painter's standard environment response |
| status proxy dynamic inputs | unavailable and frame-dependent | exclude |

# Confirmed vs Hypothesis

## Confirmed

- Ivy enables NPR lighting and status-proxy feature families in its VMAT.
- Main opaque PS static combo 24 contains `S_USE_NPR_LIGHTING=1` and
  `S_USE_STATUS_EFFECTS_PROXY=1`.
- Normal packing, AO, base color, transmissive-color value and sRGB treatment
  are recorded in the Ivy manifest and consumed by the selected PS.
- The transmissive color enters the NPR **indirect bounce** term.
- Direct diffuse/specular, bounce, and rim have separate NPR-controlled paths.
- Status effects are a dynamic path: the default dynamic state is `0`; its
  enabled state is ID `2`.

## Hypothesis / deliberately deferred

- A Painter key-light approximation can communicate the direct-NPR shape.
- Painter environment lighting can stand in for the game probe/IBL system.
- A future exposed NPR control set can be assigned useful artist ranges from
  the recovered engine fields.
- Any visual claim of Ivy parity requires side-by-side capture under matching
  game lighting; none is made here.

# Unresolved Terms

- Values of the `_Globals_` NPR controls for the captured retail scene.
- Semantic names for several reflected per-view buffer members.
- Runtime state of `D_USE_STATUS_EFFECTS_PROXY`, `D_SOLID_OUTLINE`, and other
  dynamic combos for a particular game frame.
- Probe-volume coefficients, clustered/barn-light lists, shadow data, scene
  depth, and environment lookup selected by the engine.
- Exact display/color-management conversion surrounding the final PS output.

These are explicit boundaries for a future RenderDoc or engine-frame capture;
they are not inferred from a screenshot or substituted with arbitrary Painter
values.

# First Implementable Shader Slice

No GLSL changes are made in this PR. The first safe implementation scope is a
material-local, diagnosable slice:

1. Preserve the existing Painter metal/rough baseline and its base/roughness/
   metallic/AO diagnostics.
2. Add the documented Ivy packed-normal/roughness input contract only after a
   reproducible Painter import convention is chosen.
3. Expose the verified transmissive RGB constant as a material input and add a
   diagnostic view for it; keep it disconnected from final lighting until the
   required global NPR values are captured.
4. Implement the recovered direct-diffuse quantizer behind an explicitly
   labelled approximation control only after a test-light contract exists.
5. Leave status proxy, depth-occluded rim, probe bounce, clustered/barn lights,
   and parity tuning outside that slice.

This ordering gives a small, testable Painter change whose inputs and limits
are known, while avoiding an invented replacement for game-only lighting.

# Reproduction Notes

1. Read the installed Steam manifest for app `1422450` and record buildid
   `24882156`; do not alter the installation.
2. With local VRF `20.0.6980`, read
   `game/citadel/shaders_vulkan_dir.vpk` and hash the two VCS entries listed in
   Reference Identity.
3. Parse the feature module to obtain the four enabled VMAT flags. Parse the
   PS module and use its ordered mixed-radix static strides: NPR is `8` and
   status capability is `16`, producing static ID `24` for the ordinary opaque
   forward pass.
4. Resolve static combo 24. Dynamic ID `0` maps to shader file `0`; dynamic
   ID `2` maps to shader file `1`. Reflect only in a temporary location.
5. Inspect the reflected code path around the transmissive sample, NPR bounce,
   direct diffuse/specular, rim, and final composition. Keep VCS, SPIR-V,
   reflected source, generated textures, CSDK files, and Valve assets out of
   Git.
