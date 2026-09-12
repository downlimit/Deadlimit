# Deadlock-look investigation and continuation brief

Status: Milestone B lighting skeleton and the ordinary opaque metal branch have
passed fixed-scene Painter visual gates. The opaque combo-24 ordinary graph and
optional dynamic-2 status delta plus alpha-test, sheen, basic-translucency,
glass and advanced-translucency deltas are complete. The prioritized
pixel-family decomposition is complete.

Updated: 2026-09-09.

## Purpose

This document preserves the useful result of the first Ivy/Painter integration
and records why the latest lighting experiment did not produce the requested
Deadlock look. It is the starting brief for the next Codex session.

The target remains one artist-facing `Apply Deadlimit` operation that keeps the
current Painter project usable, applies the selected character's material
profile and displays its outline and character shading together.

## Known-good baseline

Commit `e865f1710d69ee593b0d877c950e1c22d3a459da` is the last accepted local
baseline. It contains the following confirmed pipeline behavior:

- `Apply Deadlimit` prepares a disposable preview mesh without changing the
  production FBX;
- the original Ivy Texture Sets and authored project content are retained;
- the preview-only inverted hull produces the intended outline geometry;
- outline width and color come from the shared Ivy profile;
- the exact retail Ivy textures can be decoded read-only into a disposable
  cache and assigned to their matching Valve Texture Sets;
- the missing Ivy eye vertex-color stream is restored from the extracted DMX;
- the operation completes in seconds and reports visible phases instead of
  waiting on a user-installed DCC;
- generated retail assets, preview meshes and caches remain outside Git.

This baseline proves integration and input delivery. It does not prove visual
parity with Deadlock's character renderer.

## Latest investigation

### What was attempted

The uncommitted experiment after `e865f17` attempted to extend
`Deadlock_Hero.glsl` with:

- VMAT `F_USE_NPR_LIGHTING` and transmissive-color inputs;
- a lower-hemisphere NPR bounce approximation;
- Painter environment irradiance as a substitute for the engine probe term;
- a derived texture that packs normal XY, roughness and AO to reduce custom
  sampler count;
- additional UV and NPR diagnostic output.

The experiment also changed the Painter binding layer and retail texture tool
to feed those values. These edits are investigative work only and are not an
accepted continuation baseline.

### Confirmed observations

- The four inspected retail Ivy FBX slots use UV0 within the normal 0..1 range.
  Out-of-range UVs do not explain the viewport result.
- A constant-color shader renders the meshes cleanly. Mesh visibility alone
  does not explain the cyan/blue artifacts seen during sampler experiments.
- The decoded source PNGs are valid and the previously completed Apply path can
  display the retail inputs.
- Multiple custom project samplers across Painter Texture Sets produced
  cyan/blue sentinel-like output in the tested Painter 9.1 session. Reducing
  sampler count and packing data did not yield an accepted visual result.
- The final viewport still looked substantially like Painter's ordinary PBR
  shading. No meaningful Deadlock-look delta was demonstrated.

### Rejected conclusion

Adding an approximate transmissive bounce term to Painter's PBR result is not a
sufficient reconstruction of Deadlock character lighting. The last experiment
must not be presented as a completed NPR or parity implementation.

### Missing renderer components

The current accepted shader path still lacks a verified composition of the
parts that dominate the game image:

- complete NPR direct diffuse in the final shaded path;
- direct NPR/specular response and its material controls;
- rim/highlight response and masks;
- the six-direction probe/indirect-light behavior;
- game shadowing and light accumulation;
- exposure, tone mapping and other relevant display transforms;
- confirmed runtime values for globals that static analysis could only name.

Some of these can be reconstructed and calibrated in Painter. Exact numerical
retail-frame parity remains gated by runtime evidence that earlier safe offline
capture tasks did not obtain.

## Evidence boundaries

Use these labels in future work:

- **Confirmed by our pipeline/runtime:** observed in the repository tools or a
  live Painter operation.
