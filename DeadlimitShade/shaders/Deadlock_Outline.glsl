// Deadlimit Shade — preview-only inverted-hull shell shader for Substance 3D Painter.
//
// The preview mesh generator owns shell geometry and Outline Width.
// Expected prototype contract:
//   - duplicate preview shell
//   - offset shell vertices outward
//   - reverse shell triangle winding
//   - assign preview-only material: __deadlimit_outline
//
// This shader only shades the already-generated shell.

//: state cull_face on
//: state blend none

//: param custom {
//:   "default": 0,
//:   "label": "Outline Color",
//:   "widget": "color",
//:   "group": "Deadlimit Outline"
//: }
uniform vec3 dl_outline_color;

void shade(V2F inputs)
{
  albedoOutput(vec3(0.0));
  diffuseShadingOutput(vec3(0.0));
  specularShadingOutput(vec3(0.0));
  emissiveColorOutput(dl_outline_color);
  sssCoefficientsOutput(vec4(0.0));
}
