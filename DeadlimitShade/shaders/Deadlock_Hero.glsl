// Deadlimit Shade — Deadlock hero material bootstrap for Substance 3D Painter.
//
// This is intentionally a known-valid Painter metal/rough baseline with diagnostics.
// It does NOT yet claim to reproduce Deadlock's character material response.
// Deadlock-specific behavior must be added only after its retail inputs and mechanism
// are captured and validated.

import lib-sss.glsl
import lib-pbr.glsl
import lib-emissive.glsl
import lib-pom.glsl
import lib-utils.glsl

//: param auto channel_basecolor
uniform SamplerSparse basecolor_tex;

//: param auto channel_roughness
uniform SamplerSparse roughness_tex;

//: param auto channel_metallic
uniform SamplerSparse metallic_tex;

//: param auto channel_specularlevel
uniform SamplerSparse specularlevel_tex;

// Experimental only. Disabled by default until the current Deadlock material being
// reconstructed proves how mesh Vertex Color participates in the material.
//: param custom {
//:   "default": 0.0,
//:   "label": "Vertex Color Multiply",
//:   "min": 0.0,
//:   "max": 1.0,
//:   "group": "Deadlimit Experimental",
//:   "description": "Diagnostic/experimental control. 0 leaves Base Color unchanged; 1 multiplies it by mesh Vertex Color RGB."
//: }
uniform float dl_vertex_color_multiply;

//: param custom {
//:   "default": 0,
//:   "label": "Debug View",
//:   "widget": "combobox",
//:   "values": {
//:     "Shaded": 0,
//:     "Base Color": 1,
//:     "Roughness": 2,
//:     "Metallic": 3,
//:     "Ambient Occlusion": 4,
//:     "Vertex Color RGB": 5,
//:     "Vertex Color Alpha": 6
//:   },
//:   "group": "Deadlimit Diagnostics"
//: }
uniform int dl_debug_view;

void dlDebugOutput(vec3 value)
{
  albedoOutput(vec3(0.0));
  diffuseShadingOutput(vec3(0.0));
  specularShadingOutput(vec3(0.0));
  emissiveColorOutput(value);
  sssCoefficientsOutput(vec4(0.0));
}

void shade(V2F inputs)
{
  // Keep the bootstrap path aligned with Painter's current official metal/rough
  // reference shader before adding any Deadlock-specific transformation.
  vec3 viewTS = worldSpaceToTangentSpace(getEyeVec(inputs.position), inputs);
  applyParallaxOffset(inputs, viewTS);

  float roughness = getRoughness(roughness_tex, inputs.sparse_coord);
  vec3 baseColor = getBaseColor(basecolor_tex, inputs.sparse_coord);
  float metallic = getMetallic(metallic_tex, inputs.sparse_coord);
  float specularLevel = getSpecularLevel(specularlevel_tex, inputs.sparse_coord);
  float ambientOcclusion = getAO(inputs.sparse_coord);

  vec3 vertexColor = clamp(inputs.color[0].rgb, vec3(0.0), vec3(1.0));
  float vertexAlpha = clamp(inputs.color[0].a, 0.0, 1.0);

  if (dl_debug_view == 1)
  {
    dlDebugOutput(baseColor);
    return;
  }
  if (dl_debug_view == 2)
  {
    dlDebugOutput(vec3(roughness));
    return;
  }
  if (dl_debug_view == 3)
  {
    dlDebugOutput(vec3(metallic));
    return;
  }
  if (dl_debug_view == 4)
  {
    dlDebugOutput(vec3(ambientOcclusion));
    return;
  }
  if (dl_debug_view == 5)
  {
    dlDebugOutput(vertexColor);
    return;
  }
  if (dl_debug_view == 6)
  {
    dlDebugOutput(vec3(vertexAlpha));
    return;
  }

  baseColor *= mix(vec3(1.0), vertexColor, dl_vertex_color_multiply);

  vec3 diffColor = generateDiffuseColor(baseColor, metallic);
  vec3 specColor = generateSpecularColor(specularLevel, baseColor, metallic);

  float occlusion = ambientOcclusion * getShadowFactor();
  float specOcclusion = specularOcclusionCorrection(occlusion, metallic, roughness);

  LocalVectors vectors = computeLocalFrame(inputs);

  emissiveColorOutput(pbrComputeEmissive(emissive_tex, inputs.sparse_coord));
  albedoOutput(diffColor);
  diffuseShadingOutput(occlusion * envIrradiance(vectors.normal));
  specularShadingOutput(specOcclusion * pbrComputeSpecular(vectors, specColor, roughness));
  sssCoefficientsOutput(getSSSCoefficients(inputs.sparse_coord));
}
