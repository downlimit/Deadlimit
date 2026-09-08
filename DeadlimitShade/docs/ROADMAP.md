# Deadlimit Shade — Implementation Roadmap

Status: implementation started.

Updated: 2026-09-08.

The current Deadlock-look investigation status, rejected lighting experiment,
milestone gates and next-session brief are recorded in
[`DEADLOCK_LOOK_HANDOFF.md`](DEADLOCK_LOOK_HANDOFF.md).

The full character uber-shader decomposition now has a reproducible first-stage
permutation inventory in [`UBER_SHADER_PERMUTATION_MAP.md`](UBER_SHADER_PERMUTATION_MAP.md):
16 static axes, 323 permitted static entries, 16 dynamic axes, and the
prioritized opaque, alpha-test, sheen, translucent, glass and
advanced-translucency families. `OPAQUE_OUTPUT_GRAPH.md` traces the complete
combo-24/dynamic-0 contribution topology and `STATUS_PROXY_DELTA.md` isolates
the optional dynamic-2 material modifier. `ALPHA_TEST_DELTA.md` captures the
cutout/channel-layout delta and `SHEEN_DELTA.md` isolates the dedicated sheen
lobes. The next static target is translucency.

## Product contract

Deadlimit Shade is the Deadlock material-authoring product under the Deadlimit umbrella. Its purpose is to make Substance 3D Painter a reliable preview and texture-authoring environment for current retail Deadlock character materials.

Deadlimit Shade must be driven by observed current retail resources where practical. Static knowledge in this repository is a compatibility baseline, not permission to assume that a retail shader or material contract cannot change.

The product is broader than one GLSL file. The target system has eight responsibilities:

1. **Retail reference inspection** — obtain the current character-material inputs, shader family, texture references and relevant VMAT parameters from the installed retail Deadlock build.
2. **Painter shader kit** — provide the minimum set of Painter shaders required to preview supported Deadlock material families.
3. **Preview mesh preparation** — generate Painter-only geometry required for effects that a Painter surface shader cannot generate, initially the inverted-hull silhouette outline.
4. **Painter installation/integration** — install or update Deadlimit Shade resources in the selected Substance 3D Painter installation/shelf and keep the operation deterministic.
5. **Authoring/export contract** — define Painter channels and export packing from evidence taken from current Deadlock materials rather than from a generic PBR convention.
6. **Deadlimit pipeline bridge** — later connect exported authoring data to the existing CSDK/VMAT/compile/package pipeline without changing its already validated ownership rules.
7. **Character profiles** — let the artist select a supported Deadlock character and apply that character's verified material controls, texture conventions and outline defaults consistently across the Painter project.
8. **Minimal Painter workflow** — keep mesh preparation, shader assignment and
   profile synchronization behind the installed Deadlimit Shade integration so
   ordinary project creation remains the artist-facing workflow.

The standalone Shade workflow may expose paths to retail Deadlock and Substance 3D Painter, but path discovery should reuse existing Deadlimit/Steam detection where available instead of hardcoding one machine layout.

## Scope of the first version

The first version targets the normal opaque hero/character material path. It does not attempt to reproduce every world, particle, UI, translucent or special-effect shader in Deadlock.

The first usable version must expose a `Character` selector. Ivy is the first
calibrated profile, not a permanent hardcoded special case. `Custom` remains
available for unsupported characters and for parameter investigation.

Retail Deadlock is the visual ground truth. ValveResourceFormat / Source 2 Viewer is used for resource discovery, decompilation and inspection; its renderer is not the acceptance oracle for Deadlock shading.

## Target artist workflow

Deadlimit Shade has a one-time installation step. Normal project creation should
then require only:

1. open Substance 3D Painter and choose `File > New`;
2. select the artist's ordinary source mesh;
3. select the `Deadlimit Shade` project template;
4. create the project;
5. select a supported character profile and, when automatic discovery is
   insufficient, select the source texture/mask folder.

The installed Painter integration completes the remaining work automatically:

- generate a disposable preview mesh while leaving the selected source file
  unchanged;
