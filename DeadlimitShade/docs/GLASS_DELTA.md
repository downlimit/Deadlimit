# Deadlock glass character delta

Date: 2026-09-08

## Scope and identity

- parent VCS SHA-256:
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- static combo `280`: `S_GLASS=1`, `S_USE_NPR_LIGHTING=1`,
  `S_USE_STATUS_EFFECTS_PROXY=1`;
- dynamic `0`, file `0`: 94,040-byte SPIR-V, SHA-256
  `759df1583f2412853cf16e6e15ee62b9dbea034a61ae8fd8f334fb16616cfd0c`;
- topology: 48 permitted dynamic states and 24 shader files;
- blend factors: the same premultiplied configuration reported for basic
  translucency; explicit depth-stencil descriptor absent.

## Added material and screen inputs

The glass family keeps opaque `g_tColor.a` metalness and adds:

- `g_tGlass`, whose red channel is the recovered transmission weight;
- `g_tFrameBufferCopyTexture`;
- cloak/refraction controls for factor, amount, blur and blur noise;
- scene depth and noise inputs through per-view buffers.

Basic translucent opacity controls and its separate `g_tMetalness` are absent.

## Refraction and blur path

The shader first rejects fragments whose current depth differs from the
selected scene depth by more than approximately 1.6 percent relative depth.
It then builds screen UV from projected shading normal, view angle and the
cloak/refraction controls. Animated screen noise offsets the UV.

Blur radius is:

```text
blurRadius = cloakBlurAmount
           * mix(cloakBlurMinRoughness,
                 cloakBlurMaxRoughness,
                 materialRoughness)
```

The framebuffer copy is sampled at the center and eight fixed disk offsets.
Each tap is accepted only when its matching depth is nonzero. Valid taps are
averaged; if none survives, the shader falls back to an unoffset center sample.

## Transmission composition

Let `T = g_tGlass.r`, `M = g_tColor.a`, `C = prepared base color` and
`Bscreen = blurred framebuffer copy`:

```text
transmissionWeight = T * (1 - M)

if T > 0:
    transmissionTint = mix(0,
                           min(1, exp(log(max(0.01, C))
                                      / max(0.01, dot(V,N)))),
                           T)
else:
    transmissionTint = transmissionWeight

litDiffuse *= (1 - T)
glassBehind = transmissionWeight * transmissionTint * Bscreen
```

`glassBehind` is added after the ordinary opaque NPR diffuse, probe bounce,
direct specular, rim, self illumination and environment specular terms. The
combined color then passes through the translucent fog/sky path. Dynamic 0
writes one output with ordinary/additive alpha selection; its glass surface is
not driven by `g_tColor.a` opacity.

## Painter consequence

The direct NPR surface response can be previewed, while the retail glass result
also requires a framebuffer copy, scene depth, validated multi-tap blur and the
engine fog path. Painter's surface-shader contract cannot reproduce that full
screen-space composition. Any simplified transmission preview must be labeled
as a calibrated approximation and kept out of opaque Ivy parity claims.

## Evidence classification

- **Confirmed by static retail evidence:** `g_tGlass.r` role; retained opaque
  metalness layout; depth rejection; screen-space offset; nine-tap validated
  blur; angle-dependent color transmission; diffuse attenuation; final
  framebuffer contribution and fog placement.
- **Confirmed by pipeline/runtime:** current VCS identity, combo axes, 48/24
  topology, bytecode identity/size and normalized blend factors.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live glass/cloak controls, framebuffer/depth binding
  semantics, exact render-pass ordering, fog inputs and Painter equivalence.

Advanced translucency is documented in `ADVANCED_TRANSLUCENCY_DELTA.md`.
