# Deadlimit Shade — new-chat continuation handoff

Date: 2026-09-13

Repository: `D:\Documents\ChatGPT\Deadlimit Project`

Component: `DeadlimitShade`
Branch: `codex/deadlock-shader-profiles`

This document is the entry point for a fresh Codex chat. Read it before changing
the shader. It distinguishes confirmed captured execution from approximations
and records the failed approaches that should not be repeated.

## Start the new chat with this contract

Continue Deadlimit Shade from this handoff and the current branch. The product
goal is a Substance 3D Painter character viewport that reproduces the Deadlock
character shading pipeline as closely and technically as Painter permits. Treat
the selected Deadlock/CSDK render as an execution reference. Reconstruct the
shader dataflow and inputs; do not tune arbitrary coefficients by eye.

Work continuously until a meaningful implementation or a concrete technical
boundary is reached. A side question from the user is additional input: answer
briefly and continue the active task. `continue` means perform implementation,
tests and the relevant visual check in the same turn. Do not end a turn after a
plan, a status report, launching an application, compilation alone, or a single
intermediate diagnostic.

Use Computer Use only for viewport operations that require it. Launch Painter,
install files, call its remote API, and run offline RenderDoc scripts without
Computer Use where possible. When Computer Use is needed, complete the intended
UI sequence, capture the evidence, then reset it. User mouse movement is not a
reason to abandon the task. Do not leave unused windows open.

Preserve retail files, CSDK, source FBX, user `.worktrees`, SPP and temporary
data. Never commit Valve assets, captures, decoded textures, `.scratch`, FBX,
DMX, SPP, or generated caches. Keep unrelated `internal/DeadlimitUpdater.ps1`
out of commits.

## Required reading

Read these files in order:

1. `DeadlimitShade/docs/NEW_CHAT_HANDOFF_2026-09-13.md`
2. `DeadlimitShade/docs/DEADLOCK_LOOK_HANDOFF.md`
3. `DeadlimitShade/docs/ENVIRONMENT_BRDF_RUNTIME.md`
4. `DeadlimitShade/docs/OPAQUE_OUTPUT_GRAPH.md`
5. `DeadlimitShade/docs/MaterialModel.md`
6. `DeadlimitShade/docs/ROADMAP.md`
7. `DeadlimitShade/docs/Validation.md`

Also inspect current `git status`, the latest commits, and the diff from the
checkpoint named near the end of this document. Do not assume the working tree
is clean. Existing unrelated files belong to the user.

## Product goal and acceptance condition

The final artist workflow should be understandable and repeatable in another
ordinary textured project:

1. Open a supported Painter project or create one from the eventual Deadlimit
   template.
2. Choose the character profile in the visible **DEADLIMIT SHADE** panel.
3. Click **Preview as Deadlock**.
4. See the character with the appropriate Deadlock material inputs, lighting,
   environment, outline and display response without manually assigning every
   shader instance or guessing hidden settings.

The visual goal is the controlled Deadlock character preview, including its
strongly stylized, nearly toon-like NPR response. Success means the same input
mesh/materials produce a viewport image that is technically traceable to the
reference pipeline and visually matches the controlled engine reference. A
successful GLSL compile, Painter API call, saved SPP, or isolated diagnostic is
only supporting evidence.

Before declaring the milestone complete, obtain real screenshots of both the
Painter viewport and the controlled Deadlock/CSDK reference at fixed character,
pose, camera, environment and exposure. Compare the images. A qualitative
statement such as “closer” is insufficient for final acceptance. Record exact
remaining mismatches or demonstrate that the chosen tolerance is met.

## Evidence labels

Use these labels exactly:

- **confirmed by pipeline/runtime** — observed in the selected captured draw,
  bound state, constant buffer, sampler, or instruction trace.
- **confirmed by static retail evidence** — recovered from current retail
  materials/shaders/resources without proving a particular runtime value.
- **calibrated approximation** — chosen for Painter preview or inferred by
  visual calibration; never call it a retail runtime value.
- **blocked/unresolved** — required input or transformation is still unknown.

The principal execution capture is a **Reduced CSDK** capture. Its shader and
current retail shader have different identities. Never present its runtime
constants as confirmed current-retail runtime values.

## Repository state and protected baseline

Important history:

