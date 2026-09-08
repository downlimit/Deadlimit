# Deadlock-look investigation and continuation brief

Status: Milestone B lighting skeleton passed the fixed-scene Painter visual
gate. Uber-shader decomposition stage 1 (permutation map) is complete; the
combo-24/dynamic-0 output graph is next.

Updated: 2026-09-08.

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
was traced to simultaneous Painter environment specular and Deadlimit direct
specular. Painter IBL specular is now excluded from ordinary Shaded.

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

## Brief for the next Codex session

Complete the static decomposition before further visual calibration:

1. keep the fixed Ivy validation scene unchanged;
2. build the complete combo-24/dynamic-0 output graph from material decode to
   final pixel output;
3. label every texture, uniform-buffer field and light/probe/shadow dependency;
4. compare dynamic 2 only after the ordinary graph is bounded, then proceed to
   alpha-test, sheen, translucent, glass and advanced translucency;
5. resume Milestone D visual calibration from the recovered graph rather than
   adding further approximate lighting terms;
6. keep retail assets, reflected shader source, SPP/FBX/DMX, decoded textures,
   `.scratch` and `.worktrees`
   outside commits.

The next visual goal is recognisable Deadlock-style calibration across Ivy's
skin, clothing, eyes, wings and accessories. Milestone B is the stable lighting
skeleton underneath that work.
