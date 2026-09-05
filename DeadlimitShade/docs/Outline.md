# Deadlimit Shade: Painter Outline Strategy

Status: architecture decision / pending prototype.

Recorded: 2026-09-05.

## Goal

Reproduce the Deadlock character silhouette outline in the Substance 3D Painter viewport closely enough for Deadlimit Shade to serve as a reliable authoring preview.

The target is a true geometry silhouette extension equivalent in principle to an inverted-hull outline. A Fresnel or `N·V` edge darkening effect is not the target implementation because it only changes fragments that already belong to the visible surface and therefore cannot extend the silhouette beyond the original mesh.

## Painter constraint

Substance 3D Painter custom GLSL shaders are surface/fragment shaders. The supported custom shader path does not expose a user-defined vertex shader, geometry shader, arbitrary second draw pass, or custom framebuffer post-process pass that can generate a true expanded silhouette from the original mesh alone.

Consequences:

- a pure `Deadlock_Hero.glsl` implementation cannot create the required inverted-hull geometry;
- `N·V`, Fresnel, or grazing-angle darkening may be retained only as a cheap fallback/debug approximation;
- the production outline must involve preview geometry prepared outside the Painter surface shader.

Relevant Adobe references:

- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shaders-shader-api/surface-shader-shader-api
- https://experienceleague.adobe.com/en/docs/substance-3d-painter/using/scripting-and-development/shader-api-reference/shaders-shader-api/toon-shader-api

## Target architecture

Deadlimit Shade should prepare a temporary Painter-only preview mesh containing:

1. the original render geometry;
2. a duplicated outline shell;
3. the shell displaced outward along the intended vertex normals;
4. winding / normal / material identification configured so the Painter shader can render the shell as the outline while the original geometry continues to use the reconstructed Deadlock hero material.

Conceptually:

```text
original Deadlock mesh
        |
        v
Deadlimit Shade preview preparation
        |
        +-- original geometry
        |
        +-- duplicated outline shell
                |
                +-- outward normal offset
                +-- inverted-hull face setup
                +-- outline material/shell identification
        |
        v
Substance 3D Painter preview
```

The generated shell is preview-only. It must never alter the source Deadlock mesh, DMX/VMDL authoring data, or the retail compile/package output.

## Shader responsibility

The Painter GLSL shader remains responsible for shading, not for generating the shell geometry.

For the outline shell it should provide, at minimum:

- unlit outline rendering;
- `Outline Color`;
- correct front/back-face handling for the chosen shell representation;
- a reliable way to distinguish outline-shell fragments from original model fragments.

The exact shell-identification mechanism is intentionally not fixed yet. Candidate mechanisms include a dedicated material/texture set, mesh/material ID convention, or another preview-only marker that survives Painter import without contaminating the production asset.

## Outline Width ownership

`Outline Width` requires special handling.

Because Painter does not provide the custom vertex stage needed to move shell vertices interactively from the surface shader, the real geometry offset cannot be implemented as a normal GLSL slider.

The target product behavior should therefore be:

- `Outline Width` is a Deadlimit Shade preview-generation parameter;
- changing it regenerates or updates the temporary outline shell and refreshes the Painter preview mesh;
- `Outline Color` can remain a normal shader parameter because it does not require geometry changes.

If a future Painter API exposes a supported way to alter preview mesh positions or inject a custom vertex stage, this ownership can be revisited.

## Required invariants

The implementation must preserve the following:

- production mesh topology is unchanged;
- production material assignments are unchanged;
- preview shell data cannot leak into Deadlock exports;
- outline generation is deterministic for the same source mesh and parameters;
- existing vertex colors used by the Deadlock material remain intact;
- split normals / hard edges must not be silently destroyed by shell generation;
- skinned meshes must retain the deformation required for useful posing/preview if the Painter workflow depends on the same skeleton state;
- hero-specific fixes must not be promoted to the common outline path without cross-hero evidence.

## First prototype

Use Ivy as the first implementation reference because the existing Deadlimit Ivy pipeline is already practically validated and therefore minimizes unrelated uncertainty.

Prototype sequence:

1. Generate an Ivy preview mesh with one duplicate shell.
2. Offset the shell using the source render normals.
3. Configure the shell so only the intended hull side contributes to the visible outline.
4. Render the shell with a flat unlit color in Painter.
5. Verify that the line extends beyond the original silhouette rather than merely darkening the visible surface.
6. Verify that the shell remains isolated from Painter texture authoring and from Deadlock export data.
7. Test width changes through preview-shell regeneration.
8. Only after the Ivy result is stable, cross-check on materially and geometrically different heroes.

## Validation questions

The prototype must answer these before the design is considered stable:

- Which normal set should drive shell displacement at hard edges and split-normal boundaries?
- Does a constant object-space offset produce acceptable apparent thickness across hero scale and camera distance, or is a scale-aware policy required?
- How should open boundaries and non-manifold geometry be handled?
- How should disconnected accessories and very thin geometry behave?
- Can the same shell representation work cleanly with Painter's face-culling controls for both original and outline geometry?
- What is the least invasive shell-identification convention inside Painter?
- How should Deadlimit Shade refresh the preview mesh after an `Outline Width` change without disrupting the user's Painter project state?

## Fallback

A fragment-only edge approximation based on `N·V` / Fresnel may be implemented as an optional fallback for cases where temporary preview geometry is unavailable.

It must be explicitly treated as an approximation and must not replace the geometry-shell implementation as the target Deadlimit Shade outline path.