- Original handoff mentioned commit `5fec083`.
- Original confirmed working baseline was `e865f17`.
- The branch later accumulated the shader-family decomposition and opaque-path
  reconstruction.
- Checkpoint `e8f7f5d` preserves the captured environment implementation and
  pixel-trace reference tests. It was explicitly authorized as a progress
  checkpoint despite the incomplete full visual gate.

The following behaviors must survive all changes:

- **Apply Deadlimit** works.
- Retail texture bindings work.
- Ivy eyes remain restored through vertex-color/material handling.
- The outline remains a correct inverted hull rather than a screen-space fake.
- The user can see and understand the Deadlimit Shade panel.

## What was decomposed before this handoff

The repository documents the prioritized shader families and selected Ivy
opaque path:

- 16 static axes, 323 permitted static entries and 16 dynamic axes are mapped.
- Opaque combo 24 / dynamic 0 contribution topology is traced.
- Status proxy, alpha-test, sheen, translucent, screen-space glass and advanced
  translucency deltas are separately documented.
- The active implementation target remains the common opaque character path.
- SSS, special eye and hair paths were explicitly deferred by the user.
- Wings require the appropriate two-sided/backface behavior; do not invent a
  separate wing-lighting model without evidence.
- Metals are part of the uber shader and require the correct shared BRDF,
  Fresnel, environment response and energy composition.

## Implemented and confirmed

### Painter/product integration

- A visible dock exposes character, lighting input, debug view, Apply and local
  captured-environment loading.
- Apply assigns Hero/Retail shader instances, keeps the outline instance, loads
  retail maps and preserves the captured-environment bindings on active instances.
- Old unused Painter shader instances can remain after shader hash changes; the
  binder filters them by supported parameters.
- Source meshes and retail files remain unchanged.

### Retail material inputs

- Retail base color and metalness use the expected packed channels.
- Retail normal and roughness use their packed texture.
- Ambient occlusion, rim mask and NPR transmissive inputs are available.
- Vertex colors are preserved for the eye/material slots.
- Material AO remains part of the diffuse/bounce response. It must not be
  multiplied into the captured environment-specular visibility boundary.

### NPR diffuse and bounce

- Direct diffuse has an independently visible diagnostic path.
- Six signed squared-direction probe coefficients are evaluated.
- Exposure-control topology and the selected Reduced CSDK constants are recorded.
- The full spatial probe volume is not reconstructed; one frozen spatial sample
  remains an approximation.

### Captured environment BRDF

The optional `dl_captured_environment` path uses:

- the selected cube's seven captured mip levels packed into a Painter atlas;
- the captured 64x64 BRDF lookup array slice;
- captured LUT coordinates;
- radiance normalization;
- multiple-scattering compensation coupled between specular and diffuse;
- roughness-dependent dominant reflection direction;
- corrected top-down/bottom-up image V convention.

All 42 packed mip faces retain decoded source pixels. The LUT transfer has a
documented UNORM16 precision limit. The HDR atlas has RGBE precision limits.
Native cubemap cross-face filtering is currently replaced by face-edge clamp.

### Reproducible execution traces

`tools/Export-DeadlockPixelTrace.ps1` and
`tools/export_deadlock_pixel_trace.py` reproduce offline RenderDoc pixel traces
without attaching to or launching a game. RenderDoc interprets arithmetic and
uses replay for resource operations; these are stronger than synthetic tests
and are not native GPU register dumps.

For event 791, trace pixels `(660,290)` and `(580,200)` validate:

| Boundary | Recorded maximum absolute error |
| --- | ---: |
| Dominant reflection | 7.36e-8 |
| Box projection equation | 5.80e-8 |
| Post-projection roughness blend | 2.61e-8 |
| LUT UV mapping | 5.02e-9 |
| Radiance normalization | 8.64e-8 |
| Multiple-scattering specular | 1.72e-9 |
| Multiple-scattering diffuse | 4.10e-8 |
| Linear opaque composition | 1.01e-7 |

At both tested inputs, CPU bilinear LUT sampling rounded to half matches traced
RG exactly. This does not prove every hardware-filtering coordinate.

### Corrections made after checkpoint `e8f7f5d`

1. Captured environment specular no longer uses Painter's material
   AO/metalness/roughness specular-occlusion substitute. CSDK uses a separate
   screen visibility value at ISA 1732–1734. Painter lacks that buffer, so this
   input currently uses neutral one and remains **blocked/unresolved**.
