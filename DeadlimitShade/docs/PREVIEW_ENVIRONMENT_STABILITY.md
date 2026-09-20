# Deadlimit Shade — Preview environment stability, rim, and Artistic AO

The `Deadlimit Environment` panel is the sole artist selector for the shader's
reflection source, yaw and strength. Lighting Preset initializes these three
values; a subsequent manual edit stays in force through repeated Preview.
Painter Display Environment remains a viewport-background control. The shader
samples a custom `usage: "environment"` panorama for CSDK images and never
reads the Display Environment in its Deadlimit reflection branch. The explicit
`Painter PBR Baseline` diagnostic still uses Painter's own PBR environment.

Available CSDK panoramas come from `lighting/preview-presets.json` and local
Reduced CSDK content. `Default` can use the captured mip atlas + BRDF LUT when
`DEADLIMIT_CAPTURED_ENVIRONMENT_BUNDLE` points to a prepared `environment.json`
or an internal caller supplies the bundle. That resource is never committed.
Without a bundle, Default uses the selected CSDK panorama. The panorama
BRDF/Fresnel path is a **calibrated Painter approximation**, not a recovered
Source 2 probe evaluation. The captured backend remains a more accurate
runtime-backed path, with its documented unresolved screen/probe limitations.

The recovered rim view/up ramp, strength, authored AO, User0/retail mask and
direct-plus-bounce coloring are retained. A separate Painter-only
`1 - smoothstep(0.18, 0.72, abs(N·V))` factor replaces the unavailable
screen-depth suppression. These two thresholds are a **calibrated
approximation**, not retail runtime values. It makes face-on normals receive
zero rim and concentrates the contribution near grazing angles.

User1 is authoritative for Deadlimit authored AO whenever the Texture Set has
that channel. No per-pixel fallback occurs inside an existing User1 channel;
when absent, the source order remains retail AO, Painter AO, white. Direct
diffuse and Base Color stay independent of User1. On Painter 10+, `Create
Artistic AO` uses Adobe's `layerstack` API to insert a bottom Fill Layer named
`Deadlimit Artistic AO Base`, activates only User1, and connects the Texture
Set's baked AO mesh map or uniform white. Existing base layers and paint are
preserved. [Adobe's API history](https://experienceleague.adobe.com/en/docs/substance-3d-dev/painter-python/api/api-overview)
places layerstack creation in Painter 10.0 (API 0.3.0). The installed Painter
9.1 has API 0.2.11. TЗ 15 verified live through Painter's remote scripting
endpoint: `alg.layerstack` is undefined, and the exported `alg` namespaces
contain no layerstack editor. `alg.ui` exposes button/menu hooks, with no
supported source/channel/bottom-stack editing API. On 9.1 the button now
creates User1 and makes it authoritative, then instructs the artist to add a
Fill Layer and assign baked AO manually. No other channel is changed. Full
automatic AO initialization on this host requires Painter 10+.

No viewport visual PASS was performed for this checkpoint. Static
tests verify the source/consumer topology and parameter wiring; they do not
establish a visual match to Deadlock.