- preserve source geometry, render normals, UVs, vertex colors and material
  identities;
- add the reversed, expanded `__deadlimit_outline` shell;
- reload the prepared preview mesh into the newly created project;
- assign `Deadlock_Hero.glsl` to source Texture Sets and
  `Deadlock_Outline.glsl` to the reserved outline Texture Set;
- apply one character selection atomically to shading, mask interpretation,
  outline color and outline width;
- install the required channels, environment and Deadlock export preset;
- keep preview-only shell data out of authored texture exports and downstream
  Deadlimit packages.

The temporary preview mesh belongs in a versioned local cache keyed by source
content, builder version, character profile and outline settings. Regeneration
must be deterministic and must never modify the production mesh in place.

Painter's surface-shader API cannot create or expand the outline geometry. The
mesh-processing step is therefore required, but it is an implementation detail
of the installed workflow rather than an artist-authored preparation step.
The shipped workflow uses the bundled native mesh processor. It does not
require 3ds Max, Blender or another user-installed DCC.

## Shader inventory

### `Deadlock_Hero.glsl` — implement now

Purpose: common Painter surface shader for the first opaque hero-material reconstruction.

Initial bootstrap behavior:

- use Painter's current metal/rough PBR implementation as the known-valid rendering baseline;
- bind Base Color, Roughness, Metallic and Specular Level through Painter channels;
- preserve Painter AO, emissive and SSS plumbing from the supported shader libraries;
- expose diagnostic views for Base Color, Roughness, Metallic, AO and mesh Vertex Color;
- expose Vertex Color multiplication only as an explicitly experimental control, disabled by default;
- contain no guessed Deadlock-specific lighting constants.

Deadlock-specific behavior is added only when its source inputs and effect are identified from current retail resources.

The first Deadlock-specific implementation is the statically recovered NPR
direct-diffuse response. Its unresolved runtime controls are exposed as
calibration parameters until a value is confirmed. The shader must provide
diagnostic views for raw `NdotL`, wrapped input, quantized response and final
direct diffuse so a profile can be tuned without compensating through textures.

### `Deadlock_Outline.glsl` — implement now

Purpose: shade the preview-only inverted-hull Texture Set.

Responsibilities:

- render the outline shell as flat unlit color;
- expose `Outline Color`;
- emit the same resolved color in Painter's Material and Base Color views;
- use the face-culling convention required by the generated reversed-winding shell.

`Outline Width` is not a shader parameter. Width changes modify preview geometry and therefore belong to the preview-mesh generator.

### Additional shaders — evidence gated

Do not create separate hero shaders merely because a feature has a different visual role.

Create another shader file only when current retail evidence demonstrates a material family that cannot be represented cleanly as parameters or feature switches of `Deadlock_Hero.glsl`.

Potential future files include:

- `Deadlock_Hair.glsl`;
- `Deadlock_Eye.glsl`;
- `Deadlock_Translucent.glsl`.

These names are placeholders for possible families, not committed architecture.

## Architecture

```text
Current retail Deadlock
        |
        +--> resource inspection/decompilation
        |        |
        |        +--> material reference manifest
        |        +--> texture/channel/parameter evidence
        |
Artist/source mesh
        |
        +--> Deadlimit Shade preview builder
                 |
                 +--> unchanged original render geometry
                 +--> preview-only expanded/reversed outline shell
                           material: __deadlimit_outline
                 |
                 v
        Substance 3D Painter
                 |
                 +--> original Texture Sets -> Deadlock_Hero.glsl
                 +--> __deadlimit_outline -> Deadlock_Outline.glsl
                 |
                 +--> Deadlock Painter channel template
                 +--> Deadlock export preset
                           |
                           v
                 existing Deadlimit/CSDK material pipeline
```

The outline shell uses a dedicated preview-only material because Painter creates Texture Sets from mesh material definitions and supports a unique Shader Instance per Texture Set. This keeps shell identification independent of production vertex color, texture channels and hero material IDs.

## Character profile system

