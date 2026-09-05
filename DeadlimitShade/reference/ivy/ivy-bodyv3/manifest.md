# Identity

Capture date: 2026-09-05.

| Item | Retail identity | SHA-256 | Evidence |
|---|---|---|---|
| Steam app manifest | `D:\Program Files (x86)\Steam\steamapps\appmanifest_1422450.acf`; `buildid 24882156`; 1,013 bytes; modified `2026-09-04T17:14:01.5801720Z` | `065a6dc1e4c04372908da54f6a5543f3ad9fae79b0e7e8f7be788b0942f9bf5e` | Confirmed by retail / our pipeline |
| Main resource directory | `D:\Program Files (x86)\Steam\steamapps\common\Project8Staging\game\citadel\pak01_dir.vpk`; 6,890,162-byte directory file; modified `2026-08-23T14:09:47.7777856Z` | `3a12192e51306ef074656e6355fb940fad81e1be3be6b1ed030de1e1934df6dd` | Confirmed by retail / our pipeline |
| Vulkan shader directory | `D:\Program Files (x86)\Steam\steamapps\common\Project8Staging\game\citadel\shaders_vulkan_dir.vpk`; 13,249-byte directory file; modified `2026-07-31T08:20:32.6278337Z` | `5ae9ee6e4aa57dac4cba96dbff4d785f6770585ae447e90763527e39d781142d` | Confirmed by retail / our pipeline |
| Ivy model | `models/heroes_wip/ivy/ivy.vmdl_c`; 4,111,943 bytes | `6fca3312a1c4cb5cb9a88ed90c30a32c96c051f4dc96379b2e7ddeb2e6e1137a` | Confirmed by retail / our pipeline |
| Selected material | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat_c`; 5,263 bytes | `74815a7acecd73483268d2b5a4ea0db459e9c8e3f86f3b7552fc753659f406c1` | Confirmed by retail / our pipeline |

Resource hashes above and below cover the bytes returned for each entry by the
retail VPK reader. The `_dir.vpk` hashes cover the VPK directory files; chunk
identity remains tied to the recorded Steam build ID and resource-entry hashes.

Inspection used the repository's existing ValveResourceFormat NuGet dependency,
version `20.0.6980`, product commit
`a06886f7d06049052d32a7381ec05523064a2ca0`. The inspected
`ValveResourceFormat.dll` is 2,212,352 bytes, modified
`2026-08-17T16:37:00Z`, SHA-256
`54f93ba2a8b0e91ceecfff3570ce34a9f4746bf286c93b11fa363b74c164ee30`.
All selected resources, embedded mesh/VBIB data, seven VTEX resources, and the
three Vulkan SM 6 shader metadata resources parsed successfully. Evidence:
Confirmed by retail / our pipeline.

`C:\WorkProjects\Deadlock\DeadlockTools\DeadlockTools.exe` was present as
requested (file version `1.0.0.0`, product version
`1.0.0+ed8eda954f63dde4869b57b8976f9e873fe19187`, SHA-256
`0a7d29bebc20a6fe004ce075a7891b19f1bc4ba2a9e0dad5c861ec646699af27`).
Its current CLI handles Deadlimit add/fix/print workflows and does not expose a
resource-inspection command, so it was not used to interpret material data.
Evidence: Confirmed by retail / our pipeline.

# Selected Material

The current Ivy model resolves to the readable model name
`models/heroes_wip/ivy/ivy.vmdl`. Its LOD0 embedded render mesh is named `ivy`
and has these draw calls:

| Material | Vertex count | Index count | Vertex buffer | Interpretation | Evidence |
|---|---:|---:|---:|---|---|
| `models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3.vmat` | 2,701 | 18,762 | 0 | Ivy wing surface | Confirmed by retail / our pipeline |
| `models/heroes_staging/tengu/tengu_v2/materials/ivy_gearv3.vmat` | 25,110 | 176,448 | 0 | Ivy gear/accessory surface | Confirmed by retail / our pipeline |
| `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat` | 45,864 | 153,795 | 0 | Main Ivy body surface and selected ordinary opaque material | Confirmed by retail / our pipeline |
| `materials/dev/vertcolor_pbr_basic.vmat` | 1,562 | 7,896 | 1 | Separate developer vertex-color surface | Confirmed by retail / our pipeline |

The historical `ivy_bodyv3` candidate is therefore current: it is explicitly
assigned to the largest named body draw call in the current main Ivy model.
The same wings/gear/body material set is present in the inspected LOD1 and LOD2
embedded meshes. There was no second equally plausible ordinary body material.
Evidence: Confirmed by retail / our pipeline.

The selected compiled material declares `pbr.vfx`. It contains the resource
blocks `RERL`, `RED2`, `DATA`, and `INSG`, and has representative dimensions
4,096 x 4,096. Evidence: Confirmed by retail / our pipeline.

# Shader Family

The current retail Vulkan SM 6 shader family matching `pbr.vfx` is:

| Resource | Size | SHA-256 | Parsed identity | Evidence |
|---|---:|---|---|---|
| `shaders/vfx/pbr_vulkan_60_features.vcs` | 10,242 | `563eaa979a6741d9f300f5ef9aa2d3c5c543164648e64167bdf4ac7bdd20e1a3` | `pbr`, Vulkan, SM 6.0, VCS 70; 340 variables; 20 channel processors | Confirmed by retail / our pipeline |
| `shaders/vfx/pbr_vulkan_60_ps.vcs` | 9,230,833 | `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930` | `pbr`, Vulkan, SM 6.0, VCS 70; 283 variables; 18 channel processors | Confirmed by retail / our pipeline |
| `shaders/vfx/pbr_vulkan_60_vs.vcs` | 876,922 | `5c794dce069096d9bfc6e7e3c9f2702d8746f801b903c0ecb7c840196120a5d5` | `pbr`, Vulkan, SM 6.0, VCS 70; 59 variables; 2 channel processors | Confirmed by retail / our pipeline |

Only static-combo names, parameter definitions, channel processors, and render
state expressions needed to identify this material contract were inspected.
No Valve shader source is included here.

The features metadata expresses culling as
`CullMode = F_RENDER_BACKFACES ? 0 : 1` and depth writing as
`DepthWriteEnable = F_DISABLE_DEPTH_WRITE ? 0 : 1`. For this material,
backface rendering is enabled and `F_DISABLE_DEPTH_WRITE` is absent, so the
selected permutation disables culling and writes depth. Evidence: Confirmed by
retail / our pipeline.

# VMAT Parameters

## Integer and feature-like parameters

| Name | Value | Evidence |
|---|---:|---|
| `F_RENDER_BACKFACES` | 1 | Confirmed by retail / our pipeline |
| `F_SOLID_COLOR_OUTLINE` | 1 | Confirmed by retail / our pipeline |
| `F_USE_NPR_LIGHTING` | 1 | Confirmed by retail / our pipeline |
| `F_USE_STATUS_EFFECTS_PROXY` | 1 | Confirmed by retail / our pipeline |
| `g_bMaskColorTint1` | 1 | Confirmed by retail / our pipeline |
| `g_bMaskVertexColorTint1` | 1 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoord2UByModelScaleAxis` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoord2UByModelScaleOrigin` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoord2VByModelScaleAxis` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoord2VByModelScaleOrigin` | 2 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoordUByModelScaleAxis` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoordUByModelScaleOrigin` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoordVByModelScaleAxis` | 0 | Confirmed by retail / our pipeline |
| `g_nScaleTexCoordVByModelScaleOrigin` | 2 | Confirmed by retail / our pipeline |
| `g_nTextureColorTintMode1` | 0 | Confirmed by retail / our pipeline |