- **Confirmed by static retail evidence:** recovered from current retail
  resources without observing a live draw.
- **Calibrated approximation:** chosen against controlled reference images.
- **Blocked/unresolved:** required evidence is unavailable.

Do not label a calibrated value as a retail runtime value. Do not infer a visual
PASS from shader compilation, remote-API success, parameter assignment or a
saved SPP.

## Milestones to the product goal

### A. Restore a controlled baseline

Start from the behavior of `e865f17`. Preserve the current experimental diff
for reference, then remove it from the execution baseline. Establish one fixed
Ivy project, pose, camera, environment, exposure and Deadlock reference image.

Done when identical inputs reproduce identical Painter screenshots and the
existing outline/retail-texture behavior still passes.

### B. Prove the lighting skeleton visibly

Implement the final character-lighting composition with diagnostic constants
and simple colors before reconnecting all retail maps. The viewport must show a
clear stylized diffuse break, controlled specular response and rim contribution
on Ivy under the fixed setup.

Done when an actual Painter screenshot has an obvious, reproducible visual
delta from Painter PBR and every term can be isolated with a diagnostic view.

### C. Reconnect verified retail material inputs

Feed base color, normal, roughness, metallic, AO, vertex color and known NPR
masks/parameters into the proven lighting skeleton. Resolve Painter sampler
behavior with the smallest deterministic binding contract; do not compensate
for binding failures by tuning lighting.

Done when all Ivy Texture Sets render their correct inputs and no diagnostic
cyan/blue/checker output remains.

### D. Calibrate the Ivy Deadlock look

Match several controlled Deadlock reference views. Record every parameter as
static evidence, calibrated approximation or unresolved runtime data. Cover
skin, clothing, hair, eyes, wings and accessories rather than tuning one crop.

Done when side-by-side visual review accepts the Ivy result as recognizably the
same rendering style and remaining mismatches are explicitly bounded.

### E. Productize `Apply Deadlimit`

Move the accepted shader, character profile, outline and texture binding into
the single Apply workflow. Keep caches disposable and preserve existing Painter
layers and source geometry.

Done when a normal existing project and a new project both reach the accepted
combined character/outline view through one character selection and one Apply
action.

### F. Generalize beyond Ivy

Validate a materially different hero, separate shared renderer behavior from
character data and introduce a distinct shader family only where retail
evidence requires it.

Done when character selection changes data rather than relying on Ivy-specific
branches in the common shader.

## Milestone B result — 2026-09-08

The failed six-file lighting/sampler experiment was preserved as stash evidence
before implementation resumed from the accepted `e865f17` behavior. It was not
used as the new execution baseline.

`Deadlock_Hero.glsl` now composes three independently visible terms in the final
shaded path: quantized NPR direct diffuse, stepped direct specular and a
retail-mask-gated view-dependent rim. `Lighting Inputs -> Diagnostic Neutral` removes retail-map
variation while the new Direct Diffuse, Direct Specular, Rim, NPR Composite and
Painter PBR Baseline views isolate the lighting skeleton. The same shader path
then reconnects Base Color, Normal, Roughness, Metallic, AO, eye vertex color
and the four retail material bindings in `Material / Retail` mode.

Painter 9.1.0 compiled the embedded shader and exposed the new parameters on
every live Deadlimit Hero/Retail Shader Instance. A fixed-camera Computer Use
capture showed the retail-textured Ivy, restored eyes and existing inverted-hull
outline together. The diagnostic contact sheet showed each lighting term in
isolation and an obvious same-scene delta between the NPR composite and Painter
PBR baseline. No cyan/blue/checker sampler failure was present.

The exact scene is recorded in `docs/Validation.md`. The Ivy lighting values
remain **calibrated approximation** values. The failed dual-highlight result
was traced to an unbounded Painter environment term combined with Deadlimit
direct specular. Ordinary Shaded now contains one bounded, independently
diagnosable panorama substitute for the separate environment/local-probe
specular path recovered from the retail opaque output graph.

