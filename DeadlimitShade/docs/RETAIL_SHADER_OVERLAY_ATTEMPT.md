# Research record — disposable retail-shader overlay

The research snapshot rechecked Steam app `1422450` before launching. The manifest still
reports buildid `24882156`. ValvePak read
`shaders/vfx/pbr_vulkan_60_ps.vcs` from the installed retail
`shaders_vulkan_dir.vpk` as 9,230,833 bytes with SHA-256
`eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930`.
The same read against the copied overlay VPK returned the same length and hash.

The successful isolation attempt used a disposable physical host root under
the Windows temporary directory. `bin_server` was copied into the overlay's
`game/bin_tools` location so Windows reported the running executable inside the
temporary root. Private `game/citadel`, `game/core`, `content/_toolsautosave`,
and `content/citadel/addons/luaunlocker` directories held writable state. Large
unchanged CSDK content directories were exposed through read-only junctions.
The complete retail pair, `shaders_vulkan_dir.vpk` and
`shaders_vulkan_000.vpk`, was copied into the private `game/citadel` directory.
Both VPK files matched their retail SHA-256 values. Neither installed tree was
modified.

The first direct `bin_tools` host reached
`CSchemaSystem::VerifySchemaBindingConsistency()` and aborted before renderer
initialisation because its `animationsystem.dll` and `particles.dll` reported a
`particleslib` schema member-count mismatch. `bin` has the same DLL hashes. A
second junction-only host reached Vulkan but resolved its executable to the
installed CSDK path, so it was rejected as identity evidence. The physical
overlay removed that ambiguity.

# RenderDoc Vulkan Layer Registration

The official RenderDoc v1.46 x64 portable build was used. Its pre-change
`renderdoccmd vulkanlayer --explain` state reported that the portable layer was
not registered and required system registration. The layer was registered with
the official `renderdoccmd vulkanlayer --register --system` mechanism after
normal UAC elevation. A subsequent `--explain` reported that the RenderDoc
Vulkan layer was correctly registered, and the two implicit-layer entries
pointed to this portable build's 64-bit and 32-bit `renderdoc.json` files.

# Runtime Shader Identity Proof

The physical disposable host was launched through the official
`renderdoccmd capture` path with `-vulkan`; no retail process was launched,
attached, injected, hooked, or instrumented. RenderDoc's in-process overlay
reported `Capturing Vulkan`. The crash dump independently records:

```text
Render system: Vulkan
engine_rendersystem_used  -vulkan (from CL)
engine_rendersystem_init  -vulkan
```

The complete shader search slot in private `game/citadel` contained the copied
retail VPK pair whose entry hash is recorded above. During asynchronous pipeline
creation the CSDK engine rejected shaders from that package, including:

```text
CMaterial2::LoadShadersAndSetupModes(1618): Error creating shader pbr.vfx and cannot load error.vfx instead!
CMaterial2::LoadShadersAndSetupModes(1614): Error creating shader pbr.vfx for material materials/models/particle/sphere_hotblue2.vmat!
CMaterial2::LoadShadersAndSetupModes(1614): Error creating shader pbr.vfx for material materials/particle/model/white_trans.vmat!
```

The same failure repeated for `spritecard.vfx`, `projected_decals.vfx`, and
tools shaders. The process then produced an access-violation minidump while its
`Async Pipeline Compile/*` workers were active. RenderDoc saved no frame.
Therefore no actual draw survived on which byte-identical PS selection, static
combo 24, dynamic combo 0, or descriptor layout could be proven. This is the
issue's explicit retail-shader/CSDK incompatibility stop condition.

# Captured NPR Runtime Values

No NPR constants were read. There was no eligible draw after runtime shader
identity verification, and values from the ordinary CSDK shader payload are
excluded by this task. `_Globals_`, `PerViewConstantBufferCitadel_t`, and
`PerViewLightingConstantBufferGpu_t` remain unresolved runtime inputs.

# Stability Across Draws

No two-draw comparison exists because the retail shader package was rejected
before an eligible draw. No value is classified as stable, frame/view/light
dependent, or tool-overridden.

# CSDK/Offline Provenance

The evidence above is limited to a disposable Reduced CSDK12 offline tools host
attempting to use the exact current retail Vulkan package. It proves the Vulkan
launch path and the incompatibility failure. It does not establish a retail
runtime value or permutation/layout match.

# Vulkan Layer Cleanup

Cleanup removed only the two temporary RenderDoc v1.46 implicit-layer values
and preserved the four pre-existing 64-bit and four pre-existing 32-bit OBS,
Epic, and Steam Vulkan-layer values. Portable v1.46
`renderdoccmd vulkanlayer --help` exposes registration and explanation commands
but no unregistration command, and the qrenderdoc source routes its hidden
layer-update option to the same registration API. With explicit approval for
this documented gap, an elevated cleanup script removed the exact 64-bit and
32-bit RenderDoc JSON value names. Direct registry verification returned both
RenderDoc values absent. The official `renderdoccmd vulkanlayer --explain`
command then returned the original pre-task state: this build's RenderDoc layer
is not registered and would require system registration before another Vulkan
capture.

# First GLSL Slice Decision

The first direct-diffuse slice remains **not implementation-ready**. Runtime
retail PS identity, combo/layout, gate values, numeric controls, and stability
evidence could not be obtained. GLSL files remain unchanged.