Character selection is a product-level contract rather than a collection of
separate hero shaders.

The Painter-facing shader initially exposes a static `Character` combobox with
stable profile IDs:

```text
Custom
Ivy
Abrams
Haze
Doorman
...
```

The list grows only when a profile has enough evidence to state which common
shader features it uses and which values remain calibrated approximations.
Reordering existing IDs is forbidden because Painter projects persist shader
parameter values.

Each profile owns only character/material configuration:

- common-shader feature switches;
- scalar and color defaults;
- relevant texture packing/interpretation choices;
- known transmissive, tint, rim and related material values;
- validation state and reference provenance;
- outline enabled state, color and preview-width policy;
- material-family exceptions that have been confirmed for that character.

Profiles do not embed or redistribute retail textures, meshes, VPK/VCS files
or other Valve assets. For an artist-authored skin, texture resources continue
to come from the ordinary Painter project. A supported character's optional
default-retail preview first reuses exact extracted resources from the
Deadlimit project `0source` folder, then decodes missing dependencies read-only
from the `RetailSourceVpk` recorded by `EXTRACT SOURCES`. Generated preview PNGs
remain in a disposable local cache and are excluded from the repository.
The retail VMAT feature/scalar parameters travel with that cache manifest.
Ivy's Max-authored FBX does not contain the `color$0` stream used by its eye
material. Apply restores that stream from the exact extracted
`models/heroes_wip/ivy/ivy_ivy.dmx`, matching triangle corners by position and
rejecting any topology mismatch. `F_VERTEX_COLOR` and
`g_fVertexColorStrength1` then select the same color multiplication for the eye
shader instance. The generated outline shell preserves the restored stream.

Painter 9.1 channel-solo modes read the authored Texture Set stack and bypass
textures supplied directly to a custom shader. The dock therefore exposes
`Deadlimit View` modes for Shaded, Base Color, Roughness, Metallic and AO. They
run through the Deadlimit shader in Painter's Material viewport and keep both
ordinary Painter channels and optional retail preview maps visible. Native
Painter channel-stack injection becomes available through the official Layer
Stack API in supported Painter 10+ integrations.
Apply embeds content-addressed copies of both Deadlimit shaders in the SPP and
updates every live shader instance to those resources. This prevents Painter's
installed-shelf cache from retaining an earlier GLSL revision. Retail sampler
access is marked `nonlocal` so inactive Texture Sets do not show Painter's
local-sampling cyan/blue diagnostic tiles.

The canonical profile data should live in small reviewable text manifests,
for example:

```text
profiles/
    ivy.json
    abrams.json
    haze.json
    doorman.json
```

A deterministic build step embeds the same generated GLSL profile block into
the standalone hero and outline shaders from those manifests. Painter supports
only its embedded GLSL import libraries, so the generated block cannot be a
custom imported library. Hand-maintained duplicate values in GLSL and JSON are
not allowed.
Every manifest records its stable numeric ID, retail build/reference identity,
evidence classification and last validation date.

The shader resolves the selected profile into a common configuration structure.
`Custom` reads all exposed controls directly. Supported character profiles use
their recorded defaults, with a clearly labelled override mode for investigation.
The shader remains usable without the future Deadlimit Shade application.

Long term, the application presents one character selector and applies the
selection atomically to:

1. every original hero Texture Set using `Deadlock_Hero.glsl`;
2. the `__deadlimit_outline` Texture Set using `Deadlock_Outline.glsl`;
3. the preview-mesh generator's outline-width/profile input;
4. the export/profile validation rules.

Until that integration exists, both Painter Shader Instances expose the same
stable profile ID manually. Validation must reject a project whose hero and
outline profile selections disagree.

The first integration slice now exposes a `Deadlimit Shade` dock with a single
`Character` selector and `Apply Deadlimit` action. The action owns profile
synchronization, disposable format-native preview generation, mesh reload and
both shader assignments. The native processor preserves every source object
and material, adds one reserved outline material, and supports FBX, GLB and
glTF without modifying the selected source file.

## Evidence rules