## Scalar parameters

| Name | Value | Evidence |
|---|---:|---|
| `g_flAlbedoScrollQuantize1` | 0 | Confirmed by retail / our pipeline |
| `g_flHighlightAOStrength1` | 1 | Confirmed by retail / our pipeline |
| `g_flHighlightCoverage1` | 0 | Confirmed by retail / our pipeline |
| `g_flHighlightHardness1` | 0 | Confirmed by retail / our pipeline |
| `g_flHighlightNormalStrength1` | 64 | Confirmed by retail / our pipeline |
| `g_flHighlightRadius1` | 256 | Confirmed by retail / our pipeline |
| `g_flHighlightTintBrightness1` | 1 | Confirmed by retail / our pipeline |
| `g_flInvertHighlight1` | 0 | Confirmed by retail / our pipeline |
| `g_flNormalAndRoughnessScrollQuantize1` | 0 | Confirmed by retail / our pipeline |
| `g_fSolidOutlineVertexColorTint` | 0 | Confirmed by retail / our pipeline |
| `g_fVertexColorStrength1` | 1 | Confirmed by retail / our pipeline |

## Vector and constant-texture parameters

| Name | Value | Evidence |
|---|---|---|
| `g_vAlbedoContrastSaturationBrightness1` | `[1, 1, 1, 0]` | Confirmed by retail / our pipeline |
| `g_vAlbedoScrollSpeed1` | `[0, 0, 0, 0]` | Confirmed by retail / our pipeline |
| `g_vColorTint1` | `[1, 1, 1, 0]` | Confirmed by retail / our pipeline |
| `g_vHighlightPositionWs1` | `[0, 0, 72, 0]` | Confirmed by retail / our pipeline |
| `g_vHighlightTint1` | `[1, 1, 1, 0]` | Confirmed by retail / our pipeline |
| `g_vNormalAndRoughnessScrollSpeed1` | `[0, 0, 0, 0]` | Confirmed by retail / our pipeline |
| `g_vSolidOutlineAdditive` | `[0.243137, 0.164706, 0.164706, 1]` | Confirmed by retail / our pipeline |
| `g_vSolidOutlineTint` | `[0.164706, 0.054902, 0.054902, 1]` | Confirmed by retail / our pipeline |
| `TextureMetalness1` | `[0, 0, 0, 0]` | Confirmed by retail / our pipeline |
| `TextureNormal1` | `[0.501961, 0.501961, 1, 0]` | Confirmed by retail / our pipeline |
| `TextureNprOutlineMask1` | `[0.819, 0.819, 0.819, 0]` | Confirmed by retail / our pipeline |
| `TextureNprTramsissiveColor1` | `[0.321569, 0.388235, 0.176471, 0]` | Confirmed by retail / our pipeline |