2. The full linear opaque summation boundary has a CPU trace check.
3. A major flag interpretation was corrected:
   `g_bNPRDirectSpecularEnabled=0` selects ordinary direct specular. It does not
   zero all direct specular. The recovered ordinary GGX-like path at trace pixel
   `(660,290)` matches ISA 1163–1188 with error `3.46e-10`.

## Latest real viewport evidence

Local-only evidence is under:

`DeadlimitShade/.scratch/visual-proof/20260913/`

- `01-shaded.png` — captured Shaded before the ordinary direct-specular fix.
- `02-neutral-env.png` — isolated captured environment specular.
- `03-ordinary-direct-restored.png` — Shaded after restoring ordinary direct
  specular and waiting for retail map binding.
- `04-direct-specular-only.png` — isolated direct-specular diagnostic; the
  response is visibly present on the copper cuffs and wrapped arm pieces.

These images are real Painter Computer Use captures and remain uncommitted.
They prove shader execution and visible contribution separation. The current
viewport still does not match the game closely enough for final acceptance.

Painter state at handoff may be ephemeral. The last observed project was:

`C:\WorkProjects\Deadlock\IvyBuilder\4texture\subs_ivy_builder.spp`

The project was opened with remote scripting enabled, Apply was run, the local
captured environment was bound to five current Hero/Retail instances, and the
view was returned to Material / Retail → Shaded. Do not save experimental state
over the user's SPP unless the user explicitly requests it.

## What is still missing, in priority order

### 1. Finish the common opaque lighting path

This remains the current stage. Do not jump to SSS, special eyes or hair.

- Verify the new ordinary direct-specular path on more covered pixels/materials.
  One pixel confirms the executed formula; one other trace did not execute it.
- Recover the ordinary direct-light loop ownership, radiance and visibility
  across the full selected draw. Ensure key/fill preview abstractions do not add
  light sources absent from the controlled reference.
- Confirm exact specular compensation/Fresnel dependencies for dielectric and
  metal pixels across multiple roughness values.
- Reconcile the diagnostic requirement for controllable specular with the
  runtime fact that NPR specular is disabled in this particular Default draw.
  The diagnostic may expose the NPR branch, while ordinary Shaded must follow
  the captured state.

### 2. Resolve probe position and cubemap projection

- CSDK reconstructs world position as `v2 + cb1[19].xyz` at ISA 104.
- ISA 374–376 transforms world position with the selected probe's affine rows.
- ISA 395–404 performs box projection.
- ISA 422–423 blends the projected direction toward the unprojected dominant
  direction by roughness.
- Painter normalizes scene size. Its `inputs.position` cannot be inserted into
  captured CSDK bounds without recovering original scale and translation.

Recover the model-to-captured-world mapping or create an explicitly supported
profile transform derived from mesh metadata. Do not invent offsets by eye.
Until this is solved, box projection must remain unwired and clearly labeled.

### 3. Reconstruct spatial probe selection and weights

The current proof freezes cube zero and one six-direction sample. The captured
resource contains many probes. Determine the selected probe records, volume
containment, blend weights and fallback behavior for the Ivy draw. Export only
the resources needed for a reproducible proof. Keep captured/decoded assets out
of git.

### 4. Resolve missing screen-space inputs

Painter does not expose Deadlock's screen DfAO, screen specular visibility,
shadow buffers, rim depth occlusion or every clustered-light resource. For each
missing input:

- trace where it enters the captured program;
- determine whether neutral one/zero is valid for the controlled reference;
- emulate it only when Painter supplies sufficient data;
- label neutral fallbacks **blocked/unresolved**, not runtime values.

### 5. Recover final display transform

The selected pixel shader writes linear output before later display passes.
Trace the later pass chain that determines exposure, tone mapping, color space
and any post-processing visible in Lighting Preview. Without this, a correct
material shader can still look wrong in Painter. Do this before broad visual
claims or coefficient tuning.

### 6. Controlled comparison and reusable workflow

- Freeze Ivy pose, camera, environment orientation and exposure.
- Capture the engine reference and Painter result at matching framing.
- Use diagnostic views to attribute mismatches to direct diffuse, ordinary/NPR
  specular, bounce, environment specular, rim, outline or display transform.
- When two iterations produce the same image without measurable progress, stop
  that approach and name the boundary.
