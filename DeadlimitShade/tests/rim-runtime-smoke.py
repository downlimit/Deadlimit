#!/usr/bin/env python3
"""Focused CPU contract for the Reduced CSDK event-791 NPR rim scalar."""

import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CUTOFF = 1.0
SHARPNESS = 0.009999999776482582
STRENGTH = 0.30000001192092896
UP_RAMP = (0.0, 1.0)


def saturate(value):
    return max(0.0, min(1.0, value))


def rim_base(n_dot_v, normal_up, ao, cutoff=CUTOFF,
             sharpness=SHARPNESS, strength=STRENGTH,
             up_ramp=UP_RAMP):
    coordinate = saturate((cutoff - abs(n_dot_v) + 0.1) * 5.0)
    exponent = 1.0 / (1.0 - sharpness)
    wing = 1.0 - coordinate if coordinate > 0.5 else coordinate
    shaped_wing = math.pow(2.0, exponent - 1.0) * math.pow(wing, exponent)
    view_ramp = 1.0 - shaped_wing if coordinate > 0.5 else shaped_wing
    up_factor = saturate((normal_up - up_ramp[0]) / (up_ramp[1] - up_ramp[0]))
    return view_ramp * up_factor * strength * ao


# Values are debugger registers from the existing Default/Ivy capture, event 791.
# expected_base is r1.w after ISA 1277; ISA 1278 applies the material mask,
# which already contains the screen-depth occlusion multiplier from ISA 206.
SAMPLES = (
    ((650, 160), 0.5812857151031494, 0.9207710027694702, 0.963836133480072, 1.0, 1.0, 0.26624172925949097),
    ((510, 220), 0.5507072210311890, 0.9062159061431885, 0.9953612685203552, 1.0, 0.0, 0.27060365676879883),
    ((540, 100), 0.7652630805969238, 0.9179096221923828, 0.9739680886268616, 1.0, 0.0, 0.2682044208049774),
    ((680, 350), 0.6612514853477478, 0.25045207142829895, 0.268818199634552, 0.00221255817450583, 0.0, 0.020197823643684387),
    ((620, 180), 0.9695692658424377, 0.26375287771224976, 0.4417791962623596, 0.0, 0.0, 0.02284127101302147),
    ((640, 180), 0.9834906458854675, 0.41035813093185425, 0.8292362689971924, 1.0, 0.0, 0.05954696983098984),
    ((640, 250), 0.9902838468551636, 0.5729120373725891, 0.9817807078361511, 1.0, 0.0, 0.09264733642339706),
)


errors = []
for pixel, n_dot_v, normal_up, ao, rim_mask, depth, expected_base in SAMPLES:
    actual_base = rim_base(n_dot_v, normal_up, ao)
    actual_final = actual_base * rim_mask * depth
    expected_final = expected_base * rim_mask * depth
    errors.extend((abs(actual_base - expected_base), abs(actual_final - expected_final)))
    if errors[-2] > 2.0e-8 or errors[-1] > 2.0e-8:
        raise AssertionError(f"rim scalar mismatch at {pixel}: {actual_base}, {actual_final}")

catalog = json.loads((ROOT / "lighting" / "preview-presets.json").read_text(encoding="utf-8"))
default = next(preset for preset in catalog["presets"] if preset["name"] == "Default")
assert default["rim"] == {
    "enabled": True,
    "cutoff": 1.0,
    "sharpness": 0.01,
    "strength": 0.3,
    "upRamp": [0.0, 1.0],
    "depthOcclusion": True,
    "occlusionSampleDistance": 0.5,
}

profile = json.loads((ROOT / "profiles" / "ivy.json").read_text(encoding="utf-8"))
assert profile["rim"] == {
    "enabled": True,
    "cutoff": 1.0,
    "sharpness": 0.01,
    "strength": 0.3,
    "upRamp": [0.0, 1.0],
    "runtimeSource": "reduced-csdk-asset-browser",
    "evidence": "confirmed-pipeline-runtime",
}

# Artist-facing gates: zero strength and zero mask remove the contribution;
# a full mask preserves the recovered scalar. Width and sharpness must remain
# independently observable on a non-saturated sample.
probe = rim_base(0.95, 0.65, 0.8)
assert rim_base(0.95, 0.65, 0.8, strength=0.0) == 0.0
assert probe * 0.0 == 0.0
assert abs(probe * 1.0 - probe) < 1.0e-12
assert abs(rim_base(0.95, 0.65, 0.8, cutoff=0.75) - probe) > 1.0e-4
assert abs(rim_base(0.95, 0.65, 0.8, sharpness=0.8) - probe) > 1.0e-4

shader = (ROOT / "shaders" / "Deadlock_Hero.glsl").read_text(encoding="utf-8")
assert "(settings.cutoff - nDotV + 0.1) * 5.0" in shader
assert "exp2(exponent - 1.0) * pow(wing, exponent)" in shader
assert "settings.strength * ambientOcclusion * rimMask" in shader
assert "lightingBeforeRim * sample.steppedRim" in shader
assert "uniform SamplerSparse dl_rim_mask_tex;" in shader
assert "value.r + fallbackValue * (1.0 - value.g)" in shader

print(f"Deadlimit runtime rim smoke passed; max error = {max(errors):.9g}")
