"""Owned, short-lived qrenderdoc --python worker; never injects into a game."""
import hashlib
import json
import os
import traceback
from pathlib import Path
import renderdoc as rd

capture = controller = None
output = None
exit_code = 1
try:
    cfg = json.loads(os.environ['DEADLIMIT_ENV_CAPTURE_CONFIG'])
    output = Path(cfg['output'])
    output.mkdir(parents=True, exist_ok=False)
    capture = rd.OpenCaptureFile()
    status = capture.OpenFile(cfg['capture'], '', None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    status, controller = capture.OpenCapture(rd.ReplayOptions(), None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    controller.SetFrameEvent(cfg['event'], True)
    resources = controller.GetResources()
    textures = {str(t.resourceId): t for t in controller.GetTextures()}
    manifest = dict(source='user-selected capture; resource identities are capture-specific',
                    capture=cfg['capture'], event=cfg['event'], resources=[])
    for filename, matches in (
        ('brdf-array.dds', [r for r in resources if str(r.resourceId) == cfg['lut']]),
    ):
        if len(matches) != 1:
            raise RuntimeError(f'{filename}: expected one resource, got {len(matches)}')
        resource = matches[0]
        texture = textures[str(resource.resourceId)]
        save = rd.TextureSave()
        save.resourceId = resource.resourceId
        save.destType = rd.FileType.DDS
        target = output / filename
        result = controller.SaveTexture(save, str(target))
        if not target.is_file() or target.stat().st_size < 148:
            raise RuntimeError(f'{filename}: {result}')
        manifest['resources'].append(dict(file=filename, resource=str(resource.resourceId),
            name=resource.name, width=texture.width, height=texture.height,
            layers=texture.arraysize, mips=texture.mips, format=texture.format.Name(),
            sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    matches = [r for r in resources if r.name == cfg['environment']]
    if len(matches) != 1:
        raise RuntimeError('Expected one named environment array')
    resource = matches[0]
    texture = textures[str(resource.resourceId)]
    if not texture.cubemap or (cfg['cube']+1)*6 > texture.arraysize:
        raise RuntimeError('Cube index is outside the captured cubemap array')
    # Export one selected cube, not the complete 340-cube scene allocation.
    # HDR preserves linear values with RGBE precision; source format is recorded.
    for mip in range(texture.mips):
        for face in range(6):
            filename = f'cube-{cfg["cube"]}-mip-{mip}-face-{face}.hdr'
            target = output / filename
            save = rd.TextureSave()
            save.resourceId = resource.resourceId
            save.destType = rd.FileType.HDR
            save.mip = mip
            save.slice.sliceIndex = cfg['cube']*6 + face
            result = controller.SaveTexture(save, str(target))
            if not target.is_file() or target.stat().st_size == 0:
                raise RuntimeError(f'{filename}: {result}')
            manifest['resources'].append(dict(file=filename, resource=str(resource.resourceId),
                name=resource.name, mip=mip, face=face, cube=cfg['cube'],
                sourceFormat=texture.format.Name(), exportPrecision='RGBE',
                sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    exit_code = 0
except Exception:
    if output is not None and output.exists():
        (output / 'error.txt').write_text(traceback.format_exc(), encoding='utf-8')
finally:
    if controller is not None:
        controller.Shutdown()
    if capture is not None:
        capture.Shutdown()
# The wrapper owns this process; exit before Qt opens an unused main window.
os._exit(exit_code)
