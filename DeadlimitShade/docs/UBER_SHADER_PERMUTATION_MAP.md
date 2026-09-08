# Deadlock character uber-shader permutation map

Date: 2026-09-08

## Scope

This is the first stage of the full Deadlock character-shader decomposition.
It maps the current Vulkan SM 6 pixel program into material families and
runtime-effect families before further equations are ported to Painter.

The inspected retail resource is
`shaders/vfx/pbr_vulkan_60_ps.vcs`, VCS 70, SHA-256
`eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`
from Steam app `1422450`, build `25173285`. Inspection was read-only. Generated
reports and reflected GLSL remain in `.scratch` and outside Git.

## Reproduction

`Deadlimit.ShaderInspector` enumerates the VCS metadata directly from the
retail VPK. It writes JSON metadata and hashes by default. `--decompile` also
writes reflected source into the selected output directory; use it only with a
disposable local `.scratch` path.

```powershell
dotnet run --project .\DeadlimitShade\tools\Deadlimit.ShaderInspector -- `
  --vpk "<Deadlock>\game\citadel\shaders_vulkan_dir.vpk" `
  --resource "shaders/vfx/pbr_vulkan_60_ps.vcs" `
  --output ".\DeadlimitShade\.scratch\shader-inspector" `
  --static-combos "8,24,26,56,88,152,280,32824"
```

The current resource contains 16 static axes, 323 permitted static entries,
16 dynamic axes, and a mixed-radix ID layout. These counts and IDs are
**confirmed by static retail evidence**.

## Static axes

| Stride | Static axis | Role |
|---:|---|---|
| 1 | `S_MODE_DEPTH` | depth-only mode |
| 2 | `S_MODE_OUTLINE` | outline mode |
| 4 | `S_MODE_TOOLS_WIREFRAME` | tool wireframe |
| 8 | `S_USE_NPR_LIGHTING` | shared character NPR lighting |
| 16 | `S_USE_STATUS_EFFECTS_PROXY` | status-capable character family |
| 32 | `S_ALPHA_TEST` | cutout material family |
| 64 | `S_TRANSLUCENT` | translucent family |
| 128 | `S_SHEEN` | sheen material extension |
| 256 | `S_GLASS` | glass/refraction family |
| 512 | `S_ENABLE_TEXTURE_TRANSFORMS` | transformed/scrolling UV path |
| 1024 | `S_DETAIL` | detail texture path |
| 2048 | `S_MODE_TOOLS_VIS` | tool visualization mode |
| 4096 | `S_CLOAK` | cloak-capable static family |
| 8192 | `S_COSMIC_VEIL` | cosmic-veil family |
| 16384 | `S_UNLIT` | unlit family |
| 32768 | `S_ADVANCED_TRANSLUCENCY` | advanced translucency family |

Of the 323 permitted static entries, 138 contain NPR lighting, 143 contain
status support, and 61 contain both. Their coexistence does not make every
combination a base character material; depth, outline, tool, cloak, unlit and
translucency modes remain separate rendering responsibilities.

## Ivy resolution

The current Ivy body, wings and gear materials all resolve to pixel static
combo **24**:

```text
S_USE_NPR_LIGHTING = 1
S_USE_STATUS_EFFECTS_PROXY = 1
all other PS static axes = 0
```

Body and wings additionally enable `F_RENDER_BACKFACES`; gear does not. That
feature changes rasterization before the pixel program and does not create a
different PS lighting combo. `F_SOLID_COLOR_OUTLINE` makes the separate outline
family available; ordinary shading still uses `S_MODE_OUTLINE=0`.

The developer vertex-color Texture Set uses additional material features and
is outside the three retail hero materials. `F_SELF_ILLUM` and
`F_VERTEX_COLOR` are represented through material variables in this PS family,
not through additional entries in the 16-axis PS static key.

## Dynamic axes

| Stride | Dynamic axis | Ordinary combo-24 disposition |
|---:|---|---|
| 1 | `D_OUTPUT_MOTION_VECTORS` | permitted only with forward-normal output |
| 2 | `D_USE_STATUS_EFFECTS_PROXY` | distinct status-effect pixel code |
| 4 | `D_SOLID_OUTLINE` | excluded; used by outline static family 26 |
| 8 | `D_CLIP_BEHIND_VEIL` | permitted only with forward-normal output |
| 16 | `D_ENABLE_CLOAK` | transient effect family |
| 32 | `D_OPAQUE_FADE` | transient fade family |
| 64 | `D_DISCARD_IN_FRONT_OF_REFRACTIVE` | permitted with cloak |
| 128 | `D_SATURATION_VOLUMES` | scene-effect family |
| 256 | `D_GLITCH` | transient effect family |
| 512 | `D_DYNAMIC_HIGHLIGHT` | aliases existing combo-24 PS files |
| 1024 | `D_BAKED_LIGHTING_FROM_LIGHTMAP` | excluded from combo 24 |
| 2048 | `D_BAKED_LIGHTING_FROM_VERTEX_STREAM` | excluded from combo 24 |
| 4096 | `D_DISTANCE_FIELD_OCCLUSION` | distinct occlusion-enabled pixel code |
| 8192 | `D_MBOIT_PASS` (`0..2`) | excluded from combo 24 |
| 24576 | `D_SEE_THRU_WALLS` (`0..2`) | excluded from combo 24 |
| 73728 | `D_RENDER_FORWARD_NORMALS` | diagnostic/auxiliary output family |