A fresh Computer Use A/B on 2026-09-08 used the same explicit camera,
environment and exposure. It showed readable retail color, a broad NPR diffuse
break, restrained stepped copper highlights, a masked rim and an obvious delta
from Painter PBR. Rotating the Painter environment from 145 to 235 degrees
changed the signed `N dot L` diagnostic, confirming the Dota-style
`uniform_main_light` yaw path. The final embedded runtime shader was
`Deadlimit_Hero_a445aa3e6dcd`; retail maps, restored eyes and the inverted-hull
outline remained present.

## Uber-shader decomposition stage 1 — 2026-09-08

`docs/UBER_SHADER_PERMUTATION_MAP.md` now records the current retail PS
topology with a reproducible read-only inspector. The VCS exposes 16 static
axes, 323 permitted static entries and 16 dynamic axes. Ivy body, wings and
gear all resolve to the ordinary opaque NPR/status family at static combo 24.
That family contains 104 permitted dynamic states backed by 72 unique SPIR-V
files.

The map separates ordinary shading, status, distance-field occlusion,
solid-outline, forward-normal and transient effect programs. Representative
alpha-test, sheen, translucent, glass and advanced-translucency families have
also been resolved and prioritized. No new visual approximation was introduced
by this stage.

## Uber-shader decomposition stage 2 — 2026-09-08

`docs/OPAQUE_OUTPUT_GRAPH.md` traces every final RGB contribution in static 24,
dynamic 0. It establishes the material preparation order, six-direction NPR
bounce, sun and barn-light loops, shadows/cookies, direct diffuse/specular,
rim, self illumination, standard environment/local-probe specular and final
composition.

The first output-graph reconstruction stage is visually gated in Painter:
`Environment Specular Raw` and `Environment Specular Final` isolate the lobe,
and a 145/180-degree environment A/B showed it move coherently in final Shaded.
Painter panorama sampling, strength `0.18` and roughness bias `0.12` are
calibrated approximations. Exact retail probe data and probe blending remain
blocked/unresolved.

The reverse trace corrected one earlier detail: retail saturates `dot(N,L)`
before the direct-diffuse wrap in both light loops. Deadlimit now follows that
operation. Ivy's calibrated `wrap = 0.48` makes the signed and saturated forms
identical after clamp for the fixed validation scene, so the prior visual PASS
is unchanged. The correction prevents divergence in future higher-wrap
profiles.

## Uber-shader decomposition stage 3 — 2026-09-08

`docs/STATUS_PROXY_DELTA.md` compares combo 24 dynamic 0 and 2. The status
program changes prepared color, normal, metalness, roughness and self
illumination through independently weighted UV/triplanar maps before rejoining
the shared opaque lighting graph. It adds no status-specific light, specular
lobe, rim lobe or final post-process term. Ordinary Ivy preview therefore keeps
dynamic-0 semantics; implementing gameplay statuses is optional and requires
effect-specific captures and inputs.

## Uber-shader decomposition stage 4 — 2026-09-08

`docs/ALPHA_TEST_DELTA.md` reduces combo 56 to six added fields and one discard
equation. The family repurposes `g_tColor.a` from opaque metalness to cutout
opacity and reads metalness from `g_tMetalness.r`. Coverage combines texture
alpha, vertex alpha, a view-angle term and a distance boost before discard;
surviving pixels rejoin the ordinary opaque lighting graph.

## Uber-shader decomposition stage 5 — 2026-09-08

`docs/SHEEN_DELTA.md` proves combo 152 is a dedicated sheen BRDF family. Its
packed texture provides RGB color and alpha roughness. Retail evaluates the
additional lobe in both direct-light loops and the probe/environment path,
while attenuating ordinary diffuse/specular for energy compensation. The
direct sheen lobe stays outside the NPR specular quantizer.

## Uber-shader decomposition stage 6 — 2026-09-08

