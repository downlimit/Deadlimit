# Deadlimit Shade — Validation Protocol

Status: bootstrap protocol.

Updated: 2026-09-09.

## Purpose

Validation must isolate one unknown at a time. The first tests prove Painter API/resource plumbing; later tests prove Deadlock material behavior against current retail resources.

## Test A — `Deadlock_Hero.glsl` compile/load

Input: any ordinary opaque Painter test mesh with a normal material Texture Set.

Steps:

1. Add `Deadlock_Hero.glsl` to Painter's shader resources/shelf.
2. Assign it to the test Texture Set.
3. Confirm that Painter reports no shader compile error.
4. Confirm that the following `Debug View` entries appear:
   - Shaded;
   - Base Color;
   - Roughness;
   - Metallic;
   - Ambient Occlusion;
   - Vertex Color RGB;
   - Vertex Color Alpha.
5. Keep `Vertex Color Multiply = 0`.
6. Confirm that `Shaded` behaves as the normal Painter metal/rough baseline.

Result to record:

```text
Painter version:
Deadlock_Hero.glsl: PASS / FAIL
Compile error, if any:
Unexpected viewport behavior, if any:
```

A compile failure is handled before any Deadlock-specific shader code is added.

## Test B — hero input diagnostics

Use a mesh/material with known channel values.

Check exactly one diagnostic at a time:

- Base Color debug returns the sampled Base Color input;
- Roughness debug returns grayscale roughness;
- Metallic debug returns grayscale metallic;
- Ambient Occlusion debug returns raw Painter AO, without scene shadow multiplication;
- Vertex Color RGB returns mesh `color0.rgb`;
- Vertex Color Alpha returns mesh `color0.a`.

Acceptance: diagnostic outputs agree with the known source data. Do not compensate for a failed input mapping in the shading model.

## Test C — `Deadlock_Outline.glsl` compile/load

Input: a mesh containing a dedicated material named exactly:

```text
__deadlimit_outline
```

Steps:

1. Confirm Painter creates a dedicated Texture Set for `__deadlimit_outline`.
2. Create a unique Shader Instance for that Texture Set.
3. Assign `Deadlock_Outline.glsl` to it.
4. Confirm no shader compile error.
5. Confirm `Outline Color` appears.
6. Change `Outline Color` and verify flat unlit output.

Result to record:

```text
Painter version:
Dedicated Texture Set: PASS / FAIL
Unique Shader Instance: PASS / FAIL
Deadlock_Outline.glsl: PASS / FAIL
Outline Color: PASS / FAIL
```

## Test D — manual inverted-hull proof

This test validates the controlled geometry/culling contract before the shell
generator is generalized to imported character meshes.

Generate a disposable controlled mesh:

```powershell
pwsh -NoProfile -File DeadlimitShade/tools/New-ShaderTestSphere.ps1 `
  -OutputPath "$env:TEMP/deadlimit-shade-outline-preview.obj" `
  -OutlineWidth 0.04
```

The generated OBJ contains `DeadlimitTest` hero geometry and the reserved
`__deadlimit_outline` shell material. It is derived test data and must remain
outside the repository.

Prepare a preview-only two-material mesh:

1. keep the original mesh unchanged;
2. duplicate the render geometry;
3. offset duplicate vertices outward by a small known distance using the chosen render normals;
4. reverse duplicate triangle winding;
5. assign `__deadlimit_outline` to the duplicate;
6. export the combined preview mesh;
7. import it into Painter;
8. assign `Deadlock_Hero.glsl` to original Texture Sets;
9. assign `Deadlock_Outline.glsl` to the shell Texture Set.

Checks:

- outline extends outside the original silhouette;
- front-facing original surfaces are not replaced by outline color;
- rotating the camera does not expose the wrong shell side;
- disconnected pieces behave predictably;
- obvious split-normal/hard-edge explosions are absent;
- original Vertex Color is unchanged;
- original material assignments are unchanged.

If reversed winding plus `cull_face on` fails, record the exact observed face behavior before changing the convention.

## Test E — width regeneration proof

