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
from deadlock_environment_reference import environment_visibility, opaque_composition
from deadlock_environment_reference import material_f0, ordinary_direct_specular_breakdown


# PerViewLightingConstantBufferGpu_t cb3[19:20] in the selected Reduced CSDK
# capture. The direction is Source 2 Z-up; Painter performs its documented
# coordinate adaptation separately.
CAPTURED_SUN_DIRECTION=(0.494464159011841,0.410708338022232,0.766044318675995)
CAPTURED_SUN_RADIANCE=(1.60000002384186,)*3


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
    trace=json.loads(path.read_text())
    s=snapshots(trace)
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
    a=s[1732][0]
    check('environment visibility',environment_visibility(
        a['r9'][:3],a['r11'][3],a['r7'][3]),s[1735][0]['r9'][:3])
    if 1166 in s:
        a=s[1166][0]
        terms=ordinary_direct_specular_breakdown(
            a['r5'][0],a['r3'][1],a['r5'][3],a['r20'][2],a['r5'][1],
            a['r12'][:3],a['r13'][:3])
        check('material F0',material_f0(a['r4'][:3],a['r4'][3]),a['r12'][:3])
        length=lambda v:math.sqrt(sum(x*x for x in v))
        normalize=lambda v:tuple(x/length(v) for x in v)
        dot=lambda x,y:sum(a*b for a,b in zip(x,y))
        view=normalize(tuple(-x for x in a['v2'][:3]))
        half_vector=normalize(tuple(x+y for x,y in zip(view,CAPTURED_SUN_DIRECTION)))
        check('half vector',half_vector,s[1163][0]['r23'][:3])
        check('NdotH',(dot(a['r7'][:3],half_vector),),(a['r3'][1],))
        check('NdotV',(max(0,min(1,dot(a['r7'][:3],view))),),(a['r5'][3],))
        check('NdotL',(max(0,min(1,dot(a['r7'][:3],CAPTURED_SUN_DIRECTION))),),(a['r20'][2],))
        check('LdotH',(max(0,min(1,dot(CAPTURED_SUN_DIRECTION,half_vector))),),(a['r5'][1],))
        check('roughness squared',(terms['roughness2'],),(s[1172][0]['r5'][1],))
        check('roughness fourth power',(terms['roughness4'],),(s[1173][0]['r9'][3],))
        check('Fresnel scalar',(terms['fresnel'],),(s[1170][0]['r5'][1],))
        check('material/specular tint',terms['tint'],s[1171][0]['r23'][:3])
        check('distribution',(terms['distribution'],),(s[1178][0]['r3'][1],))
        check('visibility/geometry',(terms['visibility'],),(s[1185][0]['r3'][0],))
        check('raw specular lobe',(terms['raw_lobe'],),(s[1187][0]['r3'][0],))
        check('ordinary direct specular',terms['contribution'],s[1189][0]['r23'][:3])
        sun_visibility=s[1156][0]['r8'][1]
        visible_radiance=tuple(sun_visibility*x for x in CAPTURED_SUN_RADIANCE)
        check('sun radiance times shadow visibility',visible_radiance,s[1157][0]['r18'][:3])
        direct_before_composition=tuple(x*y for x,y in zip(terms['contribution'],visible_radiance))
        check('direct specular before composition',direct_before_composition,s[1244][0]['r23'][:3])
        check('direct specular into r27',direct_before_composition,s[1292][0]['r27'][:3])
        check('final r27 (no contributing barn light)',direct_before_composition,s[1731][0]['r27'][:3])
        before=s[1741][0]['r12'][:3]
        after=s[1742][0]['r12'][:3]
        check('r27 contribution into final composition',
              tuple(a-b for a,b in zip(after,before)),
              tuple(x*s[1741][0]['r7'][3] for x in s[1741][0]['r27'][:3]))
    else:
        print(path.name, ': ordinary sun specular branch not executed')
    a=s[1731][0]
    check('opaque composition',opaque_composition(
        a['r4'][:3],a['r4'][3],a['r26'][:3],a['r28'][:3],
        [a['r6'][i] for i in (0,2,3)],a['r31'][:3],a['r5'][2],
        a['r27'][:3],a['r9'][:3],a['r11'][3],a['r7'][3]),
        [s[1743][0]['r8'][i] for i in (0,2,3)])
    print(path.name, ':', ', '.join(f'{name}={err:.3g}' for name,err in checks))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('traces',nargs='+',type=Path)
    parser.add_argument('--lut',type=Path)
    args=parser.parse_args()
    lut_data=read_captured_lut(args.lut) if args.lut else None
    for path in args.traces:compare(path,lut_data)
