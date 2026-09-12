"""Pack a local capture export for Painter; no resampling of engine mip pixels."""
import argparse
import json
from pathlib import Path
import cv2
import numpy as np
from deadlock_environment_reference import read_captured_lut


def prepare(source, output):
    manifest = json.loads((source / 'manifest.json').read_text())
    faces = [r for r in manifest['resources'] if r['file'].endswith('.hdr')]
    if len(faces) != 42 or {r['cube'] for r in faces} != {0}:
        raise ValueError('This proof profile requires cube zero, six faces, seven mips')
    atlas = np.zeros((7*256, 6*256, 3), dtype=np.float32)
    for item in faces:
        face, mip = item['face'], item['mip']
        pixels = cv2.imread(str(source / item['file']), cv2.IMREAD_UNCHANGED)
        size = 256 >> mip
        if pixels is None or pixels.shape != (size, size, 3):
            raise ValueError(f'Unexpected dimensions: {item["file"]}')
        atlas[mip*256:mip*256+size, face*256:face*256+size] = pixels
    lut = np.asarray(read_captured_lut(source / 'brdf-array.dds'), dtype=np.float64).reshape(64,64,4)
    # PNG UNORM16 error <= 1/131070; custom data sampler must remain linear.
    lut = np.rint(np.clip(lut, 0, 1)*65535).astype(np.uint16)
    output.mkdir(parents=True, exist_ok=False)
    if not cv2.imwrite(str(output / 'environment-atlas.hdr'), atlas):
        raise RuntimeError('HDR write failed')
    if not cv2.imwrite(str(output / 'brdf-lut.png'), lut[:,:,[2,1,0,3]]):
        raise RuntimeError('LUT write failed')
    (output / 'environment.json').write_text(json.dumps({
        'version': 1, 'source': 'reduced-csdk-event-791',
        'atlas': 'environment-atlas.hdr', 'lut': 'brdf-lut.png',
        'limitations': ['RGBE radiance precision', 'UNORM16 LUT precision',
                         'face-edge clamp', 'single spatial probe']}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    prepare(args.source, args.output)