`TextureNprTramsissiveColor1` is the spelling present in current shader
metadata; the compiled texture parameter uses the corrected
`g_tNprTransmissiveColor` spelling. Evidence: Confirmed by retail / our
pipeline.

The material has no dynamic expressions and no float, vector, or string
attributes beyond the representative-size integer attributes. Evidence:
Confirmed by retail / our pipeline.

# Feature Combos

The material explicitly sets four static feature combos:

| Combo | Value | Contract effect | Evidence |
|---|---:|---|---|
| `F_RENDER_BACKFACES` | 1 | two-sided/no-cull render state | Confirmed by retail / our pipeline |
| `F_SOLID_COLOR_OUTLINE` | 1 | solid-color outline path enabled | Confirmed by retail / our pipeline |
| `F_USE_NPR_LIGHTING` | 1 | NPR lighting path enabled | Confirmed by retail / our pipeline |
| `F_USE_STATUS_EFFECTS_PROXY` | 1 | status-effect proxy path enabled | Confirmed by retail / our pipeline |

Current `pbr` metadata also exposes the binary combos `F_ALPHA_TEST`,
`F_TRANSLUCENT`, `F_ADDITIVE_BLEND`, `F_VERTEX_COLOR`,
`F_ENABLE_TEXTURE_TRANSFORMS`, `F_SECONDARY_UV`, `F_SHEEN`, `F_GLASS`,
`F_SELF_ILLUM`, `F_UNLIT`, `F_NO_SPECULAR_AT_FULL_ROUGHNESS`,
`F_OVERRIDE_NPR_OUTLINE`, `F_DISABLE_NPR_OUTLINE`, and
`F_DISABLE_DEPTH_WRITE`. They are absent from the material's stored feature
state. This selects the default/off values for those binary combos. The
resulting contract is opaque, depth-writing, lit, single-UV, and without the
self-illumination or vertex-color permutations. Evidence: Confirmed by retail /
our pipeline.

# Texture Inputs and Channel Processing

## Compiled resources

