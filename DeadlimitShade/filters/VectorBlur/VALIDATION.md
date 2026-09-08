# Deadlimit Vector Blur — Validation

Status: source implementation exists; SAT/Painter runtime validation is pending.

The acceptance sequence follows one check -> result -> conclusion -> next step. Do not skip ahead after the first failure.

## Check 1 — Generate the `.sbs`

Run:

```powershell
.\build.ps1
```

If PySBS fails before `sbscooker`, rerun the generator directly to isolate the graph-generation error:

```powershell
python .\build_vector_blur.py --output .\build\Deadlimit_Vector_Blur.sbs
```

Pass condition:

- `build/Deadlimit_Vector_Blur.sbs` exists;
- PySBS reports no exception;
- opening the generated package in the current Substance 3D Designer reports no broken dependencies.

If this fails, stop. Do not test Painter.

## Check 2 — Cook the `.sbsar`

Pass condition:

- `sbscooker` exits with code 0;
- `dist/Deadlimit_Vector_Blur*.sbsar` exists;
- `sbscooker` reports no invalid graph/function/type error.

If this fails, stop. Do not import a stale archive from an earlier build.

## Check 3 — Painter filter resource

Import the newly cooked archive as a Painter Filter resource.

Pass condition:

- Painter recognizes the archive as a usable Filter effect;
- the effect applies to a color channel without an evaluation error;
- `Amount = 0` is visually identical to source.

If Painter rejects the graph as a generic filter, the next implementation step is to wrap the kernel with Painter's current generic filter template rather than changing kernel math.

## Check 4 — Public UI contract

Verify controls in this order:

```text
Type
Amount
Angle Offset
Ridge Smoothness OR Revolutions
Vector Map
Property
Map Softness
```

Verify `Type` contains exactly:

```text
Natural
Constant Length
Perpendicular
Direction Center
Direction Fading
```

Verify `Property` contains exactly:

```text
Red
Green
Blue
Alpha
Luminance
Lightness
Hue
Saturation
```

Verify conditional control visibility:

- `Natural`, `Constant Length`, `Perpendicular` -> `Ridge Smoothness` visible; `Revolutions` hidden.
- `Direction Center`, `Direction Fading` -> `Revolutions` visible; `Ridge Smoothness` hidden.

Any additional artist-facing quality/sample control is a failure.

## Check 5 — Vector Map / Anchor Point

Use an Anchor Point containing a deterministic black-to-white ramp as `Vector Map`.

Pass condition:

- assigning/removing the Anchor Point changes the blur field;
- every `Property` entry selects the intended component;
- `Map Softness = 0` preserves the unsoftened field;
- increasing `Map Softness` reduces high-frequency direction changes.

## Check 6 — Type semantics

Use the same source image and scalar vector map for all five runs.

Pass conditions:

- `Natural`: blur magnitude follows map slope magnitude.
- `Constant Length`: non-zero slope regions use effectively constant blur length.
- `Perpendicular`: direction is orthogonal to the slope-derived direction.
- `Direction Center`: blur extends on both sides of the source pixel.
- `Direction Fading`: blur extends in one direction.

Run `Angle Offset` at `0`, `90`, `180`, and `-90` degrees and verify the expected rotation.

Run positive and negative `Amount` and verify direction reversal.

## Check 7 — Color/grayscale Painter compatibility

Test the filter on:

- Base Color;
- a grayscale mask/channel;
- alpha-bearing color input.

Pass condition:

- no type mismatch;
- grayscale source remains numerically correct;
- alpha is preserved by the sampling path.

A grayscale incompatibility is an integration failure, not permission to publish two unrelated user-facing filters without a design decision.

## Check 8 — After Effects calibration

Use one lossless source image and one lossless Vector Map at a fixed resolution in both applications.

Capture this matrix:

| Type | Amount | Angle Offset | Ridge/Revolutions | Property | Map Softness |
| --- | ---: | ---: | ---: | --- | ---: |
| Natural | 20 | 0 | Ridge 1 | Luminance | 0 |
| Natural | -20 | 0 | Ridge 20 | Lightness | 20 |
| Constant Length | 40 | 30 | Ridge 1 | Red | 0 |
| Perpendicular | 40 | 0 | Ridge 1 | Luminance | 20 |
| Direction Center | 40 | 0 | Rev 1 | Luminance | 0 |
| Direction Fading | 40 | 0 | Rev 1 | Luminance | 0 |
| Direction Center | 40 | 45 | Rev 9.9 | Hue | 20 |

For each case export:

- After Effects reference;
- Painter output;
- absolute difference image.

Classification:

- matching public behavior with small kernel differences -> calibration work;
- wrong direction/range/property -> implementation bug;
- resolution-dependent displacement mismatch -> pixel/UV conversion bug;
- large differences confined to ridges -> `Ridge Smoothness` model requires calibration.

Pixel-for-pixel identity is not an acceptance requirement until Cycore's kernel is independently characterized. The required first target is matching control semantics and stable Painter behavior.
