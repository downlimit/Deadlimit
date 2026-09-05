# Deadlimit Shade — Implementation Roadmap

Status: implementation started.

Updated: 2026-09-05.

## Product contract

Deadlimit Shade is the Deadlock material-authoring product under the Deadlimit umbrella. Its purpose is to make Substance 3D Painter a reliable preview and texture-authoring environment for current retail Deadlock character materials.

Deadlimit Shade must be driven by observed current retail resources where practical. Static knowledge in this repository is a compatibility baseline, not permission to assume that a retail shader or material contract cannot change.

The product is broader than one GLSL file. The target system has six responsibilities:

1. **Retail reference inspection** — obtain the current character-material inputs, shader family, texture references and relevant VMAT parameters from the installed retail Deadlock build.
2. **Painter shader kit** — provide the minimum set of Painter shaders required to preview supported Deadlock material families.
3. **Preview mesh preparation** — generate Painter-only geometry required for effects that a Painter surface shader cannot generate, initially the inverted-hull silhouette outline.
4. **Painter installation/integration** — install or update Deadlimit Shade resources in the selected Substance 3D Painter installation/shelf and keep the operation deterministic.
5. **Authoring/export contract** — define Painter channels and export packing from evidence taken from current Deadlock materials rather than from a generic PBR convention.
6. **Deadlimit pipeline bridge** — later connect exported authoring data to the existing CSDK/VMAT/compile/package pipeline without changing its already validated ownership rules.

The standalone Shade workflow may expose paths to retail Deadlock and Substance 3D Painter, but path discovery should reuse existing Deadlimit/Steam detection where available instead of hardcoding one machine layout.

## Scope of the first version

The first version targets the normal opaque hero/character material path. It does not attempt to reproduce every world, particle, UI, translucent or special-effect shader in Deadlock.

Retail Deadlock is the visual ground truth. ValveResourceFormat / Source 2 Viewer is used for resource discovery, decompilation and inspection; its renderer is not the acceptance oracle for Deadlock shading.

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

### `Deadlock_Outline.glsl` — implement now

Purpose: shade the preview-only inverted-hull Texture Set.

Responsibilities:

- render the outline shell as flat unlit color;
- expose `Outline Color`;
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

## Evidence rules

Every reconstructed feature is recorded as one of:

- **Confirmed by retail / our pipeline** — reproduced from current original resources and verified in retail Deadlock or through the already working Deadlimit pipeline.
- **Confirmed by current external source** — supported by current Painter, Source 2, ValveResourceFormat or related documentation/code but not yet proven in our live Deadlock path.
- **Hypothesis** — inferred from names, resource structure or observed rendering and awaiting controlled proof.

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

## Milestone 3 — Reconstruct the common hero material

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

## Milestone 4 — True outline preview

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

A fragment-only `N·V`/Fresnel edge darkening mode may exist later as a fallback. It is not the primary outline implementation.

## Milestone 5 — Controlled validation environment

Goal: make screenshot comparisons reproducible.

Define and version:

- Painter environment resource/configuration;
- exposure and display settings;
- camera FOV and framing rules;
- model pose;
- retail capture conditions that can reasonably be controlled;
- required comparison views.

Do not tune shader constants against screenshots taken under changing lighting.

## Milestone 6 — Cross-hero validation

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

## Milestone 7 — Painter authoring contract

Goal: make the preview useful for creation rather than only for forensic comparison.

Deliverables:

- required Texture Set channels;
- channel defaults;
- project/template configuration;
- texture naming contract;
- Deadlock export preset;
- exact output packing/inversion/color-space rules;
- normal-map convention;
- alpha use;
- bit depth/file format decisions supported by the current CSDK/retail pipeline.

Where existing Deadlimit Manager CUSTOM-material texture binding already has a confirmed convention, Shade should integrate with it rather than create a competing ownership model.

## Milestone 8 — Deadlimit Shade application/integration

Goal: turn the validated prototype resources into the actual product workflow.

Initial application responsibilities:

- locate or let the user select retail Deadlock;
- locate or let the user select Substance 3D Painter;
- detect installed/current Shade resource version;
- inspect the current retail material contract needed by supported Shade profiles;
- install/update Painter shader, template, environment and export resources;
- generate a preview mesh with optional outline shell;
- own `Outline Width` and other geometry-generation settings;
- open or refresh the Painter workflow without modifying the production source mesh;
- report unsupported/currently changed retail material contracts instead of silently applying stale assumptions.

Later integration may pass exported textures into the existing Deadlimit Manager CSDK preparation path. That bridge must respect current Manager ownership rules for CUSTOM VMATs and project-root texture sources.

## Automation strategy

Automation is introduced only after the manual operation being automated is proven.

Preferred sequence:

1. deterministic command/service for retail reference inspection;
2. deterministic preview-mesh generation;
3. deterministic Shade resource installation;
4. Painter project mesh refresh/reimport through a supported current Painter scripting/CLI mechanism;
5. export-to-Deadlimit bridge.

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

The active slice after this bootstrap is deliberately narrow:

1. load `Deadlock_Hero.glsl` and `Deadlock_Outline.glsl` in the current Painter;
2. fix only compile/API incompatibilities found by that smoke test;
3. capture one current Ivy retail material manifest;
4. verify the shader's diagnostic views against those original inputs;
5. then implement the first proven Deadlock-specific material behavior.

For outline work, first prove a two-material preview mesh manually. Build automatic shell generation only after the shell material, winding and Painter culling contract is confirmed.