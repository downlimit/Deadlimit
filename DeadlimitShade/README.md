# DeadlimitShade

Experimental workspace for developing a Substance 3D Painter preview shader and related presets/tools for Deadlock character materials.

Status: research / prototyping.

Initial investigation: 2026-08-30.

## Goal

Build a Deadlock-focused Substance 3D Painter kit inspired by the role SoMuchDota 2 Shader plays for Dota 2: a Painter viewport setup that makes authored textures behave as close as practical to their final appearance in retail Deadlock.

The target is broader than a single GLSL file. The intended end state is a small toolkit containing:

- a Deadlock character preview shader for Substance 3D Painter;
- a controlled lighting/environment preset;
- Painter project/channel conventions;
- export presets matching Deadlock texture packing and color-space requirements;
- documentation of the supported Deadlock material features;
- later, optional integration with Deadlimit for VMAT/update automation.

### v1 success criterion

Original Deadlock mesh + original textures + reconstructed material parameters should produce a material response in Substance Painter that is sufficiently close to retail Deadlock under a fixed preview-lighting setup to make Painter a reliable authoring viewport.

Pixel-perfect equivalence across arbitrary in-game scenes is not a v1 requirement because the final retail image may depend on engine-side lighting, shadows, post-processing, tone mapping, screen-space effects and game-specific render passes that Painter does not reproduce.

## Current investigation results

### 1. Substance Painter is technically capable of hosting the shader

Substance 3D Painter supports custom GLSL surface shaders and exposes the data needed for a serious Deadlock material approximation.

Relevant inputs available to a Painter shader include:

- Base Color / custom texture channels;
- Normal;
- Roughness;
- Metallic;
- AO;
- Emissive;
- interpolated vertex color (`color0` / `inputs.color[0]`);
- UV sets;
- user channels;
- camera/view direction;
- environment lighting data;
- configurable shader parameters exposed in the Painter UI;
- subsurface-scattering related data/features available through Painter's shader system.

This means Painter itself is not the main blocker. The core problem is reconstructing the Deadlock material response accurately enough.

Adobe shader API references:

- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shaders-shader-api/surface-shader-shader-api

### 2. Vertex color can be included directly

Painter exposes interpolated mesh vertex color to custom shaders. This is important because the Deadlimit pipeline already has practical cases where Deadlock models depend on vertex-color data.

The shader therefore can evaluate vertex color as part of the preview material instead of treating it as an external unsupported feature.

This also lets DeadlimitShade stay compatible with the existing DMX/vertex-color workflow rather than requiring a separate authoring path.

### 3. Retail Deadlock must be the visual reference

Source 2 Viewer / ValveResourceFormat is useful for extracting and inspecting compiled Source 2 resources, but its Deadlock rendering must not be treated as the visual ground truth.

ValveResourceFormat's current format-support documentation notes that Deadlock shading support is still incomplete / work in progress. Consequently, a material looking a certain way in Source 2 Viewer does not prove that retail Deadlock evaluates it identically.

Reference:

- https://s2v.app/ValveResourceFormat/guides/format-support.html

Therefore:

- VRF / Source 2 Viewer = resource inspection, decompilation, texture/material discovery;
- retail Deadlock = final visual reference.

### 4. Compiled Source 2 resources provide useful reconstruction data

ValveResourceFormat can parse/decompile Source 2 compiled resources such as VMAT_C and VTEX_C. This gives us a practical way to inspect original Deadlock material parameters, texture references and texture packing instead of guessing them from screenshots.

Reference:

- https://github.com/ValveResourceFormat/ValveResourceFormat

For each reference material we should capture:

- original `.vmat_c`;
- decompiled/readable material data;
- referenced `.vtex_c` resources;
- exported source textures;
- mesh vertex color;
- UV usage;
- relevant material flags / scalar parameters / colors / switches.

### 5. The first target should be a character material, not every Deadlock shader

Deadlock contains multiple material families. Trying to reproduce all of them in the first pass would make validation ambiguous.

The first shader should target the normal hero/character material path only.

Initial implementation name:

