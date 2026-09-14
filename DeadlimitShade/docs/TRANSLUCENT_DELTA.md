# Deadlock translucent character delta

Date: 2026-09-08

## Scope and identity

This document reduces basic character translucency to its delta from ordinary
opaque.

- parent VCS SHA-256:
  `eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`;
- static combo `88`: `S_TRANSLUCENT=1`, `S_USE_NPR_LIGHTING=1`,
  `S_USE_STATUS_EFFECTS_PROXY=1`;
- dynamic `0`, file `0`: 83,276-byte SPIR-V, SHA-256
  `30a240e4b713eb6150bd86404857ef21d249753401a04e28825970ce63163dfa`;
- topology: 16 permitted dynamic states and 8 shader files.

## Material and opacity delta

As in alpha test, `g_tColor.a` becomes opacity and metalness moves to
`g_tMetalness.r`. Basic translucent adds `g_flOpacityScale1`,
`g_flAlphaAnglePower1`, `g_flAlphaAngleScaleBias1` and `g_tMetalness`.

```text
angleBase = max(0.001,
                angleScale * abs(dot(viewAxis, shadingNormal)) + angleBias)
angleTerm = pow(angleBase, abs(anglePower))
opacity = opacityScale * vertexColor.a * g_tColor.a * angleTerm
```

The alpha-test threshold and distance boost are absent. A zero vertex alpha
skips lighting and writes zero.

## Render and output contract

The normalized pixel blend state uses premultiplied-alpha composition:

```text
color: SrcBlend = One, DestBlend = InvSrcAlpha, BlendOp = Add
alpha: SrcBlend = InvDestAlpha, DestBlend = One, BlendOp = Add
```

The explicit depth-stencil descriptor reported for ordinary dynamic 0 is
absent on translucent dynamic 0. This report does not infer driver defaults
from an absent descriptor.

The fragment program writes two color targets. A comparison against scene
depth sends the premultiplied result to one target and zero to the other, with
the routing reversed for the second target:

```text
premul.rgb = litAndFoggedColor * opacity
premul.a = isAdditive ? 0 : opacity

output0 = depthSide ? 0 : premul
output1 = depthSide ? premul : 0
```

## Lighting, fog and rim boundary

Surviving pixels retain the opaque material preparation, NPR bounce, direct
diffuse/specular, rim and environment specular topology. Basic translucent
adds engine fog/sky composition before opacity multiplication. The
depth-occluded rim controls and depth sampling branch are absent; the ordinary
non-depth rim remains.

No scene-color refraction term was found in this family. Refraction belongs to
later glass/advanced-translucency families.

## Painter consequence

A compatible preview needs the split opacity/metalness channel layout,
angle-corrected opacity and premultiplied output. The engine's dual-target depth
routing and volumetric fog are unavailable in a Painter surface shader and
must remain explicit pipeline gaps.

## Evidence classification

- **Confirmed by static retail evidence:** separate metalness; opacity equation;
  absence of threshold/distance boost; shared NPR lighting; fog/sky composition;
  dual-target depth routing; absence of depth-rim and refraction branches.
- **Confirmed by pipeline/runtime:** current VCS identity, combo axes, 16/8
  topology, bytecode identity/size and normalized blend factors.
- **Calibrated approximation:** none introduced by this stage.
- **Blocked/unresolved:** live opacity/angle values, exact dual-target consumer,
  unnamed fog/depth buffer fields and Painter equivalents.

Glass is documented in `GLASS_DELTA.md`; advanced translucency follows.
