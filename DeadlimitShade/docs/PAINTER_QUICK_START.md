# Painter quick start

## One-time installation

From the Deadlimit project root, run:

```powershell
pwsh -NoProfile -File .\DeadlimitShade\tools\Install-DeadlimitPainterPlugin.ps1
```

The installer places `deadlimit_apply.py` in Painter's user `python/startup`
folder. If Painter is already running with remote scripting enabled, the
`Deadlimit Shade` dock opens immediately. Otherwise restart Painter once. The
dock loads automatically in later sessions.

The installer owns only the Deadlimit plugin/runtime copies it names. It does
not change retail files, source meshes or Painter projects.

## Preview a textured project

1. Open an SPP whose project mesh is FBX, GLB or glTF.
2. Find the `Deadlimit Shade` dock. If it was closed, restore it from Painter's
   `Window > Views` menu.
3. Choose `Ivy` in `Character`.
4. Click `Preview Ivy as Deadlock`.
5. Wait for `Deadlock preview active for Ivy`.

The action creates a disposable cached mesh, preserves painting strokes,
restores Ivy's required eye vertex colors, resolves the read-only retail maps,
assigns the hero/outline shader instances and selects the normal shaded view.
There is no manual shader-instance or texture-map setup.

`Lighting Inputs` and `Deadlimit View` are diagnostics:

- `Diagnostic Neutral` proves lighting independently of authored material data;
- `Direct Diffuse`, `Direct Specular`, `Rim Contribution` and `NPR Bounce`
  isolate the lighting terms;
- `Material / Retail` plus `Shaded` is the artist-facing preview;
- `Painter PBR Baseline` provides a same-scene comparison.

## Authoring the rim mask

After **Preview as Deadlock**, expand **Deadlimit Rim Light** in Shader
Settings to edit Enable, Strength, Cutoff / Width, Sharpness and the two Up Ramp
limits. The values start from the selected character profile. Reapplying the
same character preserves edits; use **Reset to Character Preset** in the
Deadlimit Shade panel when the captured defaults are wanted again.

The retail `tint_rim.g` mask remains active until an artist creates the Painter
channel. Click **Create Paintable Rim Mask** to add the linear grayscale
`Deadlimit Rim Mask` (`User0`) channel to non-outline Texture Sets. It can then
be enabled on paint/fill layers like any other Painter channel. Zero removes rim
locally and one allows the full recovered rim response. **Export Rim Mask
PNGs…** writes the authored channel as a separate grayscale PNG per Texture
Set; it does not modify retail textures or build a VMAT/package.

The current Ivy look is a calibrated Painter approximation. The dock says so
explicitly; profile numbers are not claimed as retail runtime values.

## Reproduce the Milestone B proof scene

For an exact A/B, use Painter 9.1.0 OpenGL and the values recorded below after
`Preview as Deadlock` completes:

- camera position `149.768860, 50.999405, 15.292190`;
- camera rotation `140.143265, 99.880875, -140.561340`, perspective FOV `50`;
- built-in `viewer/panorama` version
  `df0d611fe5a4c86ca5a36bcc11fc06b363478e03`, rotation `145` degrees;
- PBR exposure `0.66 EV`, environment exposure `1.0 EV`;
- `Material / Retail` with `Shaded` for Deadlimit, then
  `Painter PBR Baseline` for the same-scene comparison.

Shift+RMB rotates Painter's main light yaw for Deadlimit diffuse and specular.
`Diagnostic Neutral` keeps this interaction live while replacing material
inputs, which makes the response easy to verify in another textured project.