Before Painter automation exists:

1. produce preview mesh A with width `w1`;
2. produce preview mesh B from the same source with width `w2`;
3. reimport/refresh B in the same Painter project using Painter's supported mesh-update workflow;
4. verify authored project data remains attached to the original Texture Sets;
5. verify only the silhouette width changes as expected.

This proves that `Outline Width` can be owned outside GLSL without destroying authoring state.

Before the Painter check, run the repository contract:

```powershell
pwsh -NoProfile -File DeadlimitShade/tests/outline-mesh-contract-smoke.ps1
```

It proves that two different widths preserve identical hero vertices, change
only shell displacement, retain the dedicated material split, reverse every
shell triangle and remain byte-deterministic for identical inputs. It cannot
prove Painter culling or project-state preservation.

## Test F — first Ivy retail-input parity

Run only after Tests A/B pass.

Required inputs come from the current Ivy reference manifest.

For the selected material compare, one channel at a time:

1. Base Color;
2. Normal orientation;
3. Roughness;
4. Metallic;
5. AO;
6. Vertex Color, if consumed;
7. UV set;
8. other verified texture/mask channels.

Use original retail inputs. Newly authored Painter textures are excluded from this stage.

Acceptance: shader inputs are proven before the Deadlock-specific response is reconstructed.

## Evidence record format

For each material mechanism added after bootstrap, record:

```text
Feature:
Reference hero/material:
Retail resource(s):
Controlling VMAT parameter(s):
Texture/channel input:
Observed retail behavior:
Painter implementation:
Validation result:
Evidence class:
Date / retail build context:
```

Allowed evidence classes:

- Confirmed by retail / our pipeline
- Confirmed by current external source
- Hypothesis

## Regression rule

After a feature becomes part of the common hero shader, changing it requires rechecking every reference material that previously established the feature as common. A hero-specific failure does not justify changing the common model until the mechanism causing the difference is identified.

## Test G — character profile contract

Run:

```powershell
pwsh -NoProfile -File DeadlimitShade/tests/profile-contract-smoke.ps1
```

The smoke verifies:

- every profile matches `profiles/schema.json`;
- IDs and keys are unique and ID `0` remains reserved for `Custom`;
- embedded profile blocks in both shaders are reproducible from the JSON profiles;
- hero and outline shaders expose matching `Custom` and Ivy IDs;
- both shaders contain the generated profile resolver;
- the recovered NPR quantizer is identity at zero sharpness, monotonic and
  symmetric over the normalized sweep.

In Painter, select the `Character Profile` debug view and switch between
`Custom` and Ivy. The diagnostic color must change. Enable
`Use Custom Calibration` while Ivy is selected; the diagnostic must return to
the custom color.

## Test H — Apply Deadlimit integration

Run the static integration contract:

```powershell
pwsh -NoProfile -File DeadlimitShade/tests/painter-apply-contract-smoke.ps1
```

In an existing Ivy Painter project, open the `Deadlimit Shade` dock, select
Ivy and press `Apply Deadlimit`. Acceptance for the current integration slice:

- the installed plugin is enabled once from `Python > deadlimit_apply` and its
  Painter `launch_at_start` setting remains `on`;

- the action reads width and color from `profiles/ivy.json`;
- the generated mesh becomes the current project mesh;
- `__deadlimit_outline` maps to `Deadlimit Outline`;
- the profile's `ivy_builder_arms`, `ivy_builder_body` and `ivy_builder_head`
  Texture Sets map to `Deadlimit Hero`;
- all other source Texture Sets retain their previous Shader Instance;
- both instances receive character ID 1 from the single selector;
- the status reports elapsed time, `Applied Ivy`, 1.0 mm and the profile RGB;
- viewport inspection shows a registered silhouette shell.

Painter's Base Color solo mode displays the authored Base Color channel and may
show the outline Texture Set as gray. Profile outline color is accepted in
Material mode, where `Deadlock_Outline` supplies its unlit emissive result.

The processor must duplicate every mesh in the selected FBX/GLB/glTF scene for
the outline. Character-specific split vertices remain literal source data.
Validation must not weld or reinterpret them. The three Ivy builder Texture
Sets control hero shader assignment only.

