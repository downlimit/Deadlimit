# Environment BRDF runtime decomposition — 2026-09-12

Scope: Reduced CSDK Ivy capture event 791, shader ResourceId::6491.
This does not establish current retail runtime identity. No captured assets
belong in Git. The numerical reference is `tools/deadlock_environment_reference.py`.

## Instruction map

| Instructions | Behavior |
| --- | --- |
| 244-245, 430 | Engine-prefiltered cubemap LOD = sqrt(roughness)*5 in this capture |
| 647-648 | Normalization denominator = dot(float4(normal,1), blended probe coefficients) |
| 834-838 | LUT UV = (roughness,sqrt(1-NdotV))*63/64 + 0.5/64; array layer 1 |
| 839-840 | Single-scattering response E = LUT.r + F0*(LUT.g-LUT.r) |
| 841-847 | Radiance scale = min(max(roughness*A+B,1), probe luminance/denominator) |
| 849-862 | Single/multiple-scattering composition, coupled specular/diffuse energy |

Captured cb4[36].xyzw uint flags are [0,0,1,1]: normalization and multiple
scattering are enabled. Captured A,B = [34.4444465637207,-2.444446563720703].
These values are confirmed by pipeline/runtime for this Reduced CSDK draw.

PS t3 is ResourceId::1826: 64x64, three layers, one mip, RGBA16 storage.
The DDS export SHA256 is
`277ec7f5afcabd5d1858269ec7a07256cabc14d6538a7c237bec5a3b04fbeef7`.
The numerical reader interprets the captured float-view data as half floats.

For LUT channels (a,b), the recovered instruction boundary is:

```
E = a + F0*(b-a)
Favg = F0 + (1-F0)*0.047619
missing = 1-b
multiple = missing * E*Favg / (1-missing*Favg)
specular = normalizedPrefilteredRadiance*E + ordinaryProbe*multiple
diffuseProbe = ordinaryProbe*(1-E-multiple)
```

The literal 0.047619 is preserved from instruction 852. Downstream NPR and
AO operations still apply; these equations alone are not a final shaded pixel.

## Reproduce the evidence

PowerShell 7, from DeadlimitShade, using an existing RenderDoc installation:

```powershell
./tools/Export-DeadlockEnvironmentCapture.ps1 `
  -Capture '<absolute path to the RDC>' `
  -RenderDocExe '<absolute path to qrenderdoc.exe>' `
  -OutputDirectory '<new local scratch directory>'
