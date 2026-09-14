"""Offline RenderDoc debugger traces. Does not attach to or launch a game.

Arithmetic is interpreted by RenderDoc; resource operations use replay.
These traces are not native GPU register dumps. Use a fresh output directory.
"""
import hashlib
import json
import os
import traceback
from pathlib import Path
import renderdoc as rd


def variable(value):
    return dict(name=value.name, rows=value.rows, columns=value.columns,
                type=str(value.type), f32=list(value.value.f32v),
                u32=list(value.value.u32v))


capture = controller = trace = output = None
output_created = False
exit_code = 1
try:
    cfg = json.loads(os.environ['DEADLIMIT_PIXEL_TRACE_CONFIG'])
    output = Path(cfg['output'])
    output.mkdir(parents=True, exist_ok=False)
    output_created = True
    capture = rd.OpenCaptureFile()
    status = capture.OpenFile(cfg['capture'], '', None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    status, controller = capture.OpenCapture(rd.ReplayOptions(), None)
    if status != rd.ResultCode.Succeeded:
        raise RuntimeError(str(status))
    controller.SetFrameEvent(cfg['event'], True)
    pipeline = controller.GetPipelineState()
    manifest = dict(capture=cfg['capture'], event=cfg['event'],
        shader=str(pipeline.GetShader(rd.ShaderStage.Pixel)),
        evidence='RenderDoc debugger arithmetic and replay-backed resource operations',
        traces=[])
    for x, y in cfg['pixels']:
        trace = controller.DebugPixel(x, y, rd.DebugPixelInputs())
        if trace.debugger is None:
            raise RuntimeError(f'No debuggable fragment at {x},{y}; choose a covered pixel')
        rows = []
        for batch in range(1000):
            states = controller.ContinueDebug(trace.debugger)
            if not states:
                break
            for state in states:
                rows.append(dict(next=state.nextInstruction, step=state.stepIndex,
                    changes=[dict(before=variable(c.before), after=variable(c.after))
                             for c in state.changes]))
        else:
            raise RuntimeError('Instruction trace exceeded batch limit; incomplete evidence rejected')
        if not rows:
            raise RuntimeError('Empty instruction trace')
        target = output / f'trace-{x}-{y}.json'
        target.write_text(json.dumps(dict(capture=cfg['capture'], event=cfg['event'],
            shader=manifest['shader'], pixel=[x,y],
            inputs=[variable(v) for v in trace.inputs], states=rows)), encoding='utf-8')
        manifest['traces'].append(dict(file=target.name, pixel=[x,y], states=len(rows),
            sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
        controller.FreeTrace(trace)
        trace = None
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    exit_code = 0
except Exception:
    if output_created:
        (output / 'error.txt').write_text(traceback.format_exc(), encoding='utf-8')
finally:
    if trace is not None and controller is not None:
        controller.FreeTrace(trace)
    if controller is not None:
        controller.Shutdown()
    if capture is not None:
        capture.Shutdown()
# Exit the owned helper before Qt opens an unused window.
os._exit(exit_code)