`docs/TRANSLUCENT_DELTA.md` identifies combo 88 as premultiplied basic
translucency. `g_tColor.a` is angle-corrected opacity, metalness uses a separate
texture, and the lit/fogged color is routed between two outputs by scene depth.
The family keeps ordinary NPR lighting, omits depth-occluded rim and contains no
scene-color refraction branch.

## Uber-shader decomposition stage 7 — 2026-09-08

`docs/GLASS_DELTA.md` reduces combo 280 to a screen-space transmission family.
`g_tGlass.r` attenuates ordinary diffuse and weights an angle-tinted framebuffer
copy. Retail validates scene depth, gathers a center plus eight blur taps, then
adds the transmitted background to the ordinary NPR-lit surface before fog.
This path depends on render-pass inputs unavailable to a Painter surface
shader.

## Uber-shader decomposition stage 8 — 2026-09-08

`docs/ADVANCED_TRANSLUCENCY_DELTA.md` closes the prioritized family map. Combo
32824 is an animated dual-mask cutout: independently scrolled `Color.A` and
`AltTranslucency.R` combine by multiply, add or subtract, then feed the ordinary
alpha-test gate. Surviving pixels use the shared opaque NPR graph. This selected
family contains no framebuffer refraction or basic-translucent fog routing.

## Ordinary opaque metal branch — 2026-09-09

The shared hero shader now follows the recovered ordinary opaque material split:
`g_tColor.rgb` is decoded from sRGB, `g_tColor.a` remains linear metalness,
diffuse is `C * (1 - M)`, and the environment/probe F0 is
`mix(0.04, C, M) * saturate(max(C) * 25)`. NPR direct specular retains its
separate normalized material tint and mixes that tint toward `C` by metalness.

Painter exposes the split as `Metal Diffuse Color`,
`NPR Specular Material Tint` and `Retail Environment F0`. Computer Use captured
all three on the same loaded Ivy gear binding and the final Shaded composition.
The copper cuff and weapon fittings were dark in the diffuse-only view, copper
in both specular-color views and recombined as controlled colored reflections
in Shaded. Nonmetal cloth, skin and painted weapon parts stayed outside the
metal response. This is a visual PASS for the metal branch.

Deadlimit View selection now repeats its Material-view restore on Painter's
next event-loop turns. A fresh restart confirmed that selecting
`Metal Diffuse Color` while the viewport was in `Base color` changed the
viewport selector to `Material` without manual intervention.

## Brief for the next Codex session

Resume visual reconstruction from the completed prioritized static map:

1. keep the fixed Ivy validation scene unchanged;
2. keep the passed metal diffuse/direct-specular/environment-F0 split intact;
3. calibrate the remaining ordinary opaque cloth and painted-surface response
   against controlled Deadlock captures;
4. add optional alpha-test and sheen Painter families only when selected Ivy
   materials or a captured reference requires them;
5. keep advanced translucency, glass and status effects outside ordinary Ivy
   calibration unless a matching material/reference activates them;
6. resume Milestone D visual calibration from the recovered graph and avoid
   further unbounded lighting terms;
7. keep retail assets, reflected shader source, SPP/FBX/DMX, decoded textures,
   `.scratch` and `.worktrees` outside commits.

The next visual goal is recognisable Deadlock-style calibration across Ivy's
ordinary opaque clothing and painted accessories. SSS, eye and hair branches
are intentionally deferred; wing handling is limited to the required culling
behavior until evidence demands a separate path. Milestone B and the passed
metal branch are the stable base underneath that work.

## Reduced CSDK runtime correction — 2026-09-09

A RenderDoc capture of the visible Ivy Asset Browser draw established the
Reduced CSDK runtime values that the preceding Painter calibration lacked:
two diffuse steps, step sharpness `0.9`, diffuse PBR blend `0.25`, direct wrap
`0.8`, normalization `0.625`, diffuse range `[-1, 1]`, DfAO range `[0.4, 1]`
and NPR light weights `[0.42, 0.42, 0.126]`. The prior Ivy values `0.18`,
`0.72`, `0.48` and `0.85` are retired.

