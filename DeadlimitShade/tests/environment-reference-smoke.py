"""Numerical checks; optional local capture LUT verification, no assets stored."""
import math
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from deadlock_environment_reference import (environment_response, lookup_uv,
    normalized_radiance, read_captured_lut, sample_lut, dominant_reflection,
    box_project, projected_lookup_direction)


class EnvironmentReference(unittest.TestCase):
    def test_dominant_direction_endpoints(self):
        normal, reflection = (0,0,1), (0.6,0,0.8)
        self.assertEqual(dominant_reflection(normal,reflection,0),reflection)
        self.assertEqual(dominant_reflection(normal,reflection,1),normal)

    def test_box_and_lookup_blend(self):
        projected=box_project((0.5,0,0),(0,0,1),(-1,-1,-1),(1,1,1))
        self.assertAlmostEqual(projected[0],0.5/math.sqrt(1.25))
        self.assertAlmostEqual(projected[2],1/math.sqrt(1.25))
        self.assertEqual(projected_lookup_direction(projected,(0,0,1),0),projected)
        self.assertEqual(projected_lookup_direction(projected,(0,0,1),1),(0,0,1))

    def test_lookup_centres_and_view_warp(self):
        self.assertEqual(lookup_uv(0, 1), (0.5/64, 0.5/64))
        self.assertEqual(lookup_uv(1, 0), (63.5/64, 63.5/64))
        self.assertEqual(lookup_uv(0.5, 0.75), (0.5, 0.5))

    def test_disabled_branch(self):
        spec, probe = environment_response((0.1, 0.8), (0, 0.5, 1), (2, 2, 2), (1, 2, 3), False)
        for got, expected in zip(spec, (0.2, 0.9, 1.6)):
            self.assertAlmostEqual(got, expected)
        self.assertEqual(probe, (1, 2, 3))

    def test_white_furnace(self):
        # Synthetic inputs: tests the algebra, not the validity of a LUT asset.
        for a, b in ((0.1, 0.8), (0, 0.3), (0.25, 1)):
            for f0 in ((0.04,)*3, (0.8, 0.4, 0.2), (1,)*3):
                spec, diffuse = environment_response((a,b), f0, (1,)*3, (1,)*3)
                for s, d in zip(spec, diffuse):
                    self.assertAlmostEqual(s+d, 1)
                    self.assertGreaterEqual(d, -1e-12)

    def test_perfect_reflector_recovers_energy(self):
        spec, diffuse = environment_response((0.1, 0.4), (1,)*3, (1,)*3, (1,)*3)
        for s, d in zip(spec, diffuse):
            self.assertAlmostEqual(s, 1)
            self.assertAlmostEqual(d, 0)

    def test_normalization_is_bounded_by_probe(self):
        self.assertEqual(normalized_radiance((2,)*3, (1,)*3, 1, 2), (1,)*3)
        self.assertEqual(normalized_radiance((2,)*3, (1,)*3, 1, 2, False), (2,)*3)
        with self.assertRaises(ValueError):
            normalized_radiance((1,)*3, (1,)*3, 0.5, 0)


if __name__ == '__main__':
    if len(sys.argv) == 2 and sys.argv[1].endswith('.dds'):
        lut = read_captured_lut(sys.argv.pop())
        for roughness in (0, 0.25, 0.5, 0.75, 1):
            for ndotv in (0, 0.25, 0.5, 0.75, 1):
                rg = sample_lut(lut, roughness, ndotv)
                if not all(math.isfinite(x) and 0 <= x <= 1.001 for x in rg):
                    raise ValueError(f'Invalid captured LUT sample {rg}')
                spec, diffuse = environment_response(rg, (0.04,)*3, (1,)*3, (1,)*3)
                if not all(abs(s+d-1) < 1e-9 for s,d in zip(spec,diffuse)):
                    raise ValueError('Captured LUT fails furnace identity')
        print('25 captured LUT coordinates verified; no fitting coefficients used.')
    unittest.main()
