#!/usr/bin/env python3
"""Focused source-resolution and consumer-topology contract for Artistic AO."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def resolve_artistic_ao(authored, retail, painter):
    fallback = retail if retail is not None else painter
    if fallback is None:
        fallback = 1.0
    return authored if authored is not None else fallback


# Required source precedence and neutral terminal fallback.
assert resolve_artistic_ao(0.25, 0.5, 0.75) == 0.25
assert resolve_artistic_ao(None, 0.5, 0.75) == 0.5
assert resolve_artistic_ao(None, None, 0.75) == 0.75
assert resolve_artistic_ao(None, None, None) == 1.0
assert resolve_artistic_ao(1.0, 0.2, 0.3) == 1.0
assert resolve_artistic_ao(0.0, 0.8, 0.9) == 0.0

shader = (ROOT / "shaders" / "Deadlock_Hero.glsl").read_text(encoding="utf-8")

assert "//: param auto channel_user0" in shader
assert "uniform SamplerSparse dl_rim_mask_tex;" in shader
assert "//: param auto channel_user1" in shader
assert "uniform SamplerSparse dl_artistic_ao_tex;" in shader
assert "float painterAmbientOcclusion = getAO(inputs.sparse_coord, true, true);" in shader
assert "float ambientOcclusionFallback = painterAmbientOcclusion;" in shader
assert "ambientOcclusionFallback = texture(" in shader
assert "uniform bool dl_artistic_ao_present;" in shader
assert "float ambientOcclusion = dl_artistic_ao_present" in shader
assert "textureSparse(dl_artistic_ao_tex, inputs.sparse_coord).r" in shader
assert ": ambientOcclusionFallback;" in shader

# The resolved producer must feed the recovered Deadlock consumer topology.
assert "dlEvaluateBounce(\n    vectors.normal,\n    viewDirection,\n    ambientOcclusion," in shader
assert "float occlusion = ambientOcclusion * shadowFactor;" in shader
assert "dlEvaluateNprDiffuseResponse(\n    baseColor,\n    ambientOcclusion," in shader
assert "rimMask,\n    ambientOcclusion,\n    lightingBeforeRim," in shader
assert "dlDebugOutput(vec3(ambientOcclusion));" in shader

# Direct diffuse stays outside authored AO, and the comparison PBR path keeps
# its previous fallback AO instead of consuming User1.
direct_start = shader.index("DLDirectDiffuseSample dlEvaluateDirectDiffuse(")
direct_end = shader.index("DLDirectSpecularSample dlEvaluateDirectSpecular(")
assert "ambientOcclusion" not in shader[direct_start:direct_end]
assert "float baselineOcclusion = ambientOcclusionFallback * shadowFactor;" in shader
assert "diffColor * (baselineOcclusion * envIrradiance(vectors.normal))" in shader
assert "baselineSpecOcclusion * pbrComputeSpecular" in shader
assert "baseColor *= ambientOcclusion" not in shader
assert "directDiffuseLighting *= ambientOcclusion" not in shader

print("Deadlimit Artistic AO source and consumer topology smoke passed.")
