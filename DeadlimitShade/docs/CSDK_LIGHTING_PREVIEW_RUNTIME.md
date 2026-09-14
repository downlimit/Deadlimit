# CSDK Lighting Preview runtime

`lighting/preview-presets.json` is the static Reduced CSDK catalog consumed by
the Deadlimit Shade panel. The source list is
`content/citadel/toolscenelightrigs.vdata`; each referenced binary tool-scene
map was decoded with the matching `dmxconvert.exe` before extracting lights,
sky, postprocess and exposure entities. The UI keeps the requested short label
`Particle Preview with Postproc` for CSDK's source label `Particle Preview with
Postprocessing`.

All 20 preset definitions are represented. A preset supplies its recovered
directional lights, shadow intent, headlight metadata, environment material,
available panorama source, map/background identity, exposure bounds and
postprocess resource. `+ Headlight` entries inherit the corresponding base
preset and replace only the fields declared by CSDK.

## Painter mapping

- Apply Deadlimit imports the selected CSDK panorama as a project Environment
  and activates it for the viewport. If the panorama source is absent from the
  Reduced CSDK depot, preset mode disables character environment specular so a
  user-selected Painter environment cannot leak into reflections.
- Key/fill/headlight values are sent independently of the character profile.
  Camera-relative headlight uses Painter's world camera direction. The shader
  consumes `getShadowFactor()` only for lights marked as shadow-casting.
- Authored AO uses `getAO(coord, true, true)`, which bypasses Painter's AO
  Intensity control. AO remains limited to the recovered bounce/NPR diffuse,
  rim and specular-occlusion paths.
- Ivy no longer owns a rim enabled flag. Preset rim inputs are separate from
  character data; every recovered preset currently has no rim payload and
  therefore disables the branch without inventing controls.
- Outline color composition follows the recovered CSDK dual-source program:
  source additive is `mix(0, additive, mask)`, framebuffer multiplier is
  `mix(1, tint, mask)`. Painter implements this with `blend add_multiply` and
  `color1Output`. The existing shell width remains unchanged.

## Exact blockers

- `CToolSceneLightRig` and the decoded maps contain no rim enable, color,
  intensity, falloff, wrap or up-ramp producer. Runtime rim globals are absent
  from the supplied static source.
- Probe-volume entities are present, while their baked cubemap/probe textures
  and coefficients are empty. Preset-specific diffuse bounce cannot be
  reconstructed statically and the existing captured Ivy probe remains an
  explicit approximation.
- Painter exposes `getShadowFactor()` and shadow-mask uniforms to shaders, but
  Painter 9.1's supported Python display API has no setter for
  `shadow_mask_enable`. Apply cannot force the viewport mask on; when present,
  it is an approximation of Deadlock's shadow atlas.
- Several sky materials have no source panorama in Reduced CSDK. Their character
  reflections are disabled. `Haze V1` only has a 4:3 presentation image, which
  is not treated as a lat-long environment.
- The referenced `toolscene`, `caldera` and `basepostprocess` resources do not
  expose their response curves in the decoded preset definitions. Preset EV
  bounds are recorded but not applied as a guessed Painter post transform.
- Painter's shader API has no recovered equivalent for CSDK outline depth bias
  `-8`; the existing expanded shell remains the depth-separation approximation.