| Compiled material slot | Retail VTEX_C | Size and format | SHA-256 | Evidence |
|---|---|---|---|---|
| `g_tAmbientOcclusion` | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_ao_png_8fb08de7.vtex_c` | 2,754,628 bytes; 2048 x 2048; ATI1N; 3 mips; linear | `dfeb0ce0bd72a71c89595557e5565f5c28dc8f64d52d86b5918a778c49894eaa` | Confirmed by retail / our pipeline |
| `g_tColor` | `models/heroes_wip/ivy/materials/ivy_bodyv3_color_png_9ca4a4c5.vtex_c` | 22,371,716 bytes; 4096 x 4096; BC7; 11 mips; sRGB RGB sampling | `e473091bed9fd44e33182f1a015f15c12a28003ba033a3b4d9fc78aed8148e32` | Confirmed by retail / our pipeline |
| `g_tNormalRoughness` | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_rough_png_ed44f497.vtex_c` | 5,594,596 bytes; 2048 x 2048; BC7; 10 mips; linear | `a26bfe514744d57501e7b17d2459ae22adcedec48906658bde0b267ec1e9a2e5` | Confirmed by retail / our pipeline |
| `g_tNprOutlineMask` | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_vmat_g_tnproutlinemask_18d6b9ce.vtex_c` | 2,024 bytes; logical 1 x 1 (stored 4 x 4); ATI1N; linear | `b41b828a819f0cea2bed6f871fec48916fc14c50490d9f6819f03ba57852130c` | Confirmed by retail / our pipeline |
| `g_tNprTransmissiveColor` | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_vmat_g_tnprtransmissivecolor_b9eaffac.vtex_c` | 2,032 bytes; logical 1 x 1 (stored 4 x 4); BC7; sRGB RGB sampling | `56459da0f1f0c1a48a04a27635269e4f0558c84cb8b4c61f80e3deacd0fb3a1e` | Confirmed by retail / our pipeline |
| `g_tSelfIllumMask` | `materials/default/default_mask_tga_344101f8.vtex_c` | 2,184 bytes; logical 1 x 1 (stored 4 x 4); ATI1N; linear | `840642c5b46c6741273b2c70bb599ba9d67fc27044bebd8338e2e4ca6722557d` | Confirmed by retail / our pipeline |
| `g_tTintMaskRimLightMask` | `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_rim_png_8b8d7c82.vtex_c` | 5,594,720 bytes; 2048 x 2048; ATI2N; 10 mips; linear | `156bc1042e423a451e9865f6307b0f27ff5df552982dfd8ea231d589c941a0a1` | Confirmed by retail / our pipeline |

## Logical sources and channel contract

| Compiled slot/channel | Logical VMAT source | Compile/decode processing | Consumption | Evidence |
|---|---|---|---|---|
| `g_tAmbientOcclusion.R` | `TextureAmbientOcclusion1` -> `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_ao.png` | `Box3`, scalar/linear; source dependency CRC `1174318557` | ambient occlusion | Confirmed by retail / our pipeline |
| `g_tColor.RGB` | `TextureColor1` -> `models/heroes_wip/ivy/materials/ivy_bodyv3_color.png` | `Box`, color mode 1; RGB is sampled as sRGB; source dependency CRC `3154108282` | base color | Confirmed by retail / our pipeline |
| `g_tColor.A` | constant `TextureMetalness1 = 0` | `Box`, scalar/linear into alpha | metalness = 0 | Confirmed by retail / our pipeline |
| `g_tNormalRoughness.RG` in compiled storage | constant flat `TextureNormal1 = [0.501961, 0.501961, 1]` | normalized normal plus `HemiOctIsoRoughness_RG_B` packing | hemi-octahedral tangent normal | Confirmed by retail / our pipeline |
| `g_tNormalRoughness.B` in compiled storage | `TextureRoughness1` -> `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_rough.png` | `Inverse` image processor, then `HemiOctIsoRoughness_RG_B`; source dependency CRC `3305538568` | processed roughness | Confirmed by retail / our pipeline |
| decoded normal/roughness view | compiled `RG` normal + `B` roughness | exact VRF decoder reconstructs normal into `RGB` and moves stored `B` roughness to `A` | readable/export comparison layout: normal `RGB`, roughness `A` | Confirmed by current external source |
| `g_tNprOutlineMask.R` | constant `TextureNprOutlineMask1 = 0.819` | `Box`, scalar/linear | NPR outline mask | Confirmed by retail / our pipeline |
| `g_tNprTransmissiveColor.RGB` | constant `TextureNprTramsissiveColor1 = [0.321569, 0.388235, 0.176471]` | `Box`, color mode 1; sampled as sRGB | NPR transmissive color | Confirmed by retail / our pipeline |
| `g_tSelfIllumMask.R` | `TextureSelfIllumMask1` -> `materials/default/default_mask.tga` | `Box`, scalar/linear; source dependency CRC `3793837628` | inactive because `F_SELF_ILLUM` is off | Confirmed by retail / our pipeline |
| `g_tTintMaskRimLightMask.R` | `TextureTintMask1` -> `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_rim.png` | `Box`, scalar/linear | tint mask | Confirmed by retail / our pipeline |
| `g_tTintMaskRimLightMask.G` | `TextureRimLightMask1` -> compiler-generated/readable `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3_rim_8b8d7c82_mask.png` | `Box`, scalar/linear; packed with the source whose dependency CRC is `1905968784` | rim-light mask | Confirmed by retail / our pipeline |

