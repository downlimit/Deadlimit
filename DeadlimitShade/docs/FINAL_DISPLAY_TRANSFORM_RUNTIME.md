# Final display transform — Reduced CSDK capture

Scope: the existing Ivy capture, linear opaque target after event 804. Resource
and event identities are capture-specific. All statements below are
**confirmed by pipeline/runtime** for this capture; they do not establish
current retail identity. No captured assets are committed.

## Dependency chain

Render target `ResourceId::8389` is 3440x1440 `R16G16B16A16_FLOAT` through
its bound views. Pixel History confirms that `(565,99)` and `(602,140)` retain
their event-804 values through the later writes to this shared target.

| Events | Resources and shaders | Executed purpose |
| --- | --- | --- |
| 1347 | `8389` -> `8412` (860x360), PS `2066` | four-tap downsample and luminance/mask bloom seed |
| 1367-1510 | `8412` <-> `6807`/`8427`, PS `1800`, `1802`, `1795`, `1799`, `1801`, `1803`, `1804` | bloom downsample, separable blur and reconstruction; final bloom is `8427` |
| 1531 | `8389` + `8427` -> `6541` (`R8G8B8A8_UNORM`), PS `2086` | exposure, bloom add, rational tonemap and explicit sRGB transfer |
| 1553 | `6541` -> `8422` (`R8G8B8A8_UNORM`), PS `1789` | FXAA |
| 1571 | `8422` SRGB view -> `8365` SRGB RTV, PS `2184` | sRGB decode/re-encode copy; display bytes are preserved |
| 1617 | write to `8365`, PS `2277` | overlay draw; Pixel History shows no write at either sampled pixel |
| 1663, 1713 | `8365` UNORM view -> swapchain `7011` UNORM RTV, PS `2278` | one-to-one top-left crop/copy; event 1714 presents `7011` |

The dependency list comes from `GetUsage` for each resource above, followed
only through outputs of those usages. No unrelated capture-wide shader scan was
used. Event 1713 interpolates source UV `(pixel + 0.5)/(3440,1440)`, so both
selected source coordinates map to the same swapchain coordinates.

## Event 1531 equation and inputs

PS `2086` binds `t0 = 8389` and `t1 = 8427`; there is no LUT. Its used
constant buffers are `ResourceId::7003`, `6706`, and `7349`. The captured
exposure is `cb1[18].x * cb2[0].x = 1 * 1.013959527015686`.

For this event, `cb0[4].z=0`, `cb0[3].y=1`, `cb0[3].w=0`, and the executed
input to the curve is therefore:

```text
exposed = max(linearOpaque, 0) * 1.013959527015686
x = min(2.8 * (exposed + bloom), 6.062026500701904)
```

With `A=0.2951999903`, `B=0.3856000006`, `C=0.5`, `D=0.3950999975`,
`E=0.2381000072`, `F=1`, and `W=1.5298149586`, ISA 18-27 executes per channel:

```text
linearMapped = saturate(
  (((x*(A*x + C*B) + D*E) / (x*(A*x + B) + D*F)) - E/F) * W
)
```

ISA 28-34 then applies the ordinary piecewise sRGB transfer (`12.92*x` up to
`0.003131`; otherwise `1.055*x^(1/2.4)-0.055`). The later blend amount
`cb0[3].z` is zero, so ISA 35-41 leaves this result unchanged. The reference in
`tools/deadlock_display_reference.py` reproduces these executed boundaries.

## Two numerical paths

The bloom column is the actual `8427` sample returned at ISA 12. Event-1531
stored values and FXAA/final values are RenderDoc Pixel History post-values.

| Boundary | `(565,99)` | `(602,140)` |
| --- | --- | --- |
| event-804 linear opaque | `[0.298339844, 0.219482422, 0.250732422]` | `[0.747070313, 0.240966797, 0.144897461]` |
| bloom `8427` | `[2.68221e-6, 2.44379e-6, 1.96695e-6]` | `[2.20537e-5, 1.96695e-5, 1.62125e-5]` |
| exposed | `[0.302504539, 0.222546294, 0.254232526]` | `[0.757499039, 0.244330585, 0.146920159]` |
| curve input `x` | `[0.847020209, 0.623136461, 0.711856544]` | `[2.121058941, 0.684180677, 0.411421835]` |
| linear mapped | `[0.404620945, 0.306496799, 0.347095221]` | `[0.738137722, 0.334666938, 0.201756835]` |
| event-1531 sRGB shader output | `[0.668640018, 0.589559793, 0.623848259]` | `[0.874629080, 0.613612473, 0.486498833]` |
| event-1531 UNORM stored | `[170,150,159]/255` | `[223,156,124]/255` |
| event-1553 FXAA stored | `[173,154,162]/255` | `[223,156,124]/255` |
| event-1713/final displayed RGB | `[173,154,162]/255` | `[223,156,124]/255` |

Event 1571 reads the FXAA target as SRGB and produces linear shader values
`[0.41796875,0.322265625,0.361328125]` and
`[0.73828125,0.33203125,0.201171875]`, then its SRGB RTV restores the same
display bytes. Events 1663/1713 read those bytes through an UNORM view and copy
them unchanged. FXAA depends on neighboring texels; its actual per-pixel result
is recorded above, while the CPU reference intentionally stops at the fully
recovered event-1531 equation.