`Deadlock_Hero.glsl`

The exact supported feature set must be derived from real retail hero materials rather than assumed in advance.

### 6. Ivy is the preferred first reference asset

Ivy is the preferred first reference because the Deadlimit pipeline around Ivy has already been practically exercised through extraction, authoring, compilation, packaging and retail replacement.

Using Ivy reduces unrelated uncertainty while we investigate the shader itself.

Important constraint: Ivy-specific observations must not automatically be promoted to global Deadlock material rules.

After the first Ivy match, the same shader must be checked against several materially different heroes before a feature is classified as common hero behavior.

Candidate cross-check set:

- Ivy;
- Abrams;
- Haze;
- Doorman.

The exact set can change if one of these uses an atypical material path.

## Evidence classification

Every discovered shader feature should be tracked as one of the following.

### Confirmed by retail / our pipeline

A behavior reproduced using original Deadlock resources and verified visually in retail Deadlock or in the already working Deadlimit pipeline.

This is the strongest category.

### Confirmed by current external source

A behavior documented in current Substance Painter, ValveResourceFormat, Source 2 or related documentation/code but not yet verified in our actual Deadlock pipeline.

### Hypothesis

A suspected material feature inferred from parameter names, decompiled resources, screenshots or partial renderer behavior.

Hypotheses must not silently become implementation assumptions.

## Proposed shader feature map

This is a working list, not a claim that every item exists as an independent Deadlock parameter.

Candidate areas to reconstruct:

- base-color response;
- normal-map response;
- roughness;
- metalness;
- ambient-occlusion contribution;
- specular response;
- Fresnel/edge response;
- specular tint if present;
- subsurface / skin response if present;
- emissive response;
- vertex-color-driven effects;
- texture packing / channel decoding;
- material masks;
- alpha behavior where relevant;
- hero-specific material switches;
- Deadlock-specific scalar/color parameters discovered in VMATs.

Each item must be proven from actual materials before being considered part of the common shader.

## Validation principle

Use a strict incremental loop:

1. Choose one material behavior.
2. Extract the exact retail inputs and parameters controlling it.
3. Reproduce only that behavior in Painter.
4. Compare against retail Deadlock under controlled conditions.
5. Record the result.
6. Only then move to the next behavior.

Avoid changing several unknown components at once.

## Development plan

### Phase 0 - Reference capture

Goal: establish reproducible source data before shader coding.

Tasks:

- select one Ivy body/clothing material as the first reference;
- locate its retail VMDL/mesh and VMAT_C;
- enumerate all referenced VTEX_C files;
- export/decompile the material and textures using current VRF tooling;
- record vertex-color and UV dependencies;
- capture retail screenshots from fixed camera/light conditions where possible;
- record all material parameters in a human-readable reference document.

Deliverable:

`reference/ivy/<material>/`

with a compact parameter manifest and source-resource notes.

### Phase 1 - Minimal Painter shader

Goal: prove the custom shader path and data mapping.

Implement:

- Painter GLSL shader skeleton;
- base color;
- tangent-space normal;
- roughness;
- metalness;
- AO;
- vertex color visualization/debug switch;
- exposed debug controls.

Deliverable:

`shaders/Deadlock_Hero.glsl`

At this phase, visual equivalence with Deadlock is not expected yet. The purpose is to verify correct Painter inputs and shader plumbing.

### Phase 2 - Original-texture parity test

Goal: remove texture-authoring uncertainty.

Use original Ivy textures inside Painter rather than newly painted textures.

The same mesh and source textures should therefore be driving both:

- retail Deadlock;
- Painter DeadlimitShade preview.

This isolates differences caused by the shader rather than by authored texture content.

### Phase 3 - Reconstruct Deadlock material response

Goal: progressively match the actual hero material.

Investigate and implement one verified component at a time:

- channel decoding / packing;
- roughness transformation;
- metalness behavior;
- specular model and scalar parameters;
- Fresnel response;
- vertex-color influence;
- SSS/skin response where applicable;
- emissive behavior;
- additional masks and VMAT switches.

Every implemented feature gets a short note containing:

- source material;
- controlling VMAT parameter(s);
- source texture/channel;
- observed retail effect;
- Painter implementation;
- confidence classification.

### Phase 4 - Controlled Painter lighting preset

Goal: make comparisons stable.

Create a fixed environment/lighting configuration used specifically for DeadlimitShade validation.

The preset should minimize differences caused by arbitrary Painter viewport lighting and exposure.

Possible deliverables:

- environment map or Painter environment configuration;
- exposure value;
- camera/display settings;
- validation screenshot protocol.

The environment must be treated as part of the preview contract, because shader equivalence cannot be evaluated reliably while lighting changes between tests.

### Phase 5 - Multi-hero validation

Goal: separate common Deadlock behavior from Ivy-specific setup.

Run original-material parity tests on several heroes.

For each discovered difference, classify it as:

- common shader behavior;
- material parameterization;
- hero-specific feature;
- unsupported/unknown engine-side feature.

Do not introduce hero-specific fixes into the common shader without evidence that the same mechanism is intended to be parameterized.

### Phase 6 - Painter channel template

Goal: make the shader practical for texture creation.

Define the Painter texture set channels required by Deadlock authoring.

Candidate channels may include:

- Base Color;
- Normal;
- Roughness;
- Metallic;
- AO;
- Emissive;
- custom mask channels as required by actual VMATs.

The final channel set must be based on reconstructed Deadlock texture packing, not a generic PBR template.

### Phase 7 - Deadlock export preset

Goal: remove manual channel repacking.

Create a Painter export preset that emits files in the packing, inversion and color-space layout required by the Deadlock material pipeline.

Requirements to determine experimentally:

- output files;
- per-channel packing;
- bit depth;
- linear vs sRGB treatment;
- normal format;
- alpha usage;
- filename conventions.

Deliverable candidate:

`presets/Deadlock_Hero_Export.spexp`

### Phase 8 - Deadlimit integration

Goal: connect Painter output to the existing modding pipeline.

Potential Deadlimit responsibilities:

- detect exported texture set;
- copy/update source textures in the CSDK project;
- generate or patch VMAT files from a known material template;
- preserve project-relative resource paths;
- trigger existing compile/package flow;
- later expose shader/export preset installation from Deadlimit.

This phase should only start after the Painter-side texture contract is stable.

## Proposed repository structure

```text
DeadlimitShade/
    README.md

    shaders/
        Deadlock_Hero.glsl

    reference/
        ivy/

    presets/
        Deadlock_Hero_Export.spexp

    environments/

    docs/
        MaterialModel.md
        TexturePacking.md
        Validation.md
```

Only `README.md` exists initially. Other directories/files should be added when their corresponding stage begins.

## First concrete task

The next implementation task should be deliberately narrow:

> Select one original Ivy character material, extract its full VMAT/VTEX input set from the current retail Deadlock build, and write a compact material manifest describing exactly what data the retail shader receives.

Do not start by guessing the Deadlock lighting model in GLSL. Establish the original material inputs first.

## Open questions

These require investigation rather than assumptions:

1. Which exact Deadlock shader/material family is used by the chosen Ivy reference material?
2. Which VMAT fields are generic hero-shader controls and which are Ivy-specific?
3. What exact texture packing and color-space transformations occur before shading?
4. Which vertex-color components are consumed by the material, if any?
5. Does the hero material expose a dedicated specular tint or derive specular color from other inputs?
6. How is skin/subsurface response parameterized in current Deadlock materials?
7. Which parts of the final retail look are impossible to reproduce inside Painter because they occur in later engine render passes?
8. Which fixed lighting/environment configuration produces the most useful authoring match rather than merely the closest screenshot in one scene?

## Non-goals for the first version

- reproducing every Deadlock world/effect/UI shader;
- matching engine post-processing;
- matching arbitrary map lighting conditions;
- rebuilding Source 2's complete renderer inside Painter;
- adding hero-specific hacks before cross-hero validation;
- changing the existing working Deadlimit model/compile/package pipeline unless the shader project proves a concrete need.