The `HemiOctIsoRoughness_RG_B` layout is established by both current retail
shader channel metadata and the exact ValveResourceFormat revision used for
inspection. That revision's decoder preserves `B` as roughness, reconstructs
the normal from `RG`, and presents roughness in decoded alpha. Evidence:
Confirmed by current external source.

The material extractor identifies four authored input groups: AO, color,
roughness, and the packed tint/rim pair. The other compiled VTEX entries are
generated constants or the shared default mask. Extracted comparison images
remain local and are not part of this package. Evidence: Confirmed by retail /
our pipeline.

# Mesh Inputs

The selected LOD0 body draw call uses vertex buffer 0 with 45,864 vertices and
28-byte stride:

| Semantic | Format | Offset | Contract | Evidence |
|---|---|---:|---|---|
| `POSITION0` | `R32G32B32_FLOAT` | 0 | object-space position | Confirmed by retail / our pipeline |
| `TEXCOORD0` | `R16G16_SNORM` | 12 | `LowPrecisionUv`; observed range `[0,0]` through `[1,1]` | Confirmed by retail / our pipeline |
| `NORMAL0` | `R32_UINT` | 16 | compressed tangent frame | Confirmed by retail / our pipeline |
| `BLENDINDICES0` | `R8G8B8A8_UINT` | 20 | skinning indices | Confirmed by retail / our pipeline |
| `BLENDWEIGHT0` | `R8G8B8A8_UNORM` | 24 | skinning weights | Confirmed by retail / our pipeline |

The selected buffer has no `COLOR0` and no secondary UV element. Its material
input signature includes `TEXCOORD0` and no `COLOR` semantic. With
`F_VERTEX_COLOR`, `F_SECONDARY_UV`, and `F_ENABLE_TEXTURE_TRANSFORMS` absent,
the selected contract consumes UV0 directly and does not consume mesh vertex
color. Zero scroll speeds, zero scroll quantization, and zero model-scale axes
agree with that direct mapping. Evidence: Confirmed by retail / our pipeline.

The separate developer-material draw call uses vertex buffer 1 and does carry
`COLOR0` (observed range `[0.031373, 0.023529, 0.019608, 1]` through
`[1, 1, 1, 1]`). That attribute belongs to
`materials/dev/vertcolor_pbr_basic.vmat`; it is outside the selected material
contract. Evidence: Confirmed by retail / our pipeline.

# Special Character-Shading Inputs

| Mechanism | Current Ivy body contract | Evidence |
|---|---|---|
| NPR lighting | Enabled by `F_USE_NPR_LIGHTING`; constant NPR transmissive color is `[0.321569, 0.388235, 0.176471]` | Confirmed by retail / our pipeline |
| Outline | Solid-color outline enabled; outline tint `[0.164706, 0.054902, 0.054902, 1]`, additive `[0.243137, 0.164706, 0.164706, 1]`, constant outline mask `0.819` | Confirmed by retail / our pipeline |
| Rim light | `g_tTintMaskRimLightMask.G` supplies the rim-light mask | Confirmed by retail / our pipeline |
| Texture tint | `g_tTintMaskRimLightMask.R` supplies the tint mask; tint is identity white and tint mode is 0 in this material | Confirmed by retail / our pipeline |
| Highlight | Coverage `0`, hardness `0`, invert `0`, tint brightness `1`, white tint; four additional stored highlight fields are listed under Unknowns | Confirmed by retail / our pipeline |
| Status effects | Proxy permutation enabled by `F_USE_STATUS_EFFECTS_PROXY` | Confirmed by retail / our pipeline |
| Vertex color | Stored mask/strength fields exist, but the permutation is off and selected mesh/signature contain no color input | Confirmed by retail / our pipeline |
| Emissive | Shared default mask is referenced; `F_SELF_ILLUM` is off, so the emissive path is inactive | Confirmed by retail / our pipeline |
| Alpha/translucency | Alpha test, translucent, and additive permutations are off; the body path is opaque | Confirmed by retail / our pipeline |
| SSS/skin | No SSS/skin feature or texture parameter is present in this selected VMAT contract | Confirmed by retail / our pipeline |
| Normal | Constant flat normal is packed with the authored roughness input | Confirmed by retail / our pipeline |