python ./tests/environment-reference-smoke.py '<output directory>/brdf-array.dds'
```

Defaults select this capture's IDs. Other captures require binding inspection
and matching event/resource arguments; the exporter does not infer bindings.
It replays offline, exports the LUT and cube zero's six faces at all seven
mips, records hashes, and closes its owned process before opening an unused
main window. Existing output directories are rejected. It needs no game
injection or Computer Use. HDR face exports have RGBE precision limits relative
to the original BC6 values.

Exporting the complete 340-cube allocation did not finish within the bounded
timeout. The helper was terminated. The selected-cube path then succeeded;
no RenderDoc processes remained. Successful evidence is local under
`.scratch/runtime-environment-cube0-20260912`.

## Validation and next boundary

Five numerical tests passed: UV mapping, disabled branch, synthetic furnace
identity, perfect-reflector recovery, and normalization limits. Twenty-five
actual LUT samples passed finite/range and algebraic energy checks. These
checks do not prove pixel parity or correct texture filtering.

Painter still uses sampled PBR integration and a panorama derived from cube
base level. It lacks the captured prefiltered mip contents, LUT transfer,
probe normalization and coupled energy response. The next integration must
replace this path together, then feed the existing NPR/AO composition.
Adding multiple scattering over the current PBR result would mix two models.

The installed shader and project are unchanged during this decomposition.
No new viewport PASS or full game parity is claimed.

## Experimental Painter integration — 2026-09-13

An optional `dl_captured_environment` path now consumes a cube mip atlas and
the captured BRDF LUT. It replaces the call to Painter PBR for this branch,
applies the captured normalization and multiple-scattering algebra, and adjusts
the ordinary probe's diffuse energy before NPR interpolation. The existing
Painter path remains the default until the new path passes visual inspection.

Prepare a bundle after the capture export:

```powershell
python ./tools/prepare_captured_environment.py '<export directory>' '<new bundle directory>'
python ./tests/captured-environment-bundle-smoke.py '<export directory>' '<bundle directory>'
```

This preparation requires NumPy and OpenCV. In an open Painter project, use
Preview as Deadlock, then **Load CSDK environment bundle…**, selecting the
generated `environment.json`. The loader embeds content-addressed resources
in the project; repeating Apply preserves an enabled bundle. The file is a
local proof bundle, not a distributable retail asset. Shader settings expose
**Captured CSDK Environment** to return to the existing Painter fallback.

Confirmed in this iteration: all 42 packed mip faces retain their decoded
source pixels exactly; LUT RG UNORM16 error <= 0.5/65535; five mapped shader
instances retain resource bindings after Apply. The new environment function
compiles in a standalone GLSL 330 harness with Painter types/function stubs.
This harness does not validate the full Painter program or GPU output.

At this initial integration point, reflected sampling
direction omitted Source 2's dominant-direction correction and box
projection; native cross-face filtering is replaced by edge clamp; probe-zero
normalization coefficients are frozen; specular occlusion still uses the
previous Painter substitute. These must not be described as exact runtime
parity. Only the listed captured values and algebra are evidence-backed.

Visual gate was not reached: Computer Use returned foreground Dota content for
the selected Painter window, including after activation. No such frame counts
as Painter evidence. Computer Use was reset; the project was returned to
Material / Retail, Shaded, captured mode disabled, without saving this experiment
over the existing SPP. No commit was made. Next action is a real neutral-input
viewport check of the loaded branch, followed by material comparison if it passes.

## Instruction-trace validation — 2026-09-13

The subsequent Painter atlas-connectivity check is recorded in the handoff.
The following work uses offline capture replay and does not change the open SPP.

`Export-DeadlockPixelTrace.ps1` now reproduces pixel traces using a short-lived,
hidden RenderDoc helper. Its worker exits before opening an unused Qt window.
It rejects an existing output directory and incomplete/empty traces. Example:

```powershell
./tools/Export-DeadlockPixelTrace.ps1 -Capture '<local capture.rdc>' `
  -RenderDocExe '<qrenderdoc.exe>' -OutputDirectory '<new local directory>' `
  -EventId 791 -Pixels '660,290','580,200'
python ./tests/captured-pixel-reference-smoke.py --lut '<export>/brdf-array.dds' `
  '<traces>/trace-660-290.json' '<traces>/trace-580-200.json'
```

These event/pixel coordinates are specific to the Reduced CSDK Ivy capture.
The repeatable export identifies pixel shader `ResourceId::6491` and records
2129 / 2068 execution states. RenderDoc interprets arithmetic and uses replay
for resource operations; this evidence is not native GPU register readback.

CPU reference comparisons against both traces:

| Boundary | Maximum absolute error |
| --- | ---: |
| Dominant reflection, ISA 249–258 | 7.36e-8 |
| Box projection, ISA 395–404 | 5.80e-8 |
| Roughness blend after projection, ISA 422–423 | 2.61e-8 |
| LUT UV mapping, ISA 834–837 | 5.02e-9 |
| Radiance normalization | 8.64e-8 |
| Multiple scattering, specular | 1.72e-9 |
| Multiple scattering, diffuse | 4.10e-8 |

Bounds above include the initial traces and a second export with the checked-in
worker. Small replay differences remain within these bounds; byte-identical
trace files are not a validation requirement.

Sampler s1 is linear min/mag, point mip, clamp edge. At these two coordinates,
half-rounded CPU bilinear sampling of the captured DDS matches the traced LUT
RG exactly. This is an observed sampling boundary for two inputs, not proof of
all-coordinate hardware filtering equivalence or Painter LUT sampling parity.

The verified dominant-reflection formula is now transferred into the captured
environment GLSL branch. Its roughness-dependent direction blends the reflection
vector toward the shading normal before normalization. The source compiles in
the GLSL 330 harness; this latest change is not installed or visually accepted.

Evidence classification:

- **confirmed by pipeline/runtime:** listed boundaries for the selected Reduced
  CSDK draw, sampler descriptor, captured LUT values. Current retail equivalence
  remains unconfirmed.
- **confirmed by static retail evidence:** no new claims in this iteration.
- **calibrated approximation:** existing fallback/profile approximations remain;
  no new fitted coefficient was added.