Every reconstructed feature or value is recorded as one of:

- **Confirmed by pipeline/runtime** — observed through repository tooling or a
  live supported runtime operation.
- **Confirmed by static retail evidence** — recovered from current retail
  resources without observing a live draw.
- **Calibrated approximation** — selected against a controlled visual reference.
- **Blocked/unresolved** — required evidence is unavailable or the mechanism has
  not yet been isolated.

Calibrated values are never described as retail runtime values.

A hero-specific observation stays hero-specific until another material proves the same mechanism or the shader/resource definition establishes that it is generic.

## Milestone 0 — Bootstrap

Goal: establish code structure and prove that our two required Painter shader entry points are valid resources.

Deliverables:

- `docs/ROADMAP.md`;
- `docs/Outline.md`;
- `docs/Validation.md`;
- `shaders/Deadlock_Hero.glsl`;
- `shaders/Deadlock_Outline.glsl`.

Acceptance:

1. both `.glsl` files load in the current Substance 3D Painter without shader compile errors;
2. `Deadlock_Hero.glsl` renders a normal Painter metal/rough material and every debug view returns the intended input;
3. `Deadlock_Outline.glsl` renders a dedicated Texture Set as flat unlit color.

This milestone deliberately does not claim Deadlock visual parity.

## Milestone 1 — Ivy retail reference manifest

Goal: remove ambiguity about what the first real Deadlock material receives.

Choose one normal Ivy body/clothing material from the current retail build and record:

- retail VMDL/mesh resource path;
- exact VMAT_C path;
- decompiled/readable VMAT data;
- shader/material family identifier;
- all referenced VTEX_C resources;
- exported source texture data used for comparison;
- texture slot -> source texture -> channel mapping;
- color-space/import information that can be established;
- scalar/vector parameters and feature switches;
- UV set dependencies;
- mesh Vertex Color dependency, including component semantics if used;
- alpha/emissive/SSS/NPR/rim/highlight/outline inputs if present;
- resource/tool versions used for capture.

Deliverable:

```text
reference/ivy/<material>/manifest.md
```

Binary retail assets should not be committed merely to make the manifest self-contained. Store paths, hashes and reproducible extraction notes where redistribution is inappropriate.

Acceptance: another machine with the current retail build and documented tooling can identify the same inputs without guessing.

## Milestone 2 — Original-input Painter parity baseline

Goal: isolate shader differences from texture-authoring differences.

Use the original Ivy mesh/material input set in Painter.

Checks:

1. Base Color sampling matches the intended source texture/channel.
2. Tangent-space Normal input is oriented correctly.
3. Roughness source and range are correct.
4. Metallic source and range are correct.
5. AO source is correct.
6. Vertex Color debug output matches source mesh data where applicable.
7. UV selection is correct.

Acceptance: every input entering the Painter shader is known and testable before Deadlock-specific BRDF/NPR work begins.

## Milestone 3 — NPR direct-diffuse preview

Goal: deliver the first visibly useful Deadlock lighting behavior using the
statically recovered equation and a deterministic Painter test-light contract.

Implement in `Deadlock_Hero.glsl`:

- an artist-controlled directional key light for the diagnostic/direct preview;
- the recovered wrap operation and triangular fractional quantizer;
- diffuse step sharpness;
- NPR/PBR diffuse blend;
- direct-light normalization;
- NPR direct-diffuse enable;
- debug views for each intermediate value;
- an Ivy profile plus `Custom` mode.

Unknown engine globals remain exposed and labelled as calibration values. They
must not be described as retail defaults. Painter environment specular remains
available while the direct-diffuse component is isolated and validated.

Acceptance:

1. the shader compiles in the supported Painter version;
2. the `Character` selector visibly switches between `Custom` and Ivy;
3. the quantizer matches the recovered equation over a generated `NdotL` sweep;
4. debug views make wrap, step and blend independently testable;
5. fixed inputs, camera, environment and profile produce a reproducible image;
6. the profile is useful for authoring even while exact runtime globals remain unresolved.

## Milestone 4 — Reconstruct the common hero material