## Test I — NPR direct-diffuse isolation

Use a smooth sphere or another mesh with a continuous range of world-space
normal directions. Keep Base Color, Metallic, Roughness and AO constant.

1. Select `Custom` and disable `NPR Direct Diffuse`.
2. Record `N dot L (Signed)` and `Final Direct Diffuse`.
3. Enable `NPR Direct Diffuse`.
4. Inspect `Wrapped Direct Diffuse`, then `Quantized Direct Diffuse`.
5. Set `Diffuse Step Sharpness = 0`; quantized output must equal wrapped input.
6. Increase sharpness and confirm the transition contracts toward a step while
   remaining continuous at the midpoint.
7. Set `Diffuse PBR Blend = 1`; final direct diffuse must return to clamped
   Lambert response. The wrapped branch must consume that saturated Lambert
   value, matching both recovered direct-light loops.
8. Select Ivy with `Use Custom Calibration` disabled and record the resulting
   profile response.

The test-light direction, color, intensity, environment, camera and exposure
must remain unchanged for all images. The Ivy values are calibration settings;
this test establishes deterministic Painter behavior and does not claim exact
retail runtime values.

## Runtime attempt record — 2026-09-06

```text
Painter version: 9.1.0
Generated two-material mesh: PASS
Repository outline mesh contract: PASS
Painter process readiness: PASS
Painter remote scripting readiness: PASS (::1:60041)
Painter GUI automation access: PASS
Exact preview OBJ imported through Python API: PASS
Dedicated hero/outline Texture Sets: PASS
Independent Shader Instances: PASS
Deadlock_Hero.glsl compile/load: PASS
Deadlock_Outline.glsl compile/load: PASS
Custom outline viewport: PASS
Ivy synchronized profile viewport: PASS
Ivy -> Custom round trip: PASS
Outline culling across camera rotation: PASS
Project-state-preserving width refresh: PASS (0.04 -> 0.08 -> 0.04)
Generic imported-OBJ shell generation: PASS on controlled fixture and sphere
Actual Ivy character import and outline viewport: PASS
Actual Ivy preserving width reload: PASS (0.5 -> 1.0 diagnostic widths)
Actual Ivy boundary/artifact policy: BLOCKED on controlled reference
```

The initial IPv4 probe failed because this Painter process listened on IPv6
loopback. `Get-NetTCPConnection` identified `::1:60041`; the official
`/run.json` endpoint then returned Painter version `9.1.0`.

The API created the project from the exact disposable OBJ and asserted the
Texture Set names `DeadlimitTest` and `__deadlimit_outline`. The JavaScript API
created independent `Deadlimit Hero` and `Deadlimit Outline` instances. Painter's
runtime log recorded successful creation of both shaders. Computer Use viewport
captures proved the black `Custom` silhouette, synchronized green/red Ivy
diagnostic colors, restoration of the original `Custom` result and correct
silhouette culling after camera rotation. Preserving reloads visibly changed
the controlled outline width `0.04 -> 0.08 -> 0.04`; the API verified before
reassignment that Texture Set mapping plus hero/debug/outline profile values
survived each reload.

Reusable automation:

```powershell
& DeadlimitShade/tools/Open-PainterShadePreview.ps1 `
  -PreviewMesh "$env:TEMP/deadlimit-shade-outline-preview.obj" `
  -Character Ivy `
  -CharacterDiagnostic
```

Before any combined outline attempt, validate the character shader against an
already open disposable copy of the textured character SPP:

```powershell
& DeadlimitShade/tools/Open-PainterShadePreview.ps1 `
  -PreviewMesh '<ProjectRoot>\IvyBuilder\4texture\texture_ivy_builder.fbx' `
  -UseExistingProject `
  -HeroOnly `
  -Character Ivy
