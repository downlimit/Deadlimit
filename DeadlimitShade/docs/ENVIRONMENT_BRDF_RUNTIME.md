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