- Once the common opaque path passes, make the environment/profile resources
  installable and usable in a newly opened textured project through the panel.

## Known wrong turns to avoid

- Do not add a hard-coded highlight vector over Painter HDRI. It creates two
  incompatible specular models.
- Do not rotate HDRI repeatedly as a substitute for shader reconstruction.
- Do not interpret a disabled NPR feature flag as removal of the corresponding
  ordinary lighting path.
- Do not feed Painter AO into environment specular merely because Painter's
  stock PBR helper does so.
- Do not tune another strength/bias after two visually unchanged attempts.
- Do not claim progress from GLSL compilation, an API call or opening Painter.
- Do not stop because the user moved the mouse, asked a side question, or said
  `continue`.
- Do not keep Computer Use active after the required viewport sequence ends.
- Do not create multiple unmanaged Painter/RenderDoc windows. Use short-lived
  owned helpers and close only processes/windows whose identity was verified.

## Verification commands

Run from `DeadlimitShade`:

```powershell
python tests/environment-reference-smoke.py
./tests/painter-apply-contract-smoke.ps1
./tests/profile-contract-smoke.ps1
./tests/retail-texture-contract-smoke.ps1
git diff --check
```

With local capture evidence available:

```powershell
python tests/captured-pixel-reference-smoke.py --lut `
  .scratch/runtime-environment-cube0-20260912/brdf-array.dds `
  .scratch/pixel-reference-20260913/trace-660-290.json `
  .scratch/pixel-reference-20260913/trace-580-200.json
```

The isolated GLSL harness is local scratch evidence and does not prove a full
Painter compile. Painter's own log and real viewport must also be checked after
installation.

## Working efficiently without exhausting the usage window

### Official OpenAI guidance

OpenAI's current Astra guide says that Astra can stop when it expects user input
and recommends an explicit initiative/follow-through instruction. It also says
that unclear or conflicting `AGENTS.md`/skill guidance can cause early pauses,
and recommends auditing those files. For testing, OpenAI recommends proportional
tests and repeating/broadening them only after new changes, failures or unresolved
concerns. The guide recommends starting Astra at low reasoning effort when a
lower setting is appropriate and increasing it only when necessary.

Official sources:

- https://developers.openai.com/api/docs/guides/latest-model
- https://developers.openai.com/api/docs/models/gpt-6-astra

The model catalog positions Astra for the hardest end-to-end work, Terra for a
balance of intelligence and cost, and Luna for cost-sensitive high-volume work.
Astra supports `low`, `medium`, `high`, `xhigh` and `max` reasoning effort. The
official documentation does not state that a Codex desktop five-hour window
equals five hours of wall-clock model activity. Do not infer per-model runtime
from that window percentage.

### Recommendations specific to this continuation

These are project-workflow recommendations derived from this run:

1. Start the new chat with this file and the current diff. Avoid replaying the
   entire old conversation unless a precise missing fact requires it.
2. Use Astra at **low** reasoning for one bounded hard task, such as tracing the
   display pass or resolving probe transforms. Use Terra/Sol for implementation,
   documentation and routine test repair. Reserve higher Astra effort for a
   demonstrated failure at low/medium.
3. State one active technical boundary per turn. Continue through inspection,
   edit, proportional checks and visual proof. End only at meaningful progress,
   completion or a concrete blocker.
4. Prefer offline RenderDoc replay and scripts to Computer Use. Use Computer Use
   for the final viewport observation and required interactive controls.
5. Do not re-read large disassemblies wholesale. Query exact instruction ranges
   and persist recovered formulas in tests/docs immediately.
6. Do not repeat passing tests without a code/input change. Run the focused test
   first, then the required contract suite once before committing.
7. Commit evidence-backed checkpoints when explicitly requested. Mark them as
   checkpoints and preserve the pending visual gate.
8. Keep status updates short. Tool work should dominate the turn.

## Immediate next action

Continue the ordinary opaque direct-light reconstruction from the newly restored
direct-specular branch. Add trace coverage for at least one clearly metallic and
one dielectric pixel that execute this branch, verify material F0, roughness,
light radiance and visibility through final `r27`, and compare isolated Direct
Specular plus Shaded against the controlled engine reference. Then proceed to
the final display-transform trace unless the new pixel evidence exposes an
earlier incorrect producer.

Do not spend the next iteration rotating the environment or adding visual
coefficients.