```

`-HeroOnly` requires one or more source Texture Sets, creates only the
`Deadlimit Hero` Shader Instance and maps every Texture Set to it. It does not
require or search for `__deadlimit_outline`. This separates character shader
validation from preview-mesh generation. A successful API summary proves the
project/mesh/Texture Set/resource mapping; viewport acceptance still requires
visual evidence.

Use `-UseExistingProject` only when the currently open project was created from
that exact preview mesh. The tool refuses to replace an unrelated open project.

For the controlled width-refresh proof, generate a second mesh from the same
source parameters and request a preserving reload:

```powershell
& DeadlimitShade/tools/Open-PainterShadePreview.ps1 `
  -PreviewMesh "$env:TEMP/deadlimit-shade-outline-preview-wide.obj" `
  -ReloadExistingProject `
  -Character Ivy `
  -CharacterDiagnostic
```

The reload path uses Painter's `MeshReloadingSettings(preserve_strokes=True)`.
Before reassigning anything, it verifies that Texture Set mapping and the hero,
debug-view and outline profile values survived unchanged.

For an imported OBJ with explicit per-corner render normals, create derived
preview geometry outside the repository:

```powershell
pwsh -NoProfile -File DeadlimitShade/tools/New-OutlinePreviewMesh.ps1 `
  -SourceMesh "C:/path/to/character-source.obj" `
  -OutputPath "$env:TEMP/deadlimit-character-preview.obj" `
  -OutlineWidth 0.04
```

The builder copies every source line before appending derived shell data. The
default `SplitRenderNormal` mode implements the literal inverted-hull prototype:
every split render vertex is duplicated and offset along its existing normal.
`SourcePosition` and `WeldedPosition` are explicit diagnostic experiments;
neither is a claimed Deadlock rule. Original materials, UVs, normals and
vertex-color fields remain untouched. The tool rejects in-place output, the
reserved outline material, missing per-corner normals, zero normals and invalid
indices.

Run the imported-mesh contract before Painter:

```powershell
pwsh -NoProfile -File DeadlimitShade/tests/outline-preview-mesh-smoke.ps1
```

This repository test cannot approve character-specific hard edges, open
boundaries, non-manifold regions or thin/disconnected accessories. Those remain
visual gates on an actual controlled character export.

### Actual Ivy character attempt

The read-only 2026-09-06 inventory found these existing IvyBuilder OBJ files:

| Source | Positions | UVs | Normals | Faces | Materials |
| --- | ---: | ---: | ---: | ---: | ---: |
| `body003.obj` | 19,963 | 0 | 0 | 39,884 | 0 |
| `node_0.obj` | 101,460 | 126,243 | 0 | 201,879 | 1 |
| `node_1.obj` | 197,612 | 248,471 | 0 | 391,448 | 1 |

All three lack `vn` records. They remain inadmissible inputs because their
original per-corner render-normal directions cannot be preserved.

The controlled replacement path used the Autodesk DCC 2025.3 to import read-only
`IvyBuilder/4texture/texture_ivy_builder.fbx` and export only nodes matching
`ivy_ivy*`. The OBJ exporter reported normals, texture coordinates and materials
enabled. The disposable source contained 35,224 positions, 14,314 UVs, 96,252
normals, 32,084 faces and two material assignments. Its SHA-256 remained
`90015822F53008EA30D0D746BABF6C2BB58A871F87542DFA69C8D2196F6A4F3C`
before and after preview generation. No FBX/OBJ was added to the repository.

Painter created `wire_057008136`, `wire_225087143` and
`__deadlimit_outline`; automation mapped both source Texture Sets to
`Deadlock_Hero` and the reserved set to `Deadlock_Outline`. The viewport showed
the real Ivy character and a width-dependent silhouette. Reloading diagnostic
width `0.5` to `1.0` retained all Shader Instance/profile mapping.

At deliberately large diagnostic widths, the visual also showed internal shell
exposure at hard/material boundaries and a detached lower component already
present in the source pose. A per-coordinate weld experiment stopped on
coincident position `5.2022|64.4351|4.2772`, whose referenced normals cancel to
zero. That experiment is not the shipping algorithm. The next gate is static or
runtime evidence for Deadlock's vertex-stage outline expansion plus a controlled
Ivy reference at millimetre-scale width.