Goal: replace the generic Painter baseline one verified component at a time.

Investigation order is controlled by dependency, not by visual prominence:

1. texture packing and decode rules;
2. color-space transformations;
3. roughness transformation;
4. metallic/specular model and scalar controls;
5. normal response;
6. AO/occlusion behavior;
7. base-color modifiers and Vertex Color semantics;
8. NPR/rim/highlight controls and masks;
9. Fresnel/specular tint if present;
10. SSS/skin response where applicable;
11. emissive behavior;
12. alpha/cutout behavior if it belongs to the same family.

For each implemented feature, add an evidence note containing:

- source VMAT parameter(s);
- source texture/channel if any;
- observed retail behavior;
- Painter implementation;
- validation asset;
- evidence classification.

Acceptance: original Ivy inputs produce a stable match under the controlled validation setup, with remaining mismatches explicitly classified rather than compensated by arbitrary constants.

## Milestone 5 — True outline preview

Goal: reproduce the geometry silhouette extension used by the target Deadlock character look inside Painter.

Prototype contract:

1. duplicate only the preview copy of the render mesh;
2. displace shell vertices along the selected source render normals;
3. reverse shell triangle winding;
4. assign the dedicated material `__deadlimit_outline`;
5. import/reload the preview mesh in Painter;
6. assign `Deadlock_Outline.glsl` to that Texture Set;
7. verify that only the intended inverted-hull side contributes;
8. change width by regenerating/reloading the preview mesh.

Required validation:

- split normals/hard edges survive correctly;
- production Vertex Color remains untouched;
- material assignments on original geometry remain untouched;
- open boundaries and non-manifold inputs fail predictably or use a documented policy;
- thin/disconnected accessories do not generate uncontrolled artifacts;
- skinned/posed preview behavior is defined before automation depends on it;
- no preview shell can enter Deadlock export/compile/package output.

The outline reconstruction must separately establish:

- whether retail uses a constant, distance-scaled or projection-scaled apparent width;
- which normals or expansion vectors drive the silhouette;
- outline color and any character/material tinting;
- whether outline fragments are flat unlit or receive scene-dependent modulation;
- culling, depth-test and occlusion behavior;
- treatment of internal boundaries, disconnected accessories and thin surfaces;
- character-specific enable/disable and width defaults.

`Deadlock_Outline.glsl` owns outline fragment color and any confirmed fragment
response. The preview-mesh generator owns shell expansion, winding and width.
The shared character profile provides defaults to both components.

The first manual prototype may use a single Ivy shell. The milestone is complete
only after character switching updates outline shading and width coherently and
the same mechanism works on at least one materially different hero.

The production form of this milestone is a standalone preview-mesh processor.
It accepts a supported Painter source format, writes a canonical disposable
Painter-compatible scene, and preserves stable source material/Texture Set
identities. FBX-native validation through 3ds Max is the current correctness
oracle for normals, transforms, material assignments and winding; it is not a
runtime dependency of the finished artist workflow.

A fragment-only `N·V`/Fresnel edge darkening mode may exist later as a fallback. It is not the primary outline implementation.

## Milestone 6 — Controlled validation environment

Goal: make screenshot comparisons reproducible.

Define and version:

- Painter environment resource/configuration;
- exposure and display settings;
- camera FOV and framing rules;
- model pose;
- retail capture conditions that can reasonably be controlled;
- required comparison views.

Runtime graphics-debugger capture is not a prerequisite for this milestone.
Ordinary reference images from a controlled safe retail viewing path may be
used to calibrate exposed parameters. Each comparison records character,
material, pose, camera, lighting conditions, build identity and crop/color
handling. Fit direct-diffuse controls against several surface orientations
rather than one attractive view.

Do not tune shader constants against screenshots taken under changing lighting.

## Milestone 7 — Cross-hero validation

Goal: separate common hero behavior from Ivy-specific data.

Start with materially different characters after Ivy is stable. Candidate set currently includes Ivy, Abrams, Haze and Doorman; replace candidates if inspection shows one uses an atypical family that would invalidate the intended comparison.