# Evidence Classification

- **Confirmed by retail / our pipeline**: a value read from the current retail
  build, or a deterministic interpretation obtained by joining the selected
  VMAT, current matching VCS metadata, VTEX dependencies, and selected mesh
  input data with the recorded inspection pipeline.
- **Confirmed by current external source**: behavior verified in the exact
  upstream ValveResourceFormat source revision corresponding to the inspected
  DLL. This classification is used only to explain the tool's packed-texture
  decode behavior.
- **Hypothesis**: a proposed visual or implementation interpretation that still
  requires a controlled Painter/retail comparison. No hypothesis is promoted
  to a confirmed contract in this manifest.

# Unknowns

- The selected resource contract is established; its final pixel-level visual
  response has not been reproduced in Painter. Any parity claim remains a
  Hypothesis.
- `g_flHighlightAOStrength1`, `g_flHighlightNormalStrength1`,
  `g_flHighlightRadius1`, and `g_vHighlightPositionWs1` are stored in the VMAT,
  while exact-name lookup found no matching variable in the current Vulkan SM 6
  features, vertex, or pixel metadata. They may be stale material fields or be
  consumed indirectly; either interpretation is a Hypothesis.
- The exact artistic response produced by tint, rim, NPR transmissive color,
  outline, highlight, and status-effect proxy controls is outside this capture.
  Their inputs and activation states are confirmed; their Painter equivalents
  remain Hypotheses.
- The main model extraction reported one missing ancillary animation clip,
  `models/heroes_staging/tengu/tengu_v2/dmx/animation/reload_quick.vnmclip_c`.
  Mesh, material, texture, and VCS parsing completed, so this does not block the
  selected render-material contract. Evidence: Confirmed by retail / our
  pipeline.

Unresolved blockers: none. All required paths resolved, the selected resources
parsed, there was one unambiguous ordinary Ivy body material, and every consumed
texture channel has an established source/constant and processing path.

# Reproduction Notes

1. Verify Steam app `1422450` reports `buildid 24882156` in
   `D:\Program Files (x86)\Steam\steamapps\appmanifest_1422450.acf`, then verify
   the file identities in `# Identity`.
2. With ValveResourceFormat `20.0.6980` / commit
   `a06886f7d06049052d32a7381ec05523064a2ca0`, open the primary retail VPK
   read-only and parse `models/heroes_wip/ivy/ivy.vmdl_c`.
3. Enumerate its embedded mesh draw calls and select the body draw call assigned
   to `models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat`.
   Confirm that the body assignment repeats in LOD1 and LOD2.
4. Parse the corresponding `_c` material, record all typed parameter maps and
   texture references, and inspect its `INSG` input signature.
5. Read each referenced VTEX_C entry from the same VPK. Record the compiled
   texture metadata, edit/dependency metadata, byte hash, and any constant or
   authored input association. Use extracted images only as local comparison
   inputs.
6. Open the three `pbr_vulkan_60_*.vcs` resources from
   `shaders_vulkan_dir.vpk`. Match material texture variables to channel
   processor indices and match explicit feature values to static-combo/render
   state metadata.
7. Read the selected mesh's VBIB declaration/data and verify UV0 range and the
   absence of `COLOR0` and a secondary UV in vertex buffer 0.

All inspection was read-only. No retail or Reduced CSDK asset was modified.
Reduced CSDK data was not needed or inspected. This package contains only this
manifest: no Valve binary, texture, extracted image, decompiled material/model,
shader source, or temporary probe output is committed.

# Next Recommended Shader Slice

Implement one original-input packed normal/roughness decode slice for
`g_tNormalRoughness`: accept the retail packed texture, decode hemi-octahedral
normal from `RG`, expose the processed roughness stored in `B`, and add focused
debug views for both outputs. Validate those views against the locally extracted
decoded normal `RGB` / roughness `A` reference before changing BRDF, NPR,
outline, rim, or highlight behavior. Evidence: Confirmed by retail / our
pipeline for the channel contract; any resulting Painter parity is a Hypothesis
until that comparison passes.