### Apply Deadlimit integration attempt

The `Deadlimit Shade` Painter dock was loaded into the existing
`subs_ivy_builder.spp` session, then the project was saved to
`subs_ivy_builder_deadlimit.spp`. The source project and
`texture_ivy_builder.fbx` remained unchanged.

```text
Apply Deadlimit control visible: PASS
Single character selector: PASS (Ivy, ID 1)
Profile outline width: PASS (1.0 mm)
Profile outline color: PASS (0.164706, 0.054902, 0.054902)
Disposable FBX generation: PASS
Complete source scene outlined: PASS (74 / 74 meshes)
Painter preserving mesh reload: PASS
Deadlimit Hero assignment: PASS (arms/body/head)
Deadlimit Outline assignment: PASS
Hero/outline character ID synchronization: PASS (1 / 1)
World-space outline registration: VISUAL PASS
All original Texture Sets preserved: PASS (7 source + outline)
Unselected source shader assignments preserved: PASS (4 / 4 Main shader)
Visible progress and elapsed time: PASS (5.5 seconds measured)
Fresh-plugin source-path recovery: PASS
FBX same-format conversion: PASS
GLB same-format scale preservation: PASS
glTF same-format scale preservation: PASS
```

The self-contained converter completed the actual Ivy FBX conversion in under
one second; Painter mesh reload and shader assignment brought the measured
button-to-result time to 5.5 seconds. A progress bar, named phase and live
elapsed time remained visible during reload. The resulting 211.8 MB project
reopened with the same eight Texture Sets and three Shader Instances.

## Milestone B — fixed Ivy lighting skeleton — 2026-09-08

### Deterministic scene contract

The validation used one disposable copy of the existing Ivy Painter project.
The source SPP, source FBX, retail files and CSDK were left unchanged.

| Setting | Fixed value |
| --- | --- |
| Painter | 9.1.0, OpenGL |
| Project copy | `subs_ivy_builder_signed_ndotl.spp` (temporary; excluded from Git) |
| Preview mesh | `ivy-c549a9a7a47ff2ef.fbx` |
| Pose | unchanged pose and detached weapon component from the source Ivy scene |
| Camera position | `149.768860, 50.999405, 15.292190` |
| Camera rotation | `140.143265, 99.880875, -140.561340` degrees |
| Camera projection | perspective, FOV 50 degrees |
| Environment | `resource://viewer/panorama?version=df0d611fe5a4c86ca5a36bcc11fc06b363478e03.image` |
| Environment rotation | 145 degrees |
| PBR exposure | 0.66 EV |
| Environment exposure | 1.0 EV |
| Display | Material |
| Character profile | Ivy, stable ID 1 |
| Outline | existing 1.0 mm inverted hull, RGB 0.164706/0.054902/0.054902 |
| Controlled visual reference | user-supplied Deadlock Tools Ivy capture, `Lighting Preview -> Default` |

The Deadlock Tools capture has different framing and renderer support, so it
calibrates broad diffuse separation, restrained highlights and edge hierarchy.
It cannot establish engine runtime constants or pixel parity.

### Diagnostic-first visual gate

All captures used the scene above without camera, environment or exposure
changes. The first five used `Diagnostic Neutral`; the final capture restored
`Material / Retail`.

```text
NPR direct diffuse: VISUAL PASS — broad quantized light/shadow break
Controlled specular: VISUAL PASS — isolated stepped highlight patches
Rim contribution: VISUAL PASS — retail-mask-gated silhouette response
NPR composite: VISUAL PASS — all three terms present together
Painter PBR baseline: CAPTURED — same mesh/camera/environment/exposure
NPR composite vs Painter PBR: VISUAL PASS — obvious reproducible delta
Retail shaded integrity: PASS — retail maps, eye color and outline retained
Retail Deadlock-style lighting skeleton: VISUAL PASS — readable matte diffuse,
restrained stepped copper highlights and no simultaneous Painter HDRI specular
Painter light rotation: VISUAL PASS — signed N dot L changes at 145/235 degrees
Sampler artifacts: PASS — no cyan/blue/checker output
Painter viewport capture: PASS — actual Painter window captured
```