Static combo 24 permits 104 dynamic IDs backed by 72 unique SPIR-V files:

- ordinary shading starts at dynamic `0`, shader file `0`;
- status proxy uses dynamic `2`, shader file `1`;
- adding `D_DYNAMIC_HIGHLIGHT=1` produces IDs `512` and `514`, which map back
  to files `0` and `1` with identical PS render state. This proves PS-code
  equivalence in combo 24; VS behavior and runtime uniform changes remain
  separate questions;
- distance-field occlusion occupies shader files `24..39`;
- forward-normal output occupies files `40..71` and is excluded from the
  ordinary shaded-output decomposition;
- cloak, fade, saturation and glitch variants are transient effects and do not
  define the default hero look.

The outline static family is combo `26`. Its `D_SOLID_OUTLINE=1` state is
dynamic ID `4`, shader file `4`, with only 1,600 bytes of SPIR-V versus 69,672
bytes for the family's default full-material state. This is direct evidence
that the solid outline is a dedicated output path. A dark Fresnel term inside
ordinary character shading cannot reproduce this program boundary.

## Representative material families

| Static combo | Added family | Dynamic states / files | Default SPIR-V bytes | Relevant interface delta from combo 24 |
|---:|---|---:|---:|---|
| 8 | NPR, no status capability | 12 / 12 | 75,176 | status/highlight sphere controls absent |
| 24 | Ivy opaque NPR + status | 104 / 72 | 76,732 | common opaque character baseline |
| 26 | separate outline mode | 32 / 32 | 69,672 | `D_SOLID_OUTLINE` path available |
| 56 | alpha-tested NPR + status | 104 / 72 | 78,072 | alpha reference/angle/boost plus separate metalness texture |
| 88 | translucent NPR + status | 16 / 8 | 83,276 | opacity/fog inputs; depth-occluded rim controls absent |
| 152 | sheen NPR + status | 104 / 72 | 81,856 | sheen texture and independent tint/vertex masks |
| 280 | glass NPR + status | 48 / 24 | 94,040 | glass, framebuffer copy, fog and refraction/cloak-blur inputs |
| 32824 | alpha-tested advanced translucency | 96 / 64 | 80,056 | alternate translucency textures, blend mode and UV scroll controls |

The byte counts are code-size evidence and do not measure visual importance.
Interface deltas are identifier-set differences in reflected dynamic-0 GLSL;
they establish branch inputs, while exact runtime values and active draw states
remain unresolved.

## Decomposition order established by this map

1. Finish the common opaque output graph using combo 24 / dynamic 0.
2. Compare combo 24 / dynamic 2 only to bound status-proxy additions.
3. Port material-family deltas in this order: alpha test 56, sheen 152,
   translucent 88, glass 280, advanced translucency 32824.
4. Keep outline combo 26 as its existing independent Painter mesh/shader path.
5. Treat cloak, fade, saturation, glitch, forward normals and MBOIT as
   optional effect/output families after the base hero look is accepted.

This order prevents transient game effects from contaminating the ordinary
Painter character preview.

## Evidence classification and remaining boundary

- **Confirmed by pipeline/runtime:** `Deadlimit.ShaderInspector` builds and
  reproduces the JSON report from the installed retail VPK.
- **Confirmed by static retail evidence:** VCS identity; all static and dynamic
  axes; 323/104/72 counts; ID-to-file mappings; representative family
  interfaces and code sizes; the dedicated solid-outline program.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** the runtime-selected dynamic ID for a particular game
  draw; engine-bound NPR globals; the VS contribution of dynamic highlight;
  probe, light and shadow buffers; post-processing; material families not yet
  reduced to equations.

The combo-24/dynamic-0 contribution topology is continued in
`docs/OPAQUE_OUTPUT_GRAPH.md`; its optional dynamic-2 material modifier is
isolated in `docs/STATUS_PROXY_DELTA.md`. The next material-family delta is
alpha test, documented in `docs/ALPHA_TEST_DELTA.md`; sheen follows.