For every difference classify it as:

- common shader behavior;
- ordinary material parameterization;
- distinct material family;
- hero-specific data;
- engine-side effect outside Painter's supported surface-shader contract;
- unresolved.

Acceptance: no Ivy-specific workaround remains in the common path without a mechanism explaining why it applies generally.

Additional acceptance:

- every supported character appears in the selector with a stable ID;
- switching profiles changes only documented material/outline configuration;
- the same source textures produce deterministic results when returning to a profile;
- unsupported behavior is reported at profile level instead of hidden in common GLSL;
- hero and outline Texture Sets agree on the selected profile.

## Milestone 8 — Painter authoring contract

Goal: make the preview useful for creation rather than only for forensic comparison.

Deliverables:

- required Texture Set channels;
- channel defaults;
- project/template configuration;
- an installable `Deadlimit Shade` template that carries shader resources,
  channels, display/environment settings and export configuration;
- texture naming contract;
- automatic mask discovery plus an explicit folder-selection fallback;
- Deadlock export preset;
- exact output packing/inversion/color-space rules;
- normal-map convention;
- alpha use;
- bit depth/file format decisions supported by the current CSDK/retail pipeline.

Where existing Deadlimit Manager CUSTOM-material texture binding already has a confirmed convention, Shade should integrate with it rather than create a competing ownership model.

## Milestone 9 — Deadlimit Shade application/integration

Goal: turn the validated prototype resources into the actual product workflow.

Initial application responsibilities:

- locate or let the user select retail Deadlock;
- locate or let the user select Substance 3D Painter;
- detect installed/current Shade resource version;
- inspect the current retail material contract needed by supported Shade profiles;
- install/update Painter shader, template, environment and export resources;
- observe creation of a project that uses the Deadlimit Shade template;
- generate a cached preview mesh with the outline shell and reload it into that
  same project without discarding authored project state;
- expose one character selector and synchronize hero shader, outline shader,
  preview geometry and validation settings from the selected profile;
- own `Outline Width` and other geometry-generation settings;
- open or refresh the Painter workflow without modifying the production source mesh;
- report unsupported/currently changed retail material contracts instead of silently applying stale assumptions.

Later integration may pass exported textures into the existing Deadlimit Manager CSDK preparation path. That bridge must respect current Manager ownership rules for CUSTOM VMATs and project-root texture sources.

## Automation strategy

Automation is introduced only after the manual operation being automated is proven.

Preferred sequence:

1. deterministic command/service for retail reference inspection;
2. deterministic FBX-native preview-mesh generation proven on an actual hero;
3. standalone cached preview-mesh generation without a user-operated DCC;
4. deterministic Shade resource and project-template installation;
5. Painter project detection, mesh refresh and atomic shader/profile setup;
6. texture/mask discovery and export-to-Deadlimit bridge.

Do not make Painter automation a prerequisite for proving the shader itself.

## Validation layers

### Static/repository checks

Can be automated without Painter:

- expected Shade resource files exist;
- generated manifests satisfy schema/required fields;
- preview generator does not write into production source paths;
- shell material naming is reserved and deterministic;
- shader source contains required Painter entry point and supported state declarations.

### Painter smoke checks

Initially manual:

- shader compiles;
- expected channels/parameters appear;
- debug modes map to intended inputs;
- dedicated outline Texture Set accepts its own Shader Instance;
- outline shell culling/winding is correct.

Automate only if the current Painter API/CLI exposes a stable supported path for the exact check.

### Retail parity checks

Visual and resource-grounded:

- original inputs only;
- fixed comparison conditions;
- one changed shader mechanism per test;
- evidence note updated after each accepted result.

## Repository structure

```text
DeadlimitShade/
    README.md

    shaders/
        Deadlock_Hero.glsl
        Deadlock_Outline.glsl

    profiles/
        ivy.json
        ...

    docs/
        ROADMAP.md
        Outline.md
        Validation.md
        MaterialModel.md        # added when reconstruction starts
        TexturePacking.md       # added when packing is established

    reference/
        ivy/

    presets/
        Deadlock_Hero_Export.spexp

    environments/
```