Capture hashes are evidence identifiers only; the PNGs remain in `.scratch`
and are excluded from Git.

| Capture | SHA-256 |
| --- | --- |
| Direct diffuse | `671B5F394EB2D97A4B98C2A8C154E990A7636F01F3F1E2658064CC75CD70BC4C` |
| Direct specular | `30AB5D8E243F0E85C533D018E8A79C03F1FBBD60C3E4BE25C536FAF2198D3008` |
| Rim | `E96AB0AFD9A580617C15D4E4F4E55218977103E232169A6DEBB940B73B9C558B` |
| NPR composite | `C66FCEB48AF8E5E316A1379E1D024FD80EF16D1A369F35DECD8C9E2EECA391D7` |
| Painter PBR | `67DABADD0F16DB3A549490537855A19AF776A162D03C0CCC48FF09F0033922AD` |
| Retail shaded | `DCDD49282D6BF1B348C43F39EE7F9C888B6487EBD4DFD0742EB30380DD1CD5A1` |
| Contact sheet | `E0A9D5320ADB95F639A2F3EA5E2124B1C9D8D6C33C99A7C1FE2D1501F809CD4C` |

### Evidence classification

| Result or value | Classification |
| --- | --- |
| Painter shader compilation, live parameters, shader-instance assignment and saved disposable SPP | Confirmed by pipeline/runtime |
| Retail texture/material identities, eye `color$0` dependency and outline color | Confirmed by static retail evidence |
| Key direction/color/intensity, environment weights, diffuse controls, specular controls and 1.0 mm outline width | Calibrated approximation |
| Recovered direct-diffuse and stepped-specular equations | Confirmed by static retail evidence |
| Default/Ivy rim equation, enable, cutoff, sharpness, strength, up-ramp, material mask and AO placement | Confirmed by event-791 ISA/cbuffer runtime trace |
| Rim scene-depth occlusion in Painter, exact engine light globals, six-direction probe response, camera-relative retail setup and pixel parity | Blocked/unresolved |

The fresh 2026-09-08 captures prove term isolation, asset integrity, live
Painter-light yaw and an obvious same-scene delta from Painter PBR. The earlier
unbounded dual-highlight composition was discarded. The current Ivy values
remain calibrated approximations and do not claim pixel parity.

The subsequent combo-24 output trace corrected the direct wrap input from raw
signed `N dot L` to retail's saturated `N dot L`. An exhaustive 0.001-step
sweep over `[-1,1]` at Ivy's calibrated `wrap = 0.48` produced maximum wrapped-
response delta `0`; the fixed-scene visual result is therefore unchanged. The
contract correction affects future profiles whose wrap allows a negative-
hemisphere response.

### Environment/local-probe specular reconstruction — 2026-09-08

The opaque retail output trace established a distinct environment/local-probe
specular path alongside direct specular. Painter cannot reproduce the bound
Deadlock probes, so the preview uses Painter's panorama-prefiltered
`pbrComputeSpecular` as a bounded substitute inside the Deadlimit composition.
It is exposed independently as `Environment Specular Raw` and `Environment
Specular Final`; ordinary Shaded adds only the scaled final contribution.

A Painter viewport validation pass on the deterministic Ivy scene captured both diagnostics,
then changed Environment Rotation from 145 to 180 degrees. The lobe moved over
the copper cuffs and weapon in both the isolated pass and final Shaded. Direct
NPR lighting continued to use the same Painter yaw bridge. The environment was
returned to 145 degrees and 1.0 EV after the A/B.

| Result or value | Classification |
| --- | --- |
| Separate environment/local-probe specular in opaque final RGB | Confirmed by static retail evidence |
| Shader compilation, debug modes 19/20, moving lobe and final Shaded composition | Confirmed by pipeline/runtime |
| Painter panorama substitute, strength `0.18` and roughness bias `0.12` | Calibrated approximation |
| Exact Deadlock probe cubemaps, local-probe weights/parallax and runtime roughness response | Blocked/unresolved |

