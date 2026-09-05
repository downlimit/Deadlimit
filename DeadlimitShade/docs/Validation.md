# Deadlimit Shade — Validation Protocol

Status: bootstrap protocol.

Updated: 2026-09-05.

## Purpose

Validation must isolate one unknown at a time. The first tests prove Painter API/resource plumbing; later tests prove Deadlock material behavior against current retail resources.

## Test A — `Deadlock_Hero.glsl` compile/load

Input: any ordinary opaque Painter test mesh with a normal material Texture Set.

Steps:

1. Add `Deadlock_Hero.glsl` to Painter's shader resources/shelf.
2. Assign it to the test Texture Set.
3. Confirm that Painter reports no shader compile error.
4. Confirm that the following `Debug View` entries appear:
   - Shaded;
   - Base Color;
   - Roughness;
   - Metallic;
   - Ambient Occlusion;
   - Vertex Color RGB;
   - Vertex Color Alpha.
5. Keep `Vertex Color Multiply = 0`.
6. Confirm that `Shaded` behaves as the normal Painter metal/rough baseline.

Result to record:

```text
Painter version:
Deadlock_Hero.glsl: PASS / FAIL
Compile error, if any:
Unexpected viewport behavior, if any:
```

A compile failure is handled before any Deadlock-specific shader code is added.

## Test B — hero input diagnostics

Use a mesh/material with known channel values.

Check exactly one diagnostic at a time:

- Base Color debug returns the sampled Base Color input;
- Roughness debug returns grayscale roughness;
- Metallic debug returns grayscale metallic;
- Ambient Occlusion debug returns raw Painter AO, without scene shadow multiplication;
- Vertex Color RGB returns mesh `color0.rgb`;
- Vertex Color Alpha returns mesh `color0.a`.

Acceptance: diagnostic outputs agree with the known source data. Do not compensate for a failed input mapping in the shading model.

## Test C — `Deadlock_Outline.glsl` compile/load

Input: a mesh containing a dedicated material named exactly:

```text
__deadlimit_outline
```

Steps:

1. Confirm Painter creates a dedicated Texture Set for `__deadlimit_outline`.
2. Create a unique Shader Instance for that Texture Set.
3. Assign `Deadlock_Outline.glsl` to it.
4. Confirm no shader compile error.
5. Confirm `Outline Color` appears.
6. Change `Outline Color` and verify flat unlit output.

Result to record:

```text
Painter version:
Dedicated Texture Set: PASS / FAIL
Unique Shader Instance: PASS / FAIL
Deadlock_Outline.glsl: PASS / FAIL
Outline Color: PASS / FAIL
```

## Test D — manual inverted-hull proof

This test validates the geometry/culling contract before any automatic shell generator is written.

Prepare a preview-only two-material mesh:

1. keep the original mesh unchanged;
2. duplicate the render geometry;
3. offset duplicate vertices outward by a small known distance using the chosen render normals;
4. reverse duplicate triangle winding;
5. assign `__deadlimit_outline` to the duplicate;
6. export the combined preview mesh;
7. import it into Painter;
8. assign `Deadlock_Hero.glsl` to original Texture Sets;
9. assign `Deadlock_Outline.glsl` to the shell Texture Set.

Checks:

- outline extends outside the original silhouette;
- front-facing original surfaces are not replaced by outline color;
- rotating the camera does not expose the wrong shell side;
- disconnected pieces behave predictably;
- obvious split-normal/hard-edge explosions are absent;
- original Vertex Color is unchanged;
- original material assignments are unchanged.

If reversed winding plus `cull_face on` fails, record the exact observed face behavior before changing the convention.

## Test E — width regeneration proof

Before Painter automation exists:

1. produce preview mesh A with width `w1`;
2. produce preview mesh B from the same source with width `w2`;
3. reimport/refresh B in the same Painter project using Painter's supported mesh-update workflow;
4. verify authored project data remains attached to the original Texture Sets;
5. verify only the silhouette width changes as expected.

This proves that `Outline Width` can be owned outside GLSL without destroying authoring state.

## Test F — first Ivy retail-input parity

Run only after Tests A/B pass.

Required inputs come from the current Ivy reference manifest.

For the selected material compare, one channel at a time:

1. Base Color;
2. Normal orientation;
3. Roughness;
4. Metallic;
5. AO;
6. Vertex Color, if consumed;
7. UV set;
8. other verified texture/mask channels.

Use original retail inputs. Newly authored Painter textures are excluded from this stage.

Acceptance: shader inputs are proven before the Deadlock-specific response is reconstructed.

## Evidence record format

For each material mechanism added after bootstrap, record:

```text
Feature:
Reference hero/material:
Retail resource(s):
Controlling VMAT parameter(s):
Texture/channel input:
Observed retail behavior:
Painter implementation:
Validation result:
Evidence class:
Date / retail build context:
```

Allowed evidence classes:

- Confirmed by retail / our pipeline
- Confirmed by current external source
- Hypothesis

## Regression rule

After a feature becomes part of the common hero shader, changing it requires rechecking every reference material that previously established the feature as common. A hero-specific failure does not justify changing the common model until the mechanism causing the difference is identified.