Do not create empty placeholder assets solely to satisfy this tree.

## Immediate implementation slice

The active slice first proves the character shader on the real textured Ivy
project, then resumes outline integration:

1. open a disposable copy of the existing Ivy SPP without changing its source
   FBX, layers or masks;
2. validate the exact original FBX and all seven expected Texture Sets;
3. apply `Deadlock_Hero` to every source Texture Set with `-HeroOnly` and select
   the Ivy profile;
4. obtain a visual viewport pass for the actual Ivy skin in shaded and diagnostic
   views;
5. compare that output with the controlled Deadlock reference and correct the
   common hero shader where evidence requires it;
6. after the hero viewport passes, produce one compact disposable preview shell
   that preserves the source material identities and authored SPP content;
7. obtain a combined hero/outline viewport pass and a width-regeneration pass;
8. prototype the `Deadlimit Shade` project template and automatic post-create
   setup around the proven contracts.

This slice is successful when the real textured Ivy project visibly renders
through `Deadlock_Hero`, followed by a clean controllable outline without losing
the project's authored content. A large intermediate FBX is not itself an
acceptance result.

### Implementation progress — 2026-09-06

- profile schema, stable `Custom`/Ivy IDs and deterministic dual-shader profile
  generation are implemented;
- hero NPR direct-diffuse controls, intermediate diagnostics and mathematical
  sweep checks are implemented;
- the outline shader consumes the same generated character selector and keeps
  width outside GLSL;
- the controlled sphere generator now emits unchanged hero geometry plus a
  width-controlled, reversed-winding `__deadlimit_outline` shell;
- a generic imported-OBJ builder now preserves all source mesh records and
  implements the literal inverted-hull prototype: duplicate split render
  vertices, offset along their existing normals and reverse winding;
- the outline mesh smoke proves deterministic output, material isolation,
  displacement, winding and width-only regeneration at the OBJ contract level;
- the imported-OBJ smoke additionally proves source-line, material, UV and
  vertex-color preservation, negative-index handling and fail-closed behavior
  when per-corner render normals are unavailable;
- controlled Painter shader compilation, Texture Set separation, viewport
  culling and synchronized `Custom`/Ivy profile switching are validated on the
  generated sphere;
- `Open-PainterShadePreview.ps1` captures the proven remote import, shader
  assignment, multi-Texture-Set hero mapping, profile synchronization and
  preserving reload contract as a reusable tool;
- Painter visually passed width changes `0.04 -> 0.08 -> 0.04`; each reload
  retained the hero/outline mapping and Ivy diagnostic profile values;
- a 3ds Max batch bridge exported only `ivy_ivy*` nodes from the controlled FBX
  with normals, UVs and materials, without modifying the source;
- the FBX-native inverted-hull generator now emits a disposable 3-mm Ivy shell
  with two source nodes and 32,084 source faces; a full-face-corner split keeps
  render normals exact and creates 96,252 shell vertices;
- Painter imports the original `texture_ivy_builder.fbx` directly and exposes
  seven Ivy Texture Sets, while the first exported source-plus-shell FBX stayed
  busy in Painter's importer for more than five minutes; this is an FBX export
  compatibility blocker, not evidence against the source FBX or the shell
  algorithm;
- the actual Ivy character passed programmatic import, three-Texture-Set shader
  assignment and a preserving `0.5 -> 1.0` diagnostic width reload;
- actual-character inspection at deliberately large diagnostic widths shows
  internal shell exposure at boundaries and one detached source component.
  Coordinate welding cannot be applied blindly:
  at least one coincident-position group has opposing normals whose average is
  zero. The retail vertex-stage outline operation still requires static/runtime
  proof; averaged-normal variants remain diagnostics rather than defaults.
- `Open-PainterShadePreview.ps1 -HeroOnly` now treats the character shader as an
  independent validation path and works in Windows PowerShell 5.1 without the
  unsupported `Invoke-RestMethod -NoProxy` parameter;
