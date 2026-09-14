#!/usr/bin/env python3
"""Focused TЗ 14 contracts; no Painter process or Computer Use required."""

from pathlib import Path


root = Path(__file__).resolve().parents[1]
shader = (root / "shaders" / "Deadlock_Hero.glsl").read_text(encoding="utf-8")
plugin = (root / "painter_plugins" / "deadlimit_apply.py").read_text(encoding="utf-8")

# Environment has one panel selector; viewport background cannot enter the
# selected custom sampler. The captured Default atlas stays implementation-only.
assert 'QtWidgets.QGroupBox("Deadlimit Environment"' in plugin
assert 'self.environment_combo.setObjectName("DeadlimitEnvironment")' in plugin
assert 'self.environment_rotation.setObjectName("DeadlimitEnvironmentRotation")' in plugin
assert 'self.environment_strength.setObjectName("DeadlimitEnvironmentStrength")' in plugin
assert 'parameters["dl_selected_environment"] = resource.identifier().url()' in plugin
assert 'substance_painter.display.set_environment_resource' not in plugin
assert '"usage": "environment"' in shader
assert 'uniform sampler2D dl_selected_environment;' in shader
assert 'textureLod(dl_selected_environment, uv,' in shader
assert 'pbrComputeSpecular(\n    vectors,\n    specularColor,' not in shader
assert '"label": "Captured CSDK Environment", "visible": "false"' in shader
assert 'self._captured_environment_parameters' in plugin
assert 'Load CSDK environment bundle' not in plugin

# A Painter-only grazing gate acts on the recovered rim after view/up ramps.
assert 'float painterDepthOcclusion = 1.0 - smoothstep(0.18, 0.72, nDotV);' in shader
assert 'settings.strength * ambientOcclusion * rimMask * painterDepthOcclusion' in shader
assert '(settings.cutoff - nDotV + 0.1) * 5.0' in shader
assert 'lightingBeforeRim * sample.steppedRim' in shader


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


assert 1.0 - smoothstep(0.18, 0.72, 0.0) == 1.0
assert 1.0 - smoothstep(0.18, 0.72, 0.9) == 0.0
assert 0.0 < 1.0 - smoothstep(0.18, 0.72, 0.45) < 1.0

# Presence is a Texture Set-level decision. User1=0 must not sample the retail
# AO beneath it, while no User1 keeps the documented retail/Painter/white path.
assert 'RIM_MASK_CHANNEL = substance_painter.textureset.ChannelType.User0' in plugin
assert 'ARTISTIC_AO_CHANNEL = substance_painter.textureset.ChannelType.User1' in plugin
assert 'dl_artistic_ao_present: artisticAoTextureSets.indexOf(textureSetName) >= 0' in plugin
assert 'float ambientOcclusion = dl_artistic_ao_present' in shader
assert 'textureSparse(dl_artistic_ao_tex, inputs.sparse_coord).r' in shader
assert ': ambientOcclusionFallback;' in shader
assert 'float baselineOcclusion = ambientOcclusionFallback * shadowFactor;' in shader
assert 'float occlusion = ambientOcclusion * shadowFactor;' in shader
assert 'dlEvaluateNprDiffuseResponse(\n    baseColor,\n    ambientOcclusion,' in shader
assert 'rimMask,\n    ambientOcclusion,\n    lightingBeforeRim,' in shader
assert 'dlDebugOutput(vec3(ambientOcclusion));' in shader


def resolve_ao(user1_exists, user1_value, retail, painter):
    return user1_value if user1_exists else (
        retail if retail is not None else painter if painter is not None else 1.0)


assert resolve_ao(True, 0.0, 0.7, 0.9) == 0.0
assert resolve_ao(True, 1.0, 0.7, 0.9) == 1.0
assert resolve_ao(False, None, 0.7, 0.9) == 0.7
assert resolve_ao(False, None, None, 0.9) == 0.9
assert resolve_ao(False, None, None, None) == 1.0


def gate_terms(ao):
    # Direct is independent; the other weights carry the resolved AO.
    direct = 0.6
    rim = 0.5 * ao
    bounce_weight = 0.4 * ao
    spec_occlusion = ao * 0.8
    return direct, rim, bounce_weight, spec_occlusion


neutral = gate_terms(resolve_ao(True, 1.0, 0.7, 0.9))
dark = gate_terms(resolve_ao(True, 0.0, 0.7, 0.9))
assert neutral[0] == dark[0]
assert all(a != b for a, b in zip(neutral[1:], dark[1:]))

direct = shader.split('DLDirectDiffuseSample dlEvaluateDirectDiffuse(', 1)[1].split(
    'DLDirectSpecularSample dlEvaluateDirectSpecular(', 1)[0]
assert 'ambientOcclusion' not in direct

assert 'ARTISTIC_AO_BASE_LABEL = "Deadlimit Artistic AO Base"' in plugin
assert 'painter_layerstack.insert_fill(position)' in plugin
assert 'fill.active_channels = {ARTISTIC_AO_CHANNEL}' in plugin
assert 'texture_set.get_mesh_map_resource(' in plugin
assert 'substance_painter.textureset.MeshMapUsage.AO' in plugin
assert 'painter_colormanagement.Color(1.0, 1.0, 1.0)' in plugin
assert 'if painter_layerstack is None:' in plugin
assert '"fileName": "$textureSet_Deadlimit_Artistic_AO"' in plugin

print("Deadlimit Preview stability contracts passed.")