- **blocked/unresolved:** model-to-captured-world coordinate mapping required for
  box projection in Painter; probe selection/weights across the model; native
  cubemap cross-face filtering; exact specular occlusion; final display transform.
  RGBE atlas export and UNORM16 LUT transfer retain their documented precision
  limits. Full image parity and cross-project reproduction remain unproved.

## Screen visibility and composition boundary — 2026-09-13

Continuing after checkpoint `e8f7f5d`, the CSDK coordinate chain was identified:
ISA 104 reconstructs world position as `v2 + cb1[19].xyz`; ISA 374–376
transforms that position with the selected probe's three affine rows. Painter's
installed shader documentation exposes `scene_original_radius` and explicitly
describes scene normalization. Consequently, directly using `inputs.position`
with CSDK's box bounds is unjustified; original scale and translation are still
unresolved. Box projection remains unwired pending coordinate evidence.

ISA 1732–1734 applies `min(screenSpecularVisibility,1)` and global visibility
to environment specular. The former comes from the captured screen-buffer path
(ISA 208–231), independently of the material AO polynomial used for bounce.
The CPU reference for this boundary matches both pixel traces exactly.

Removed Painter's material-AO/metalness/roughness specular-occlusion substitute
from the captured environment branch. Missing screen and global visibility use
neutral one in this preview path: **blocked/unresolved input fallback**, not
confirmed runtime values. The standard Painter fallback is unchanged. This
does not remove material AO from the recovered diffuse/bounce path.

Added a CPU reference for linear opaque composition (ISA 1731–1742), preserving
the separate placement of diffuse AO, metalness, rim, emission, direct specular
and environment visibility. Maximum error against the two traces is 1.01e-7.
This validates the summation boundary with traced inputs; it does not validate
all input producers, post-processing, or Painter pixel output.

Updated source installed in the user's Painter shader/plugin folders with
`-SkipOpenDock`. Painter was not running; no project was opened or saved and no
viewport PASS is claimed. Ten unit/contract tests and the isolated GLSL harness
pass. Changes after the checkpoint remain uncommitted.

## Ordinary direct-specular two-material validation — 2026-09-13

Scope: the same Reduced CSDK capture and pixel shader `ResourceId::6491`, draw
event 804. This draw was selected after a bounded scan showed that event 791's
covered candidates were dielectric. The event-804 traces are local evidence at
`.scratch/direct-spec-reference-event804-20260913` and remain out of Git.

Material data comes from event-804 `t13` (`ResourceId::7610`, BC7, 1024x1024)
and `t16` (`ResourceId::7631`, BC7, 1024x1024). ISA 23–67 samples/prepares the
color/metalness and normal/roughness inputs; ISA 139–160 finishes prepared color,
constructs F0, and constructs the multiple-scattering compensation. The material
F0 equation executed at ISA 150–155 is:

```text
authoredVisibility = saturate(max(baseColor) * 25)
F0 = mix(0.04, baseColor, metalness) * authoredVisibility
compensation = 1 + 2 * roughness^4 * NdotV * F0
```

The ordinary sun BRDF executes at ISA 1153–1188. ISA 86–89 supplies the
normalized view direction, ISA 1159–1162 constructs the half vector, and ISA
1163–1188 evaluates the following traced terms. Values below are
**confirmed by pipeline/runtime** for these exact event/pixel identities.

| Value | dielectric `(565,99)` | metallic `(602,140)` |
| --- | ---: | ---: |
| prepared base color | `[0.05664063, 0.02922058, 0.04135132]` | `[0.81591809, 0.25952151, 0.15625004]` |
| metalness | `0` | `0.95068282` |
| roughness | `0.71392387` | `0.76945144` |
| normal | `[0.64796305, 0.42351583, -0.63307029]` | `[0.58288836, 0.33196920, 0.74164510]` |
| view direction | `[0.59019670, 0.67222235, 0.44697311]` | `[0.59878644, 0.64858128, 0.46989054]` |
| light direction | `[0.49446416, 0.41070834, 0.76604432]` | same |
| half vector | `[0.55491434, 0.55402918, 0.62058178]` | `[0.55754684, 0.54022708, 0.63031438]` |
| `NdotL` | `0.009376109` | `0.99269295` |
| `NdotV` | `0.38415706` | `0.91282666` |
| `NdotH` | `0.20133221` | `0.97179598` |
| `LdotH` | `0.97732282` | `0.98041153` |
| F0 | `[0.04, 0.04, 0.04]` | `[0.77765203, 0.24869534, 0.15051693]` |
| `roughness^2` | `0.50968729` | `0.59205552` |
| `roughness^4` | `0.25978114` | `0.35052974` |
| Fresnel scalar `(1-LdotH)^5` | `5.99716e-9` | `2.88405e-9` |
| distribution `D` | `0.27610114` | `2.34472714` |
| visibility/geometry `V` | `2.44964786` | `0.26773811` |
| raw lobe `D*V*NdotL` | `0.006341536` | `0.62318565` |
| material/specular tint | `[0.040000005, 0.040000005, 0.040000005]` | `[0.77765203, 0.24869535, 0.15051693]` |
| compensation | `[1.00798368, 1.00798368, 1.00798368]` | `[1.49765503, 1.15915155, 1.09632266]` |
| BRDF before radiance/visibility | `[0.000255687, 0.000255687, 0.000255687]` | `[0.72579595, 0.17964921, 0.10283505]` |

