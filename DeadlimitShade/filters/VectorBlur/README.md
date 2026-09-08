# Deadlimit Vector Blur

`Deadlimit Vector Blur` is a Substance 3D Painter filter whose public control surface mirrors **After Effects CC Vector Blur**.

## Public controls

The Painter UI is intentionally limited to the CC Vector Blur controls, in this order:

1. `Type`
   - `Natural`
   - `Constant Length`
   - `Perpendicular`
   - `Direction Center`
   - `Direction Fading`
2. `Amount`
3. `Angle Offset`
4. `Ridge Smoothness` for `Natural`, `Constant Length`, and `Perpendicular`
5. `Revolutions` for `Direction Center` and `Direction Fading`
6. `Vector Map`
7. `Property`
   - `Red`
   - `Green`
   - `Blue`
   - `Alpha`
   - `Luminance`
   - `Lightness`
   - `Hue`
   - `Saturation`
8. `Map Softness`

There is deliberately no public `Samples`, `Quality`, `Iterations`, or Deadlimit-specific tuning parameter. The current kernel uses 16 fixed samples internally.

`Ridge Smoothness` and `Revolutions` are separate graph inputs with mutually exclusive `Visible If` expressions, so the control shown to the artist changes with `Type`.

## Behavior contract

The implementation is independent of Cycore's closed CC Vector Blur kernel. The public semantics are:

- `Natural`: direction is derived from the selected scalar property of `Vector Map`; vector magnitude follows local slope magnitude.
- `Constant Length`: direction uses the same local slope but normalizes its magnitude.
- `Perpendicular`: rotates the slope-derived field by 90 degrees.
- `Direction Center`: the scalar Vector Map value encodes direction over `Revolutions`; source samples are accumulated symmetrically around the current pixel.
- `Direction Fading`: uses the same direction encoding but accumulates only in the forward direction.
- `Angle Offset`: rotates the generated direction.
- `Map Softness`: prefilters the selected Vector Map property before field construction.
- `Amount`: measured in output pixels and supports positive and negative values.

`Amount` is hard-clamped to `[-500, 500]`, matching the observed CC Vector Blur amount limit. The other numeric slider bounds are UI ranges rather than parity claims; their exact After Effects editor bounds have not yet been measured from a controlled AE parameter dump.

## Source layout

```text
VectorBlur/
    build_vector_blur.py   # PySBS graph + Pixel Processor generator
    build.ps1              # generate .sbs and cook .sbsar
    VALIDATION.md          # required acceptance protocol
    .gitignore
```

Generated files are intentionally not committed:

```text
build/Deadlimit_Vector_Blur.sbs
dist/Deadlimit_Vector_Blur*.sbsar
```

## Build

Requirements:

- Substance Automation Toolkit / PySBS compatible with the current Substance 3D toolchain;
- `python` resolving to an interpreter with `pysbs`;
- `sbscooker` available on `PATH`, or explicit executable paths passed to `build.ps1`.

From PowerShell:

```powershell
.\build.ps1
```

Or with explicit executables:

```powershell
.\build.ps1 `
  -PythonExe "C:\Path\To\python.exe" `
  -SbsCookerExe "C:\Path\To\sbscooker.exe"
```

The expected result is a `.sbsar` under `dist/`.

## Painter use

Import the cooked `.sbsar` into the Painter shelf as a filter resource, add it as a Filter effect, then connect `Vector Map` to the intended texture/resource/Anchor Point.

The `Source` graph input is the primary filter input and is not an artist-facing replacement for the layer-stack source.

## Current verification status

Implemented and statically checked:

- public parameter names and dropdown values;
- conditional `Ridge Smoothness` / `Revolutions` visibility;
- Pixel Processor source generation;
- fixed internal sampling;
- no-op `Amount = 0` path by construction;
- reproducible `PySBS -> .sbs -> sbscooker -> .sbsar` build entry point.

Not yet runtime-proven in this repository session:

- PySBS generation against the user's installed SAT version;
- `sbscooker` success;
- Painter resource classification and Filter UI;
- color and grayscale layer-stack compatibility;
- Anchor Point assignment to `Vector Map`;
- visual calibration against After Effects CC Vector Blur.

Those checks are mandatory before the filter is called production-ready. See [`VALIDATION.md`](VALIDATION.md).