The profile records these as **confirmed by pipeline/runtime** with the source
`reduced-csdk-asset-browser`. They are not current retail runtime values;
current retail and Reduced CSDK shader identities differ. Painter's probe
colors, neutral screen DfAO substitute, and the numeric direct-specular/rim
controls retain their existing calibrated/unresolved classifications. The
same draw confirms `g_bNPRDirectSpecular = 0` and `g_bNPRRimLighting = 0`;
ordinary Default Shaded must therefore omit both calibrated lobes. The
next visual gate must use the corrected profile before further cloth or paint
calibration.

## Reduced CSDK exposure-control reconstruction — 2026-09-09

The corrected profile was applied in a fresh Painter process and the real
viewport was captured through Computer Use. Direct diffuse, direct specular,
rim and the NPR lighting composite remain independently selectable. The
recovered bounce exposure control removed the preceding near-black midtone
failure and produced a stable, visibly stepped Shaded result on the same Ivy
camera. The proof frames are local temporary evidence under
`.scratch/visual-proof/runtime-exposure` and stay outside commits.

This is a visual PASS for the exposure-control slice. Full game parity remains
open: the Source 2 draw uses bound directional/local-probe resources that are
only partly reconstructed in Painter.

## Runtime environment and duplicate-lobe correction — 2026-09-09

The captured pixel draw binds `envmaparray.vtex` at PS texture slot 12. Its
runtime descriptor is a 256x256 cubemap array with seven mips and 2040 faces
(340 cubes). Cube zero was exported from the capture and converted to a
lat-long HDR solely as local proof evidence. In the fixed Painter scene,
rotation 145 produces the same broad industrial-panel reflection structure on
Ivy's weapon and copper pieces; Direct Diffuse, Direct Specular, Rim, NPR
Composite and Painter PBR were captured independently under
`.scratch/visual-proof/runtime-probe`.

Those frames exposed a composition error rather than a tuning problem. The
Ivy profile still forced its calibrated direct-specular and rim branches on
even though the captured Default draw disables both runtime flags. This added
a second view/light-vector highlight over the cubemap response. The profile
now follows the captured flags: environment specular remains active, while
direct specular and NPR rim contribute zero in ordinary Default Shaded. Their
diagnostic implementations remain available for future runtime states where
the corresponding flags are enabled. The captured HDR remains temporary and
must not be committed; a reproducible local extraction path is still required
before environment setup can be claimed complete for arbitrary projects.

## Six-direction bounce reconstruction — 2026-09-09, pending visual gate

The local implementation now evaluates the six signed, squared-direction
probe coefficients exported from Reduced CSDK capture event 791. Exposure
control applies to each coefficient before ordinary and NPR probe evaluation;
captured cb1[18].x is 1. The surface-to-camera vector contributes positively
to the NPR direction. The side probe uses the camera vector's horizontal
projection (instructions 934-947), independently of the weighted NPR direction.
DfAO remapping follows instructions 906-909: lerp(minimum, 1,
saturate(screenDfAO / scale)). Transmission uses 1 minus the quantized coordinate.

The six samples are capture-derived RGBE-export measurements, with export
precision limits. Freezing a single origin sample over the character is a
calibrated spatial approximation. This does not reconstruct the full volume,
establish current-retail parity, or justify a final visual PASS. The captured
sun radiance is 1.6 per RGB channel; the secondary preview fill is disabled
for this controlled slice, without claiming all runtime light lists are empty.

Visual follow-up: Computer Use verified neutral NPR Composite, material Shaded,
and isolated raw environment specular. Frames 12 and 13 under the local
runtime-probe proof directory show restored diffuse brightness and copper
response after retiring the old environment strength 0.18 and roughness bias
0.12 (neutral defaults are 1 and 0). Painter was left in Material / Retail,
Shaded. Existing camera, environment and outline were preserved. Static profile,
Apply and retail-texture contracts and git diff --check passed. No commit was
made: this is visible progress on the reconstruction slice, not full parity.
Next unresolved boundary is Source 2 environment BRDF lookup/multiple scattering
(captured instructions 834-862) and final display transform. Do not resume
arbitrary lobe calibration or repeat environment-rotation tests without new evidence.