The light direction is `PerViewLightingConstantBufferGpu_t` `cb3[19].xyz`.
The unoccluded RGB radiance is `cb3[20].rgb = [1.6,1.6,1.6]`. Sun visibility
is generated at ISA 965–1149: cascade selection and transforms use the tail of
`cb3`, 16x4 stochastic gathers read PS `t6` (`ResourceId::6650`,
`shadow_atlas_1.vtex`, R32 typeless, 6144x4608) through `s1`, and the selected
cascades are blended into `r8.y` at ISA 1149. Visibility is `0.87788045` at the
dielectric pixel and `1` at the metallic pixel. ISA 1156 multiplies that value
by `cb3[20].rgb`, producing visible radiance `[1.40460873]*3` and `[1.6]*3`.

ISA 1243 multiplies the ordinary BRDF by visible radiance. ISA 1290–1292 moves
the result into the direct-specular accumulator `r27`. The clustered/barn-light
loop at ISA 1298–1717 does not change `r27` at either tested pixel, proving one
contributing direct light at this boundary. ISA 1741 adds `r27 * r7.w` to the
opaque composition; captured global visibility `r7.w` is one for both pixels.

| Boundary | dielectric `(565,99)` | metallic `(602,140)` |
| --- | ---: | ---: |
| direct specular before composition | `[0.000359140]*3` | `[1.16127360, 0.28743875, 0.16453609]` |
| final `r27` | `[0.000359140]*3` | `[1.16127360, 0.28743875, 0.16453609]` |
| actual add into final composition | `[0.000359140]*3` | `[1.16127364, 0.28743876, 0.16453610]` |
| BRDF max absolute CPU/trace error | `2.40e-12` | `3.33e-8` |
| pre-composition RGB max error | `1.07e-11` | `5.97e-8` |
| final-add RGB max error | `6.40e-10` | `3.73e-8` |
| maximum error across direct checks | `1.93e-7` | `1.34e-7` |

`tests/captured-pixel-reference-smoke.py` now checks material F0, H/dot terms,
roughness powers, Fresnel, distribution, visibility, raw lobe, tint,
compensation, captured sun radiance/shadow multiplication, the unchanged barn
loop accumulator, and the final composition add. Its 2e-6 tolerance covers the
largest observed float-replay difference above.

The current `Deadlock_Hero.glsl` ordinary captured-environment branch has the
same F0 construction, roughness interpretation, Fresnel, distribution,
visibility, `NdotL`, material tint/compensation, light-radiance multiplication,
visibility multiplication and final additive placement. The profile's Z-up to
Painter direction adaptation maps the captured `cb3[19].xyz` to its key-light
direction; `keyLightIntensity * keyLightColor` is `[1.6,1.6,1.6]`, matching
captured `cb3[20].rgb`. Fill intensity is zero, consistent with the lack of a
second contributing direct light at these pixels.

Result: **confirmed by pipeline/runtime** PASS for the Reduced CSDK event-804
main-sun ordinary direct-specular equation, radiance multiplication, shadow
visibility multiplication and `r27` composition at these two material samples.
This does not prove other draws, barn lights, roughness ranges, current retail
runtime identity, or Painter pixel parity. Painter's `getShadowFactor()` has the
same multiplication role, while reproducing the captured shadow-atlas field in
Painter remains **blocked/unresolved**. No GLSL change is justified by this
evidence.
