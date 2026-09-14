# Deadlock advanced-translucency character delta

Date: 2026-09-08

## Scope and identity

- parent VCS SHA-256:
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- static combo `32824`: `S_ADVANCED_TRANSLUCENCY=1`, `S_ALPHA_TEST=1`,
  `S_USE_NPR_LIGHTING=1`, `S_USE_STATUS_EFFECTS_PROXY=1`;
- dynamic `0`, file `0`: 80,056-byte SPIR-V, SHA-256
  `f96bcabb2e4303efa4beb6f661805d1bdc74d37e0b3db94fa399cc2bb1f78d71`;
- topology: 96 permitted dynamic states and 64 shader files;
- dynamic-0 depth and normalized blend state: identical to alpha-test combo 56.

## Added mask contract

Relative to alpha test, the family adds eight named fields:

- `g_tAltTranslucency`;
- primary and alternate secondary-UV selectors;
- primary and alternate UV scroll speeds;
- primary and alternate scroll-time quantizers;
- `g_nTranslucencyBlendMode1`.

The base material still reads `g_tColor.rgb` at its ordinary UV. Coverage
resamples `g_tColor.a` at the independently selected/scrolled primary
translucency UV and samples `g_tAltTranslucency.r` at the alternate UV.

```text
primary = sample(g_tColor, primaryAnimatedUv).a
alternate = sample(g_tAltTranslucency, alternateAnimatedUv).r

mode 0: combined = primary * alternate
mode 1: combined = saturate(primary + alternate)
other:  combined = saturate(primary - alternate)
```

Each scroll clock can be independently quantized by rounding engine time to
the nearest configured interval before applying fractional UV motion.

## Coverage and output

`combined` replaces `g_tColor.a` in the alpha-test coverage equation:

```text
coverage = vertexColor.a
         * combined
         * angleTerm
         * distanceBoost

if coverage < alphaTestReference:
    discard
```

Metalness remains in the separate `g_tMetalness.r` texture. Surviving pixels
use the same opaque NPR bounce, direct diffuse/specular, rim, self illumination
and environment-specular graph. Dynamic 0 multiplies the final RGB by the
survival mask and writes alpha 1 for a surviving non-additive pixel.

Despite the family name and premultiplied-capable render state, this selected
program is a dual-mask animated cutout. It contains no framebuffer refraction,
fog/sky composition or basic-translucent dual-target routing.

## Painter consequence

A faithful preview needs two independently selectable UV sets, quantized
scroll clocks, three mask-combine modes and the recovered alpha-test controls.
This can be implemented as an optional cutout family without introducing a
new lighting model. Runtime scroll values and effect-specific masks still need
material evidence before useful defaults can be assigned.

## Evidence classification

- **Confirmed by static retail evidence:** eight added fields; mask channels;
  independent UV/scroll paths; multiply/add/subtract modes; reuse of alpha-test
  coverage and shared opaque NPR lighting; solid surviving output.
- **Confirmed by pipeline/runtime:** current VCS identity, combo axes, 96/64
  topology, bytecode identity/size and render-state match to alpha test.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live scroll/quantize/blend values, material usage,
  source masks and visual parity.

This closes the prioritized opaque, status, alpha-test, sheen, basic
translucent, glass and advanced-translucency pixel-family decomposition. Live
engine buffers, effect variants and Painter reconstruction remain separate
tasks.