The earlier `light_test_default` static cubemap recipe is removed from Apply.
Its decoded image is nearly uniform and does not match the cubemap array bound
to the captured Ivy draw. Apply now preserves the project's selected
environment until the runtime-array extraction path is reproducible.

## Environment BRDF evidence — 2026-09-12

See `ENVIRONMENT_BRDF_RUNTIME.md`: captured normalization and multiple-scattering
flags are both enabled. The real BRDF LUT and all seven mips of selected cube
zero now have a repeatable offline export tool. Five numerical tests and 25
captured-LUT sample checks pass. This advances decomposition; the installed
Painter shader stays at the preceding visually inspected version. Next replace
the environment integration as a complete path using those inputs. No additional
artistic coefficients, viewport PASS, or commit were introduced in this step.

### 2026-09-13 integration status

The optional captured-environment shader branch and the user-facing bundle
loader are implemented and installed. Native mip packing, LUT quantization,
GLSL 330 environment-block compilation, and binding persistence across Apply
were checked. See `ENVIRONMENT_BRDF_RUNTIME.md` for explicit approximations.
Visual gate is pending because Computer Use returned Dota rather than the
selected Painter viewport. Captured mode is disabled in the open project;
Material / Retail and Shaded are restored, and the experimental SPP is not saved.
Do not claim visual progress from this iteration or commit before the gate.

### 2026-09-13 subsequent real viewport check

Painter became capturable after user moved it to another monitor and the
window was activated. Neutral Composite rendered, but isolated captured specular
was black and metallic regions failed. Debug 24 proved LUT sampling was nonzero;
debug 25 compared a known nonzero atlas texel at top-down V and flipped V.
Only flipped V produced the expected grey. Atlas sampling now flips V from image
row coordinates to Painter texture coordinates; the LUT uses the same image
coordinate convention (LUT orientation still needs a numeric GPU readback).

The isolated neutral specular now shows reflections. Material Shaded was captured
in `.scratch/visual-proof/runtime-probe/14-captured-brdf-v-corrected.jpg`: weapon
and copper response returned, eyes and outline remain visible. Captured mode
is enabled in the open project, Material / Retail and Shaded selected. Computer
Use was reset. This is a visual pass for atlas connectivity only; full CSDK
parity remains unproved and the documented BRDF approximations still apply.
No commit. Project was not saved over the existing SPP during this check.

### 2026-09-13 pixel-trace reference and dominant reflection

Added reproducible offline pixel-trace export and independent CPU comparison for
event 791, pixels (660,290) and (580,200). Dominant reflection, box projection,
post-projection roughness blend, LUT coordinates, radiance normalization and
coupled multiple scattering match the RenderDoc traces within 8.64e-8 absolute
error. Captured DDS bilinear LUT samples, rounded to half, match both traced
samples exactly. See `ENVIRONMENT_BRDF_RUNTIME.md` for scope and commands.

Transferred verified dominant reflection into the captured GLSL path. No fitted
values added. The latest source change is not installed; the open Painter SPP
and prior visual status are unchanged. No commit or full visual PASS.

Next technical boundary: establish model-to-captured-world coordinates before
transferring box projection; confirm probe selection/weights, specular occlusion
and final display transform. Preserve distinctions between Reduced CSDK evidence
and unconfirmed current retail equivalence. Do not label the decomposition complete.

### User-authorized checkpoint — 2026-09-13

The user explicitly requested committing the current progress before continuing.
This checkpoint preserves source, tests and documentation despite the pending
full visual gate. It is not a release or an accepted Deadlock viewport match.
Local captures, decoded assets, SPP, scratch data and user worktrees are excluded.
