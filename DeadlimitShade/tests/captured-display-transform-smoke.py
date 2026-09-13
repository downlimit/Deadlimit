"""Check the recovered event-1531 display equation at two traced pixels."""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from deadlock_display_reference import captured_display_tonemap


EXPOSURE = 1.0 * 1.013959527015686
CASES = (
    {
        'linear': (0.29833984375, 0.219482421875, 0.250732421875),
        'bloom': (2.682209014892578e-6, 2.4437904357910156e-6,
                  1.9669532775878906e-6),
        'exposed': (0.3025045394897461, 0.2225462943315506,
                    0.2542325258255005),
        'curve_input': (0.8470202088356018, 0.6231364607810974,
                        0.7118565440177917),
        'linear_mapped': (0.4046209454536438, 0.306496798992157,
                          0.34709522128105164),
        'encoded': (0.6686400175094604, 0.58955979347229,
                    0.6238482594490051),
    },
    {
        'linear': (0.7470703125, 0.240966796875, 0.1448974609375),
        'bloom': (2.205371856689453e-5, 1.9669532775878906e-5,
                  1.621246337890625e-5),
        'exposed': (0.7574990391731262, 0.24433058500289917,
                    0.14692015945911407),
        'curve_input': (2.121058940887451, 0.6841806769371033,
                        0.41142183542251587),
        'linear_mapped': (0.7381377220153809, 0.3346669375896454,
                          0.20175683498382568),
        'encoded': (0.8746290802955627, 0.6136124730110168,
                    0.4864988327026367),
    },
)


def check(actual, expected, tolerance=3e-7):
    error = max(abs(a - b) for a, b in zip(actual, expected))
    if not math.isfinite(error) or error > tolerance:
        raise AssertionError(f'error={error}; actual={actual}; expected={expected}')
    return error


maximum = 0.0
for case in CASES:
    result = captured_display_tonemap(
        case['linear'], case['bloom'], exposure=EXPOSURE)
    for boundary in ('exposed', 'curve_input', 'linear_mapped', 'encoded'):
        maximum = max(maximum, check(result[boundary], case[boundary]))
print(f'captured display transform: PASS; maximum error={maximum:.3g}')
