"""Verify prepared local assets retain all source mip pixels and LUT values."""
import argparse
import json
import sys
from pathlib import Path
import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from deadlock_environment_reference import read_captured_lut

parser = argparse.ArgumentParser()
parser.add_argument('source', type=Path)
parser.add_argument('bundle', type=Path)
args = parser.parse_args()
manifest = json.loads((args.source / 'manifest.json').read_text())
atlas = cv2.imread(str(args.bundle / 'environment-atlas.hdr'), cv2.IMREAD_UNCHANGED)
assert atlas.shape == (1792,1536,3), 'Atlas dimensions changed'
count = 0
for item in manifest['resources']:
    if not item['file'].endswith('.hdr'):
        continue
    original = cv2.imread(str(args.source / item['file']), cv2.IMREAD_UNCHANGED)
    mip, face = item['mip'], item['face']
    size = 256 >> mip
    packed = atlas[mip*256:mip*256+size, face*256:face*256+size]
    assert np.array_equal(original, packed), f'Mip pixels changed: {item["file"]}'
    count += 1
assert count == 42
original = np.asarray(read_captured_lut(args.source / 'brdf-array.dds')).reshape(64,64,4)
packed = cv2.imread(str(args.bundle / 'brdf-lut.png'), cv2.IMREAD_UNCHANGED)[:,:,[2,1,0,3]] / 65535.0
error = np.max(np.abs(original[:,:,:2]-packed[:,:,:2]))
assert error <= 0.5/65535 + 1e-8, error
print(f'PASS: 42 mip faces unchanged; maximum BRDF RG quantization error {error:.9g}')