Visual gate: **PASS for the reconstructed environment-specular stage**. It
removes the previous fixed fake-highlight conflict and restores the missing
view-dependent material response. Full game pixel parity remains unresolved.

## Uber-shader decomposition stage 1 — retail static gate — 2026-09-08

The read-only `Deadlimit.ShaderInspector` was run against current retail build
`25173285` and `pbr_vulkan_60_ps.vcs` SHA-256
`eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`.
The generated report and reflected source were written below `.scratch`.

Assertions passed:

```text
Static definitions: 16
Permitted static entries: 323
Dynamic definitions: 16
Combo 24: 104 dynamic states / 72 shader files
Combo 26: 32 dynamic states / 32 shader files
Combo 24 dynamic 0 and 512: same PS file and render state
Combo 26 dynamic 4 solid outline: 1,600-byte SPIR-V
Selected combo 24/26 reflected-source files written: 104
```

This is a **confirmed by pipeline/runtime** inspector result over
**confirmed static retail evidence**. It does not identify the dynamic state
selected by a live game draw and introduces no calibrated values.

## Uber-shader decomposition stage 3 — status static gate — 2026-09-08

The current retail combo-24 dynamic programs were compared from inspector
output. Dynamic 0 resolves to file 0 (76,732-byte SPIR-V, SHA-256
`03af15c800051c118177d27a99b6cbfdd39b7b491e6fc2216611030afd490229`);
dynamic 2 resolves to file 1 (88,520-byte SPIR-V, SHA-256
`ea7d0bdb7d89b07422366dcd64fbaa0ec25acd427039b0774668dab86ecc71b0`).
Their pixel render states are identical. The status program adds exactly 26
named `_Globals_` fields and two interpolants, modifies prepared material
properties, then rejoins the ordinary lighting graph. No visual approximation
or Painter status mode was introduced.

## Uber-shader decomposition stage 4 — alpha-test static gate — 2026-09-08

Retail static combo 56 reports `S_ALPHA_TEST=1`, `S_USE_NPR_LIGHTING=1` and
`S_USE_STATUS_EFFECTS_PROXY=1`, with 104 dynamic states and 72 shader files.
Its dynamic-0 program is 78,072-byte SPIR-V with SHA-256
`c09682f7e8e8d5f0af078c29e0e8169eb4ef9cdd3c218c4897332183106b16c6`.
The dynamic-0 render state matches ordinary combo 24. Static comparison found
six added `_Globals_` fields: a separate metalness texture and five cutout
controls. The recovered coverage equation uses vertex alpha, color alpha,
view-angle correction and distance boost before discard. No Painter visual or
calibrated alpha value is claimed by this gate.

## Uber-shader decomposition stage 6 — translucency static gate — 2026-09-08

Retail static combo 88 reports `S_TRANSLUCENT=1`, `S_USE_NPR_LIGHTING=1` and
`S_USE_STATUS_EFFECTS_PROXY=1`, with 16 dynamic states and 8 shader files. Its
dynamic-0 program is 83,276-byte SPIR-V with SHA-256
`30a240e4b713eb6150bd86404857ef21d249753401a04e28825970ce63163dfa`.

The inspector now expands all eight render-target blend slots instead of
serializing VRF's indexed state wrappers as empty objects. Dynamic 0 reports
premultiplied color factors `One` / `InvSrcAlpha` and alpha factors
`InvDestAlpha` / `One`; its explicit depth-stencil descriptor is absent. Static
trace confirms angle-corrected opacity, separate metalness, two depth-routed
outputs, fog/sky composition, ordinary NPR lighting, and no refraction or
depth-rim branch. No Painter approximation is claimed by this gate.

## Uber-shader decomposition stage 5 — sheen static gate — 2026-09-08

