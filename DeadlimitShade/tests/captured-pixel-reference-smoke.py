"""Compare independent reference functions with RenderDoc instruction traces.

Trace files are local evidence; never embed captured inputs in repository tests.
RenderDoc executes arithmetic in its debugger and uses replay for resource access.
This is stronger than synthetic tests, but is not a native GPU register readback.
"""
import argparse
import json
import math
import struct
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from deadlock_environment_reference import dominant_reflection, box_project, projected_lookup_direction, environment_response, normalized_radiance, lookup_uv, read_captured_lut, sample_lut


def snapshots(trace):
    registers = {v['name']:v['f32'][:4] for v in trace['inputs']}
    result = {}
    for state in trace['states']:
        for change in state['changes']:
            value = change['after']
            registers[value['name']] = value['f32'][:4]
        result.setdefault(state['next'],[]).append(dict(registers))
    return result


def compare(path, lut_data=None):
    s=snapshots(json.loads(path.read_text()))
    checks=[]
    def check(name,actual,expected,tolerance=2e-6):
        error=max(abs(a-b) for a,b in zip(actual,expected))
        checks.append((name,error))
        if not math.isfinite(error) or error>tolerance:
            raise AssertionError(f'{path.name} {name}: error={error}; actual={actual}; expected={expected}')
    a=s[249][0]
    check('dominant direction',dominant_reflection(a['r7'][:3],a['r16'][:3],a['r5'][0]),s[259][0]['r17'][:3])
    for i,a in enumerate(s[395]):
        # r54=max-position; r55=min-position is established at instruction 396.
        b=s[398][i]
        position=a['r52'][:3]
        lo=[p+d for p,d in zip(position,b['r55'][:3])]
        hi=[p+d for p,d in zip(position,a['r54'][:3])]
        check(f'box projection {i}',box_project(position,a['r51'][:3],lo,hi),s[405][i]['r50'][:3])
    for i,a in enumerate(s[422]):
        check(f'projected lookup {i}',projected_lookup_direction(
            a['r50'][:3],a['r51'][:3],a['r5'][0]),s[430][i]['r50'][:3])
    a=s[834][0]
    check('LUT coordinates',lookup_uv(a['r5'][0],a['r5'][3]),s[838][0]['r21'][:2])
    check('normalization',normalized_radiance(a['r9'][:3],a['r19'][:3],a['r5'][0],a['r6'][1]),s[849][0]['r9'][:3])
    lut=s[839][0]['r21'][:2]
    if lut_data is not None:
        filtered=sample_lut(lut_data,a['r5'][0],a['r5'][3])
        # Both captured samples match half-rounded bilinear interpolation.
        # This is an observed boundary, not a general hardware rounding claim.
        half_filtered=struct.unpack('<2e',struct.pack('<2e',*filtered))
        check('captured LUT bilinear/half',half_filtered,lut,tolerance=0)
    a=s[849][0]
    spec,diff=environment_response(lut,a['r12'][:3],a['r9'][:3],a['r19'][:3])
    check('multiple scattering specular',spec,s[863][0]['r9'][:3])
    check('multiple scattering diffuse',diff,s[863][0]['r19'][:3])
    print(path.name, ':', ', '.join(f'{name}={err:.3g}' for name,err in checks))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('traces',nargs='+',type=Path)
    parser.add_argument('--lut',type=Path)
    args=parser.parse_args()
    lut_data=read_captured_lut(args.lut) if args.lut else None
    for path in args.traces:compare(path,lut_data)
