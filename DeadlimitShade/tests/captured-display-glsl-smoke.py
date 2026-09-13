"""Bind the Hero GLSL event-1531 constants to two captured pixel samples.

This is a source/CPU regression, not a Painter GPU or viewport test. Bloom is
fixed to zero in the shader, so the capture comparison includes that error.
"""
import math
import re
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = (root / "shaders" / "Deadlock_Hero.glsl").read_text(encoding="utf-8")
sys.path.insert(0, str(root / "tools"))
from deadlock_display_reference import captured_display_tonemap

assert "#define DISABLE_FRAMEBUFFER_SRGB_CONVERSION" in source
assert 'uniform bool dl_captured_display;' in source
assert 'diffuseShadingOutput(dl_captured_display\n    ? dlCapturedDisplay(linearOpaque)' in source
assert 'dlDebugOutput' in source and 'emissiveColorOutput(dlDisplaySrgbEncode(value))' in source
assert 'FXAA' not in source.upper()

curve = source.split('float dlCapturedDisplayChannel(float linearValue)', 1)[1].split(
    'vec3 dlCapturedDisplay(vec3 linearColor)', 1)[0]
encode = source.split('float dlDisplaySrgbEncode(float linearValue)', 1)[1].split(
    'vec3 dlDisplaySrgbEncode(vec3 linearColor)', 1)[0]


def glsl_constant(name):
    match = re.search(r'const float ' + re.escape(name) + r'\s*=\s*([0-9.]+)\s*;', curve)
    assert match, f'missing GLSL constant {name}'
    return float(match.group(1))


values = {name: glsl_constant(name) for name in (
    'exposure', 'preCurveScale', 'clampMax', 'bloom',
    'a', 'b', 'c', 'd', 'e', 'whiteScale')}
assert values['bloom'] == 0.0
assert 'float exposed = max(linearValue, 0.0) * exposure;' in curve
assert 'float x = min(preCurveScale * (exposed + bloom), clampMax);' in curve
assert 'return dlDisplaySrgbEncode(mapped);' in curve
assert 'value <= 0.003131' in encode
assert '12.92 * value' in encode
assert '1.055 * pow(value, 1.0 / 2.4) - 0.055' in encode


def glsl_equivalent(channel):
    exposed = max(channel, 0.0) * values['exposure']
    x = min(values['preCurveScale'] * (exposed + values['bloom']), values['clampMax'])
    a, b, c, d, e = (values[name] for name in ('a', 'b', 'c', 'd', 'e'))
    mapped = ((x * (a * x + c * b) + d * e) /
              (x * (a * x + b) + d) - e) * values['whiteScale']
    mapped = max(0.0, min(1.0, mapped))
    return 12.92 * mapped if mapped <= 0.003131 else 1.055 * mapped ** (1.0 / 2.4) - 0.055


# Event-1531 ISA output before UNORM storage; actual capture had tiny bloom.
samples = (
    ((0.29833984375, 0.219482421875, 0.250732421875),
     (0.6686400175094604, 0.58955979347229, 0.6238482594490051)),
    ((0.7470703125, 0.240966796875, 0.1448974609375),
     (0.8746290802955627, 0.6136124730110168, 0.4864988327026367)),
)
max_capture_error = 0.0
for linear, captured in samples:
    actual = tuple(glsl_equivalent(channel) for channel in linear)
    cpu = captured_display_tonemap(
        linear, (0.0, 0.0, 0.0), exposure=values['exposure'])['encoded']
    assert max(abs(a - b) for a, b in zip(actual, cpu)) < 1e-12
    max_capture_error = max(max_capture_error,
                            max(abs(a - b) for a, b in zip(actual, captured)))

assert math.isfinite(max_capture_error) and max_capture_error < 3e-5, max_capture_error
print(f'captured display GLSL/CPU: PASS; max capture error={max_capture_error:.9g} (bloom=0)')