- Painter 9.1.0 opened the 169 MB disposable copy of the textured Ivy SPP,
  reported the original FBX and all seven expected Texture Sets, mapped every
  set to `Deadlimit Hero`, selected Ivy ID 1 with shaded view and saved the copy;
- that textured-SPP result is confirmed at application/API level. Current
  Computer Use discovery returned no Windows application surfaces, so no new
  visual PASS is claimed for the Ivy viewport;
- a full-scene export that created 73 source nodes plus 73 separate outline
  nodes (87,197 source faces; 261,591 split shell vertices) left Painter at
  `busy:true` with no useful visible progress. It is recorded as a rejected
  integration experiment rather than a required pipeline stage.
- the rejected DCC backend has been replaced by the self-contained
  `Deadlimit.MeshPreview` processor. The actual Ivy source completed in under
  one second and retained all 74 meshes plus all seven source materials;
- the final Painter Apply completed in 5.5 seconds with visible phase/elapsed
  progress, added one outline Texture Set, assigned `Deadlimit Hero` only to
  `ivy_builder_arms/body/head`, and retained `Main shader` on all four Valve
  Texture Sets;
- the optional Ivy default-retail preview reuses `0source` first and fills only
  missing maps from the read-only retail VPK. Four dedicated shader instances
  were assigned in 8.3 seconds, with no launch of Deadlock and no source-file
  modification;
- current `materials/dev/vertcolor_pbr_basic.vmat_c` inspection records
  `F_VERTEX_COLOR=1` and `g_fVertexColorStrength1=1`. The exact extracted
  `ivy_ivy.dmx` contains the corresponding `color$0` stream, while the source
  FBX does not. Apply transferred all 7,896 eye-corner colors onto the matching
  FBX mesh and retained them on its outline copy;
- `Deadlimit View -> Base Color` applies the VMAT vertex-color multiply before
  emitting its diagnostic output, so the resolved eye color is identical in
  Base Color and shaded preview modes;
- `subs_ivy_builder_deadlimit.spp` reopened with the same eight Texture Sets and
  six Shader Instances. A cache manifest restored the original FBX after a
  simulated fresh plugin session;
- format-native offline conversion preserves physical bounds for FBX, GLB and
  glTF. GLB/glTF millimetres use metre-based units and no repeated root scaling.
- the Milestone B lighting skeleton now contributes quantized NPR direct
  diffuse, stepped direct specular and retail-mask-gated rim lighting to the final shaded
  composition;
- `Lighting Inputs -> Diagnostic Neutral` plus Direct Diffuse, Direct Specular,
  Rim, NPR Composite and Painter PBR Baseline views isolate the three terms
  without retail-map variation;
- a fixed Painter 9.1.0 Ivy scene proved that the neutral composite differs
  clearly from the same-scene Painter PBR baseline and that Material/Retail mode
  retains retail textures, restored eye color and the inverted-hull outline;
- the first retail composition failed the likeness gate because its lighting
  was muddy, its outline dominated the image and two highlight systems were
  visible at once;
- environment rotation identified the concrete boundary: Painter PBR
  environment specular and Deadlimit direct specular produced incompatible
  highlights. Painter environment specular was removed from Shaded;
- a fresh fixed-camera Painter 9.1.0 A/B passed the Milestone B visual gate:
  direct diffuse, stepped specular and masked rim are independently visible,
  the combined result differs clearly from Painter PBR, and retail textures,
  restored eyes and inverted-hull outline remain intact;
- the `uniform_main_light` yaw contract was verified by changing environment
  rotation from 145 to 235 degrees and observing the signed `N dot L`
  diagnostic move across the model;
- all new Ivy lighting values remain calibrated approximations pending
  Milestone D comparison across several controlled Deadlock views.

The successful 2026-09-06 validation used Painter 9.1.0. Its remote server bound
to IPv6 loopback (`::1:60041`), so the automation probes IPv4, IPv6 and
`localhost` rather than treating `127.0.0.1` refusal as application failure.
