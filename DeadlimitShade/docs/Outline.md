# Deadlimit Shade — Painter Outline Architecture

Status: architecture decision / prototype pending.

Recorded: 2026-09-05.

## Goal

Reproduce the Deadlock character silhouette outline in the Substance 3D Painter viewport closely enough for Deadlimit Shade to provide a useful authoring preview.

The target implementation is a true geometry silhouette extension equivalent to an inverted-hull outline.

A fragment-only Fresnel or `N·V` edge-darkening effect remains an optional fallback/debug approximation because it can only shade fragments already inside the original silhouette.

## Confirmed Painter constraint

Current Substance 3D Painter custom GLSL shaders expose a surface-shader entry point:

```glsl
void shade(V2F inputs)
```

The supported custom shader path provides only a portion of the fragment shader. It does not expose a user-authored vertex shader, geometry shader, arbitrary second draw pass or general custom framebuffer post-process pass capable of creating an expanded silhouette from the original mesh alone.

Consequences:

- `Deadlock_Hero.glsl` cannot generate the required outline geometry;
- real outline width cannot be implemented by moving vertices from a normal Painter shader parameter;
- the primary solution must prepare preview geometry before Painter shades it.

Current Adobe references:

- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shaders-shader-api/surface-shader-shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/interface/shader-settings/shader-settings
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/interface/texture-set/texture-set-list

## Prototype architecture

Deadlimit Shade prepares a temporary Painter-only mesh containing:

1. unchanged original render geometry;
2. a duplicate outline shell;
3. shell vertices displaced outward along the selected source render normals;
4. reversed shell triangle winding;
5. a dedicated preview-only material named `__deadlimit_outline`.

Painter creates a Texture Set from the dedicated shell material. Current Painter supports a unique Shader Instance per Texture Set, so the shell Texture Set can use `Deadlock_Outline.glsl` while the original material Texture Sets use `Deadlock_Hero.glsl`.

```text
production/source mesh
        |
        v
Deadlimit Shade preview generation
        |
        +-- original geometry (unchanged)
        |
        +-- duplicate shell
                +-- offset along selected source normals
                +-- reversed winding
                +-- material = __deadlimit_outline
        |
        v
Painter preview mesh
        |
        +-- original Texture Sets -> Deadlock_Hero.glsl
        +-- __deadlimit_outline   -> Deadlock_Outline.glsl
```

The dedicated material/Texture Set is the selected prototype identification mechanism. It avoids consuming production Vertex Color, UVs or texture channels merely to tag the shell.

## Shader responsibility

`Deadlock_Outline.glsl` shades already-generated shell fragments. It does not own shell generation.

Minimum responsibilities:

- flat unlit rendering;
- `Outline Color` parameter;
- explicit face-culling state compatible with reversed shell winding.

The initial implementation uses Painter back-face culling with reversed shell winding. This is a prototype contract and must be validated with an actual two-material mesh before it becomes a stable generation rule.

## Outline Width ownership

`Outline Width` belongs to the preview-mesh generator.

Changing width means:

1. regenerate/update shell vertex positions;
2. re-export the temporary preview mesh;
3. refresh/reimport that mesh in Painter through a supported workflow;
4. retain authored Painter project data.

A future Painter API that exposes a supported programmable vertex stage could justify revisiting this split. Current architecture does not assume such an API exists.

## Required invariants

The implementation must preserve all of the following:

- production mesh topology is unchanged;
- production material assignments are unchanged;
- preview shell data cannot enter Deadlock export/compile/package output;
- shell generation is deterministic for identical source mesh and parameters;
- existing Vertex Color used by the Deadlock material remains intact;
- split normals and hard edges are not silently destroyed;
- UV data on the production geometry remains unchanged;
- skinned meshes retain whatever deformation state the chosen Painter workflow requires;
- the reserved preview material cannot collide silently with an artist production material;
- hero-specific workarounds are not promoted to the common generator without cross-hero evidence.

## First prototype

Use Ivy because the existing Ivy extraction/authoring/compile/package path is already validated and therefore removes unrelated pipeline uncertainty.

Test sequence:

1. take a known Ivy preview/source mesh;
2. duplicate one render mesh as the shell;
3. offset the duplicate along its source render normals;
4. reverse triangle winding;
5. assign `__deadlimit_outline`;
6. import the combined preview mesh into Painter;
7. create a unique Shader Instance for the shell Texture Set;
8. assign `Deadlock_Outline.glsl`;
9. confirm that the line extends beyond the original silhouette;
10. confirm that front/back-face behavior is correct across camera rotation;
11. change shell width by regeneration and reimport;
12. confirm that production source data is untouched.

## Validation questions

The prototype must answer these before automatic shell generation is considered stable:

- Which normal representation should drive displacement at split-normal boundaries?
- Should coincident position vertices with different render normals remain split for shell displacement, or require a welded displacement normal while preserving original render data?
- Does constant object-space offset produce acceptable apparent thickness for the intended Painter camera distances?
- Is a model-scale-normalized width policy required?
- How should open boundaries be handled?
- How should non-manifold topology be detected and reported?
- How should very thin surfaces and disconnected accessories behave?
- Does reversed winding plus Painter `cull_face on` produce the desired hull side consistently for all supported import formats?
- Does the dedicated shell Texture Set create unacceptable Painter painting/baking overhead?
- What supported Painter mesh-refresh path preserves the project state most reliably when width changes?
- What is the required behavior for skinned or posed preview meshes?

## Production isolation

The shell is derived preview data.

It must live outside the authoritative source/production asset path and must never be used by:

- DMX/VMDL preparation for CSDK;
- CUSTOM VMAT ownership;
- retail replacement model compilation;
- VPK packaging.

Deadlimit Shade may cache generated preview assets, but deletion of that cache must never remove or alter artist source files.

## Fallback

A fragment-only outline approximation based on grazing angle / `N·V` / Fresnel may be added later for situations where preview geometry cannot be generated or refreshed.

It must be labeled as an approximation and must not replace the inverted-hull implementation as the target Deadlimit Shade outline path.