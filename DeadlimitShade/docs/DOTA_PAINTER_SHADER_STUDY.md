# Local Dota Painter shader study

Date: 2026-09-08

## Scope and provenance

Two user-installed Painter shaders were inspected locally as comparative
implementation evidence. They are third-party Dota authoring shaders and do
not establish Deadlock retail behavior. Their source is not copied into this
repository.

| Local file | Size / lines | SHA-256 |
| --- | ---: | --- |
| `assets/shaders/SMM_DOTA2.glsl` | 90,120 bytes / 854 | `02B3704C5186CBE50DAA1A06116CBD7FC1C1764DE1C4EB9A2424FB5906FA8820` |
| `assets/assets/shaders/SMM_DOTA2.glsl` | 93,925 bytes / 1,002 | `DBACEE16DBAC642CBE3E9F41D387D636B20CEC4FB890A6C096DBC410D2B552EC` |

Painter exposed them as two `your_assets/SMM_DOTA2` resource versions. This
resource enumeration is confirmed by pipeline/runtime.

## Useful architecture

The newer installed SMM shader binds Painter's interactive light direction with
`//: param auto main_light`. Shift+RMB updates `uniform_main_light`; the
shader extracts its yaw, combines it with the lighting preset's fixed pitch,
and uses that resolved direction for both diffuse and specular. Deadlimit now
uses the same binding in every lighting-input mode. Diagnostic Neutral replaces
material inputs only, so it can prove the light response while Shift+RMB stays
live.

Both shaders preserve local color in shadow with half-Lambert-shaped direct
diffuse. The newer variant adds a small diffuse-colored ambient floor. They
also keep specular and rim independently masked, use a gloss-driven specular
exponent and limit rim by an upward-facing hemisphere.

The reusable lesson is the separation of material response from lighting
terms. No Dota constant or warp texture is treated as a Deadlock value.

## Deadlimit mapping

- The ambient-floor idea informed a Deadlock-structured Painter bounce
  approximation driven by a fixed environment color, AO and the material-local
  `g_tNprTransmissiveColor` map. Painter panorama IBL is excluded from Shaded.
- The independent Dota rim mask reinforced binding Deadlock's static retail
  `g_tTintMaskRimLightMask` input and diagnosing it separately.
- The hemisphere restriction informed the calibrated upper-hemisphere rim
  guard used in Painter.
- The direct specular term remains independently viewable and materially
  calmer than the earlier diagnostic skeleton.

## Evidence classification

- **confirmed by pipeline/runtime:** both local resources load in Painter; the
  Deadlimit retail samplers bind successfully; shader-native diagnostic views
  can be selected on every active hero material instance; changing Painter
  environment rotation from 145 to 235 degrees changes Deadlimit's signed
  `N dot L` diagnostic.
- **confirmed by static retail evidence:** Ivy material manifests expose
  `g_tTintMaskRimLightMask` and `g_tNprTransmissiveColor`; VMAT flags identify
  the vertex-color-dependent materials.
- **calibrated approximation:** ambient floor, environment-bounce balance,
  direct-diffuse softness, specular shaping, rim intensity and hemisphere
  restriction.
- **blocked/unresolved:** exact Deadlock six-direction probe sampling,
  shadowing, BRDF/warp functions, color grading and post-processing are not
  available through the Painter surface-shader runtime.

## Adoption boundary

This study changed the composition architecture and diagnostics. It provides
no basis for calling the calibrated constants retail runtime values. The
subsequent fixed-scene Painter capture passed the Milestone B lighting-skeleton
gate against the controlled Deadlock Tools reference; broader material and
post-process likeness remains Milestone D work.
