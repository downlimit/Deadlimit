"""CPU reference for the captured Reduced CSDK final tonemap pass.

The constants are explicit inputs. This module does not emulate bloom filtering,
FXAA, render-target quantization, or texture sampling.
"""
import math


def srgb_encode(value):
    """Event 1531 ISA 28-34, including the executed low-value branch."""
    if value <= 0.003131:
        return value * 12.92
    return 1.055 * math.pow(value, 1.0 / 2.4) - 0.055


def captured_curve(value, *, a, b, c, d, e, f, white_scale):
    """Event 1531 ISA 18-27 for one non-negative, pre-scaled channel."""
    numerator = value * (a * value + c * b) + d * e
    denominator = value * (a * value + b) + d * f
    mapped = (numerator / denominator - e / f) * white_scale
    return max(0.0, min(1.0, mapped))


def captured_display_tonemap(
        linear_rgb, bloom_rgb, *, exposure, scene_weight=1.0,
        bloom_exposure_weight=0.0, bloom_add_weight=1.0,
        pre_curve_scale=2.8, clamp_max=6.062026500701904,
        a=0.295199990272522, b=0.3856000006198883, c=0.5,
        d=0.3950999975204468, e=0.23810000717639923, f=1.0,
        white_scale=1.5298149585723877):
    """Event 1531 ISA 4-34 with its captured zeroed post-curve blend."""
    exposed = tuple(max(0.0, channel) * exposure for channel in linear_rgb)
    curve_input = tuple(min(
        pre_curve_scale * (
            scene_weight * channel
            + bloom * (exposure * bloom_exposure_weight + bloom_add_weight)
        ),
        clamp_max,
    ) for channel, bloom in zip(exposed, bloom_rgb))
    linear_mapped = tuple(captured_curve(
        channel, a=a, b=b, c=c, d=d, e=e, f=f,
        white_scale=white_scale,
    ) for channel in curve_input)
    encoded = tuple(srgb_encode(channel) for channel in linear_mapped)
    return {
        'exposed': exposed,
        'curve_input': curve_input,
        'linear_mapped': linear_mapped,
        'encoded': encoded,
    }
