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

    painter_plugins/
        deadlimit_apply.py

    profiles/
        schema.json
        ivy.json

    tools/
        Deadlimit.MeshPreview/
        Generate-CharacterProfiles.ps1
        Install-DeadlimitPainterPlugin.ps1
        New-OutlinePreviewMesh.ps1
        New-ShaderTestSphere.ps1
        Open-PainterShadePreview.ps1

    tests/
        profile-contract-smoke.ps1
        outline-mesh-contract-smoke.ps1
        outline-preview-mesh-smoke.ps1
        painter-apply-contract-smoke.ps1

    docs/
        ROADMAP.md
        Outline.md
        Validation.md
```

- `shaders/Deadlock_Hero.glsl` resolves a self-composed Deadlock preview from
  statically recovered NPR direct diffuse, bounce, stepped direct specular,
  masked rim, character profiles and focused diagnostic views. Painter PBR
  remains available only as the same-scene comparison view.
- `shaders/Deadlock_Outline.glsl` is the preview-only flat shader for the
  dedicated inverted-hull shell Texture Set and consumes the same character
  profile IDs as the hero shader.
- `profiles/*.json` is the source of truth for built-in character profiles;
  `tools/Generate-CharacterProfiles.ps1` embeds the same generated resolver in
  both standalone Painter shaders because Painter does not support custom GLSL
  import libraries.
- `tools/New-ShaderTestSphere.ps1` creates a disposable two-material OBJ with
  unchanged hero geometry and a width-controlled, reversed-winding outline
  shell for the first Painter outline proof.
- `tools/Deadlimit.MeshPreview` is the shipped self-contained mesh processor.
  It reads FBX, GLB or glTF, retains the complete source scene, appends a
  reversed-winding shell for every mesh and writes the same format selected by
  the artist. FBX centimetres and glTF metres receive format-specific outline
  units. No separately installed DCC is used.
- `tools/New-OutlinePreviewMesh.ps1` appends a derived outline shell to an
  imported OBJ while retaining every source line, source material assignment,
  UV reference and vertex-color component. Its literal inverted-hull default
  offsets split render vertices along their existing normals. Experimental
  averaging/welding modes are diagnostic and have no retail-parity claim.
- `tests/outline-mesh-contract-smoke.ps1` verifies shell displacement, material
  isolation, winding, deterministic generation and width-only regeneration.
- `tests/outline-preview-mesh-smoke.ps1` verifies imported OBJ preservation,
  normalized displacement, split normals, negative indices and predictable
  rejection when per-corner render normals are unavailable.
- `tools/Open-PainterShadePreview.ps1` connects to an already running Painter
  remote-scripting endpoint. `-HeroOnly` validates the original character mesh
  or an existing textured SPP without requiring preview-shell geometry. The
  two-material mode creates independent hero/outline Shader Instances and
  applies one synchronized character profile.
- `painter_plugins/deadlimit_apply.py` adds a `Deadlimit Shade` dock with one
  character selector and one `Preview <character> as Deadlock` button. Preview reads outline
  width/color from the selected profile, builds a format-preserving disposable preview mesh,
  reloads it with stroke preservation and assigns both shader instances with
  the same stable character ID. It retains the original source path in a cache
  manifest so Apply continues to work after Painter restarts.
- `tools/Install-DeadlimitPainterPlugin.ps1` installs the dock as a Painter
  `python/startup` module plus its minimal runtime and both GLSL resources.
  If Painter is already open and remote scripting is available, the installer
  opens the dock immediately. Otherwise restart Painter once. The dock then
  appears automatically in every compatible project; no Python-menu activation
  is required.
- In any compatible textured project, open the `Deadlimit Shade` dock, select
  the character and click `Preview <character> as Deadlock`. The integration
  generates the disposable preview mesh, resolves retail inputs, restores the
  required vertex colors, assigns hero and outline shaders and activates the
  `Shaded / Material` view. No manual shader or texture assignment is required.
- If an older optional-plugin build was installed, the installer removes only
  its owned `python/plugins/deadlimit_apply.py` copy before installing the
  automatic startup module.
- `docs/ROADMAP.md` is the authoritative implementation sequence.
- `docs/Outline.md` records the geometry-shell architecture and production-isolation requirements.
- `docs/Validation.md` defines the first Painter smoke tests and subsequent retail validation protocol.
- `docs/PAINTER_QUICK_START.md` is the reproducible artist-facing route from an
  open textured project to the Deadlock preview.
- `docs/DOTA_PAINTER_SHADER_STUDY.md` records the local comparative shader
  evidence and its adoption boundary.

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

`Apply Deadlimit` now passes on the actual Ivy project. Painter 9.1.0 preserved
all seven source Texture Sets, added `__deadlimit_outline`, assigned the hero
shader only to the three authored builder sets and retained `Main shader` on
the four Valve sets. The complete 74-mesh FBX received a 1.0 mm shell. The
visible operation completed in 5.5 seconds and the prepared 211.8 MB SPP
reopened with the same shader mapping.

The next slice is the zero-setup project-creation path:

1. package the Deadlimit shaders and Apply plugin as a Painter installation;
2. add the `Deadlimit Shade` File > New template;
3. detect the newly selected FBX/GLB/glTF and run the same proven Apply path;
4. expose a texture/mask-folder choice only when automatic discovery cannot
   resolve it;
5. validate a fresh project without hand-authored setup.

## Non-goals for the first version

- reproducing every Deadlock world/effect/UI shader;
- rebuilding the complete Source 2 renderer inside Painter;
- matching arbitrary map lighting;
- compensating for unknown engine post-processing with arbitrary shader constants;
- adding hero-specific hacks to the common shader without cross-hero evidence;
- modifying the already working Deadlimit model/compile/package path without a demonstrated Shade integration requirement.
