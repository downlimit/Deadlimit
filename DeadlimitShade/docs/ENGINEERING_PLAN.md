# Deadlimit Shade — Engineering Plan

Date: 2026-09-13
Scope: evidence-driven implementation and validation plan.

## Product target

Reproduce the controlled Deadlock character shading pipeline in Substance 3D Painter as closely as Painter permits, with every implemented contribution traceable to runtime/static evidence or explicitly labeled approximation.

The acceptance gate is a controlled side-by-side comparison against a fixed Deadlock/Reduced-CSDK reference: same character, pose, camera, environment/orientation and exposure. A compile, passing unit test or successful Painter API call is supporting evidence only.

## Evidence classes

Use the project labels consistently:

- **confirmed by pipeline/runtime** — observed in the selected captured draw, bound state, constant buffer, sampler or instruction trace;
- **confirmed by static retail evidence** — recovered from current retail materials/shaders/resources without proving the selected runtime value;
- **calibrated approximation** — chosen for Painter preview or inferred by calibration;
- **blocked/unresolved** — required input/transformation is unknown or Painter does not expose it.

Never upgrade an evidence class from a report or implementation note alone. Require reproducible trace, data, or test evidence.

## Working protocol

Every iteration follows:

1. one technical boundary;
2. one explicit hypothesis/question;
3. minimum data needed to resolve it;
4. perform only the bounded extraction, edit, or test task needed to answer that question;
5. inspect returned evidence and diff;
6. classify the result;
7. decide the next boundary.

Do not use open-ended tasks such as "continue the shader", "make it match" or "investigate everything".

Implementation must not tune visual coefficients, rotate the environment, invent substitute lights, or reinterpret flags without evidence. Implement only after the required producer/input chain is identified. Local Valve assets, RenderDoc captures, decoded textures, SPP/FBX/DMX and `.scratch` data remain out of git.

## Protected baseline

Current checkpoint: `76aebec` (`Checkpoint direct specular reconstruction and new-chat handoff`), based on `e8f7f5d`.

Must survive all work:

- Apply Deadlimit;
- retail texture binding;
- Ivy eye/vertex-color behavior;
- inverted-hull outline;
- visible Deadlimit Shade panel;
- existing environment-BRDF trace/reference tests.

## Phase 1 — Finish ordinary opaque direct lighting

Current active boundary.

### 1A. Expand ordinary direct-specular trace coverage

Find at minimum:

- one clearly metallic covered pixel;
- one clearly dielectric covered pixel;
- both must execute the ordinary direct-specular branch.

For each pixel record and verify through the executed instruction chain:

- base color / metallic-derived material F0;
- roughness and all roughness transforms;
- N, V, L and half-vector terms;
- ordinary BRDF terms;
- Fresnel dependencies;
- light radiance/color/intensity producer;
- visibility/shadow producer;
- final direct-specular value entering the opaque composition (`r27` boundary in the selected trace).

Acceptance: CPU/reference reconstruction matches the executed trace within numerical tolerance for both material classes. One pixel is insufficient.

### 1B. Resolve direct-light ownership

Trace the selected draw's actual direct-light loop/source. Determine:

- how many contributing direct lights exist in the controlled reference;
- which one corresponds to the currently reconstructed specular path;
- radiance and visibility inputs;
- whether current Painter `keyLight` / `fillLight` abstractions correspond to captured sources.

Acceptance: Painter Shaded must not contain an invented extra light. Any preview-only light stays explicitly `calibrated approximation` and outside claims of runtime parity.

### 1C. Clean flag semantics

After 1A/1B are stable, rename ambiguous internal fields such as `directSpecularEnabled` if necessary so the name reflects the actual runtime semantic (`NPR direct specular enabled` versus ordinary direct specular fallback). Regenerate profile-derived code rather than hand-editing generated sections.

## Phase 2 — Final display transform

Trace the pass chain after the selected opaque pixel shader and identify the transformations responsible for the final controlled reference image:

- exposure;
- tone mapping;
- color-space conversion;
- any relevant grading/post-processing between linear opaque output and displayed frame.

Acceptance: documented equations/resources/order sufficient to reproduce or explicitly bound the Painter approximation. Do this before broad visual tuning.

## Phase 3 — Probe/world coordinate mapping

Recover the mapping needed to use captured box projection correctly:

- CSDK world-position reconstruction;
- probe affine transform;
- Painter scene normalization;
- original model scale/translation or a reproducible profile transform derived from mesh metadata.

Do not wire Painter `inputs.position` directly into captured bounds without evidence.

Acceptance: world/probe coordinates for sampled points reproduce captured transform values within tolerance, or the feature remains explicitly `blocked/unresolved`.

## Phase 4 — Spatial probe selection and blending

Recover:

- selected probe records;
- containment rules;
- blend weights;
- fallback behavior;
- which captured resources are actually required for the Ivy controlled reference.

Acceptance: more than the current frozen cube-zero / frozen six-direction sample can be reproduced deterministically without committing captured assets.

## Phase 5 — Missing screen-space inputs

Resolve individually:

- screen specular visibility;
- screen DfAO;
- shadow inputs not already resolved;
- rim depth occlusion;
- other clustered/screen resources that enter the common opaque path.

For each input choose exactly one status:

1. reproducible from Painter data;
2. valid neutral value for the controlled reference, proven by trace;
3. calibrated approximation;
4. blocked/unresolved.

Never substitute Painter AO for unrelated Deadlock screen visibility merely because it is available.

## Phase 6 — Controlled visual comparison

Freeze and record:

- Ivy pose;
- camera transform/FOV/framing;
- environment selection and orientation;
- exposure/display settings;
- material/texture state.

Capture controlled Deadlock/CSDK and Painter frames. Use contribution diagnostics to attribute residual error to:

- direct diffuse;
- ordinary/NPR direct specular;
- bounce;
- environment specular;
- rim;
- outline;
- display transform.

Acceptance: remaining mismatches are measured/attributed. Two iterations with the same image and no measurable progress terminate that approach.

## Phase 7 — Reusable artist workflow

Only after the common opaque path passes the controlled comparison:

- package required profile/environment resources;
- make character profile selection deterministic;
- ensure `Preview as Deadlock` works in a newly opened supported textured project;
- remove debug-only requirements from ordinary artist use;
- validate install/update behavior without modifying retail/CSDK/source assets.

SSS, special eye and hair paths remain deferred until the common opaque path is accepted.

## Immediate next research task

Do **not** change visual coefficients or shader structure yet.

Produce trace evidence for at least one metallic and one dielectric pixel executing the ordinary direct-specular branch. For each, export enough data to verify material F0, roughness, BRDF terms, light radiance, visibility and the final contribution entering `r27`. Extend the existing reference test rather than creating an unrelated synthetic test. Return:

- exact capture/event/pixel identifiers;
- instruction ranges used;
- extracted inputs and traced outputs;
- maximum numerical error of the reconstruction;
- files changed, if any;
- no visual-tuning changes.

If suitable pixels cannot be found, stop at that concrete boundary and report the search criterion and evidence instead of inventing substitutes.
