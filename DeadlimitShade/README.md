# Deadlimit Shade

Deadlimit Shade is the Deadlock material-authoring product under the Deadlimit umbrella. It is intended to make Substance 3D Painter a reliable preview and texture-authoring environment for current retail Deadlock character materials.

Status: implementation / prototyping.

Initial investigation: 2026-08-30.
Implementation bootstrap: 2026-09-05.

## Current implementation

The first code/resources now exist:

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
```

- `shaders/Deadlock_Hero.glsl` is a current Painter metal/rough bootstrap with Deadlimit diagnostic views. It deliberately does not claim Deadlock material parity yet.
- `shaders/Deadlock_Outline.glsl` is the preview-only flat shader for the dedicated inverted-hull shell Texture Set.
- `docs/ROADMAP.md` is the authoritative implementation sequence.
- `docs/Outline.md` records the geometry-shell architecture and production-isolation requirements.
- `docs/Validation.md` defines the first Painter smoke tests and subsequent retail validation protocol.

## v1 target

Original Deadlock mesh + original textures + reconstructed material parameters should produce a material response in Substance 3D Painter sufficiently close to retail Deadlock under a controlled preview setup to make Painter a dependable authoring viewport.

Pixel-perfect equivalence across arbitrary in-game scenes is outside the v1 contract because retail output may depend on lighting, shadows, post-processing and engine render passes that Painter does not expose to a custom surface shader.

## Product scope

Deadlimit Shade is broader than one GLSL file. The target system includes:

- current-retail material/resource inspection;
- Painter hero-material preview shader(s);
- true preview-only outline geometry where required;
- controlled validation environment;
- Painter channel/project conventions;
- Deadlock export preset;
- installation/update integration for Painter resources;
- later bridge to the existing Deadlimit Manager CSDK/VMAT pipeline.

The current implementation plan is in [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Core technical decisions

### Retail Deadlock is the visual ground truth

ValveResourceFormat / Source 2 Viewer is used for resource discovery, extraction and decompilation. Its renderer is not used as the final proof of Deadlock shading behavior.

References:

- https://github.com/ValveResourceFormat/ValveResourceFormat
- https://s2v.app/ValveResourceFormat/guides/format-support.html

### Painter custom shaders are surface shaders

Current Substance 3D Painter allows custom GLSL surface shaders and exposes normal, position, UVs, `color0`, material channels, camera/environment data and custom shader parameters.

The supported shader entry point is fragment/surface oriented. A custom Painter GLSL file does not provide the programmable geometry stage required to push an inverted hull beyond the source silhouette.

References:

- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shaders-shader-api/surface-shader-shader-api

### True outline uses preview geometry

The target outline path is:

```text
source/production mesh
        |
        v
Deadlimit Shade preview generation
        |
        +-- unchanged original geometry
        +-- expanded + reversed preview shell
                material = __deadlimit_outline
        |
        v
Painter
        +-- original Texture Sets -> Deadlock_Hero.glsl
        +-- outline Texture Set   -> Deadlock_Outline.glsl
```

Painter supports a distinct Shader Instance per Texture Set, which lets the preview shell use its own shader without consuming production Vertex Color, UV or texture channels as a shell marker.

Detailed decision: [`docs/Outline.md`](docs/Outline.md).

### Outline Width is geometry state

`Outline Color` is a normal shader parameter.

`Outline Width` belongs to Deadlimit Shade preview generation because changing it requires shell vertex displacement and preview-mesh refresh/reimport.

### Shader families are evidence gated

The first common shader is `Deadlock_Hero.glsl` for the normal opaque hero/character path.

Separate hair/eye/translucent/etc. shader files will be created only if current retail resources demonstrate a material-family distinction that cannot be represented cleanly as parameters or feature switches of the common hero shader.

## First reference asset

Ivy remains the preferred first reference because the existing Deadlimit Ivy pipeline has already been practically exercised through extraction, authoring, compilation, packaging and retail replacement.

Ivy-specific observations must remain scoped to Ivy until another material or the underlying shader/resource definition establishes a generic mechanism.

Candidate later cross-checks include Abrams, Haze and Doorman, subject to inspection of their actual current material families.

## Evidence classification

Every reconstructed feature is tracked as one of:

### Confirmed by retail / our pipeline

Reproduced from original resources and verified in retail Deadlock or through the already working Deadlimit pipeline.

### Confirmed by current external source

Supported by current Painter, Source 2, ValveResourceFormat or related documentation/code, but not yet proven in our live Deadlock path.

### Hypothesis

Inferred from parameter names, resource structure or observed rendering and awaiting controlled proof.

Hypotheses must not silently become common shader rules.

## Immediate next check

The bootstrap stops at the first external dependency that this repository cannot prove by itself: Painter compilation/runtime behavior.

Run the bootstrap protocol in [`docs/Validation.md`](docs/Validation.md):

1. load `Deadlock_Hero.glsl` in the current Substance 3D Painter;
2. confirm it compiles and the diagnostic views appear;
3. load `Deadlock_Outline.glsl` on a dedicated `__deadlimit_outline` Texture Set;
4. confirm flat unlit color and culling behavior;
5. only then capture the first current Ivy retail material manifest and begin Deadlock-specific material reconstruction.

This keeps the implementation sequence as one check -> result -> conclusion -> next step.

## Non-goals for the first version

- reproducing every Deadlock world/effect/UI shader;
- rebuilding the complete Source 2 renderer inside Painter;
- matching arbitrary map lighting;
- compensating for unknown engine post-processing with arbitrary shader constants;
- adding hero-specific hacks to the common shader without cross-hero evidence;
- modifying the already working Deadlimit model/compile/package path without a demonstrated Shade integration requirement.
