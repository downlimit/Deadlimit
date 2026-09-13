"""CPU reference for Reduced CSDK event 791, instructions 834-862.

No Valve texture data is embedded. Radiance, material F0, and LUT samples
are explicit inputs. This reference does not emulate cubemap filtering.
"""
import math
import struct
from pathlib import Path


def dominant_reflection(normal, reflection, roughness):
    """ISA 249-258, normalized shading-normal blend for this opaque path."""
    smooth = max(1 - roughness*roughness, 0)
    weight = smooth * (math.sqrt(smooth) + roughness*roughness)
    direction = tuple(n + weight*(r-n) for n,r in zip(normal, reflection))
    length = math.sqrt(sum(c*c for c in direction))
    return tuple(c/length for c in direction)


def box_project(position, direction, minimum, maximum):
    """ISA 395-404 in probe-local coordinates, for an interior point."""
    times = []
    for p,d,lo,hi in zip(position,direction,minimum,maximum):
        if d == 0:
            if not lo < p < hi:
                raise ValueError('Degenerate boundary ray requires IEEE GPU semantics')
            times.append(math.inf)
        else:
            times.append(max((hi-p)/d,(lo-p)/d))
    hit = tuple(p+d*abs(min(times)) for p,d in zip(position,direction))
    length = math.sqrt(sum(c*c for c in hit))
    return tuple(c/length for c in hit)


def projected_lookup_direction(projected, unprojected, roughness):
    """ISA 422-423: roughness lerp after box projection, without normalization."""
    return tuple(p + roughness*(u-p) for p,u in zip(projected,unprojected))


def lookup_uv(roughness, ndotv):
    """Instructions 834-838: texel-centred 64x64 lookup, array slice 1."""
    return (roughness * (63 / 64) + 0.5 / 64,
            math.sqrt(1 - ndotv) * (63 / 64) + 0.5 / 64)


def read_captured_lut(path):
    """Read local DDS export: RGBA16 float/typeless, 64x64x3, one mip.

    The captured shader view interprets this resource as floating-point.
    Fail on unrelated DDS layouts rather than silently resampling them.
    """
    data = Path(path).read_bytes()
    if data[:4] != b'DDS ' or data[84:88] != b'DX10':
        raise ValueError('Expected DX10 DDS')
    height, width = struct.unpack_from('<II', data, 12)
    mips = struct.unpack_from('<I', data, 28)[0]
    fmt, dimension, flags, layers, _ = struct.unpack_from('<5I', data, 128)
    if (width, height, layers, dimension) != (64, 64, 3, 3):
        raise ValueError('Expected 64x64 three-layer Texture2D array')
    if fmt not in (9, 10) or mips not in (0, 1) or flags & 4:
        raise ValueError('Expected uncompressed RGBA16 float/typeless, one mip')
    if len(data) != 148 + 3 * 64 * 64 * 8:
        raise ValueError('Unexpected payload length')
    return list(struct.iter_unpack('<4e', data[148 + 64 * 64 * 8:148 + 2 * 64 * 64 * 8]))


def sample_lut(lut, roughness, ndotv):
    u, v = lookup_uv(roughness, ndotv)
    x, y = u * 64 - 0.5, v * 64 - 0.5
    ix, iy = math.floor(x), math.floor(y)
    tx, ty = x - ix, y - iy
    def fetch(dx, dy, c):
        return lut[min(63, max(0, iy + dy)) * 64 + min(63, max(0, ix + dx))][c]
    return tuple((fetch(0, 0, c) * (1-tx) + fetch(1, 0, c) * tx) * (1-ty)
                 + (fetch(0, 1, c) * (1-tx) + fetch(1, 1, c) * tx) * ty for c in (0, 1))


def environment_response(lut_rg, f0, prefiltered_radiance, probe_irradiance,
                         multiple_scattering=True):
    """Instructions 839-840 and 849-862, after optional radiance normalization.

    Returns specular and energy-adjusted diffuse probe. The caller must provide
    r9 and r19 at instruction 849; raw cubemap pixels are not equivalent inputs.
    """
    a, b = lut_rg
    ess = tuple(a + c * (b-a) for c in f0)
    specular = tuple(r * e for r, e in zip(prefiltered_radiance, ess))
    if not multiple_scattering:
        return specular, tuple(probe_irradiance)
    # r8.y*r11 == F0 at this boundary. 0.047619 is the literal in the ISA.
    favg = tuple(c + (1-c) * 0.047619 for c in f0)
    ems = 1 - b
    fms = tuple(e * f / (1 - ems*f) for e, f in zip(ess, favg))
    extra = tuple(ems * f for f in fms)
    return (tuple(s + p*m for s, p, m in zip(specular, probe_irradiance, extra)),
            tuple(p*(1-e-m) for p, e, m in zip(probe_irradiance, ess, extra)))


def environment_visibility(specular, screen_visibility, global_visibility):
    """ISA 1732-1734. Material AO is deliberately not an input."""
    return tuple(c*min(screen_visibility,1)*global_visibility for c in specular)


def ordinary_direct_specular(roughness, ndoth, ndotv, ndotl, ldoth, f0, compensation):
    """CSDK ISA 1163-1188: ordinary GGX response before light radiance."""
    r2=roughness*roughness
    r4=r2*r2
    denominator=1+ndoth*ndoth*(r4-1)
    distribution=r4/(denominator*denominator)
    visibility=0.5/max(ndotl*(ndotv*(1-r2)+r2)+ndotv*(ndotl*(1-r2)+r2),0.00001)
    fresnel=(1-max(0,min(1,ldoth)))**5
    return tuple(distribution*visibility*ndotl*(f+(1-f)*fresnel)*e for f,e in zip(f0,compensation))


def opaque_composition(base, metalness, direct_diffuse, bounce, ao_response,
                       rim, emissive_amount, direct_specular, environment_specular,
                       screen_specular_visibility, global_visibility):
    """ISA 1731-1742, linear output before debug overrides and later passes."""
    env=environment_visibility(environment_specular,screen_specular_visibility,global_visibility)
    return tuple(
        (d+b*a)*global_visibility*c*(1-metalness) + r + emissive_amount*c +
        sp*global_visibility + e
        for c,d,b,a,r,sp,e in zip(base,direct_diffuse,bounce,ao_response,rim,direct_specular,env))


def normalized_radiance(radiance, probe, roughness, normalization_luminance,
                        enabled=True, coefficients=(34.4444465637207, -2.444446563720703)):
    """Instructions 841-847. Caller supplies traced r6.y, not guessed exposure."""
    if not enabled:
        return tuple(radiance)
    if normalization_luminance <= 0:
        raise ValueError('Positive traced r6.y is required')
    target = sum(x*w for x, w in zip(probe, (0.2125, 0.7154, 0.0721)))
    scale = min(max(roughness*coefficients[0]+coefficients[1], 1), target/normalization_luminance)
    return tuple(x*scale for x in radiance)