Retail static combo 152 reports `S_SHEEN=1`, `S_USE_NPR_LIGHTING=1` and
`S_USE_STATUS_EFFECTS_PROXY=1`, with 104 dynamic states and 72 shader files.
Its dynamic-0 program is 81,856-byte SPIR-V with SHA-256
`2566cb636c7a76a79d690497786ce14ce7fd7cafd5d87ecc266745fb7b0e73a0`;
its render state matches ordinary combo 24. Static comparison found five added
named fields. Dataflow confirms a packed RGB+roughness sheen texture, direct
sun/barn sheen, ordinary-lobe energy attenuation, BRDF-LUT layer 2 and a
separate final probe/environment sheen contribution. No Painter sheen
approximation or calibrated value is claimed by this gate.

## Uber-shader decomposition stage 7 — glass static gate — 2026-09-08

Retail static combo 280 reports `S_GLASS=1`, `S_USE_NPR_LIGHTING=1` and
`S_USE_STATUS_EFFECTS_PROXY=1`, with 48 dynamic states and 24 shader files. Its
dynamic-0 program is 94,040-byte SPIR-V with SHA-256
`759df1583f2412853cf16e6e15ee62b9dbea034a61ae8fd8f334fb16616cfd0c`.
Normalized blend factors match the premultiplied configuration and the explicit
depth-stencil descriptor is absent. Static trace confirms `g_tGlass.r`
transmission, retained opaque metalness, relative-depth rejection, nine
depth-validated framebuffer taps, angle tint, diffuse attenuation and fog
placement. No Painter approximation is claimed by this gate.

## Uber-shader decomposition stage 8 — advanced-translucency static gate — 2026-09-08

Retail static combo 32824 reports `S_ADVANCED_TRANSLUCENCY=1`,
`S_ALPHA_TEST=1`, `S_USE_NPR_LIGHTING=1` and
`S_USE_STATUS_EFFECTS_PROXY=1`, with 96 dynamic states and 64 shader files. Its
dynamic-0 program is 80,056-byte SPIR-V with SHA-256
`f96bcabb2e4303efa4beb6f661805d1bdc74d37e0b3db94fa399cc2bb1f78d71`.
Depth and normalized blend state match alpha-test combo 56. Static comparison
found eight added fields: a second mask, two UV selectors, two scroll speeds,
two scroll quantizers and a blend mode. The recovered multiply/add/subtract
mask result feeds the ordinary alpha-test gate and shared opaque NPR graph. No
Painter approximation is claimed by this gate.

## Ordinary opaque metal visual gate — 2026-09-09

The deterministic `subs_ivy_builder.spp` scene remained on the Ivy profile,
retail inputs, retail Default environment and `1.0 EV`. A close crop of the
already loaded retail `ivy_gearv3` binding made the copper cuff and weapon
fittings large enough for channel inspection; lighting and material inputs
were unchanged.

Painter viewport validation captured these shader-native views from the live Painter viewport:

| View | Observed result |
| --- | --- |
| `Metal Diffuse Color` | Copper regions became dark while nonmetal base color remained visible. |
| `NPR Specular Material Tint` | The same copper regions carried the recovered colored direct-specular tint. |
| `Retail Environment F0` | The same metal mask produced copper F0; dielectric regions resolved to low neutral F0. |
| `Shaded` | Suppressed metal diffuse recombined with colored reflection into readable copper. |

The loaded game reference shows Ivy's cuff hardware as bright copper with broad
colored response while nearby purple cloth and pale skin remain diffuse. The
Painter result reproduces that material separation and is visibly distinct
from the white stock-PBR highlight that caused the earlier dual-highlight
failure.

| Result or value | Classification |
| --- | --- |
| `g_tColor.a` metalness, ordinary `C * (1 - M)` diffuse, default-disabled full-roughness switch and opaque F0 equation | Confirmed by static retail evidence |
| Painter shader compilation; live modes 21–23; final Shaded recomposition; automatic Base color to Material restore | Confirmed by pipeline/runtime |
| Existing Ivy light, rim, environment strength, roughness bias and exposure controls | Calibrated approximation |
| Exact live probe selection, probe texture contents, BRDF LUT runtime values and pixel parity | Blocked/unresolved |

Visual gate: **PASS for the ordinary opaque metal branch**. This pass does not
claim completion of the overall Deadlock character look.
