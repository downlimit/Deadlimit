#!/usr/bin/env python3
"""
Generate the source .sbs for Deadlimit Vector Blur.

Target: Substance Automation Toolkit / PySBS 2026.x.
The public parameter contract intentionally mirrors After Effects CC Vector Blur.
The kernel is an independently implemented approximation; Cycore's internal kernel
is proprietary, so parity must be calibrated with controlled AE/Painter captures.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from pysbs import context, sbsgenerator, sbsenum
from pysbs.autograph import ag_functions


GRAPH_ID = "Deadlimit_Vector_Blur"
SAMPLE_COUNT = 16

TYPE_OPTIONS = {
    0: "Natural",
    1: "Constant Length",
    2: "Perpendicular",
    3: "Direction Center",
    4: "Direction Fading",
}

PROPERTY_OPTIONS = {
    0: "Red",
    1: "Green",
    2: "Blue",
    3: "Alpha",
    4: "Luminance",
    5: "Lightness",
    6: "Hue",
    7: "Saturation",
}


def _slider_options(min_value: float, max_value: float, step: float, clamp: bool) -> dict:
    return {
        sbsenum.WidgetOptionEnum.MIN: str(min_value),
        sbsenum.WidgetOptionEnum.MAX: str(max_value),
        sbsenum.WidgetOptionEnum.STEP: str(step),
        sbsenum.WidgetOptionEnum.CLAMP: "1" if clamp else "0",
    }


def _add_dropdown(graph, identifier: str, label: str, default: int, values: dict, *, visible_if=None):
    param = graph.addInputParameter(
        aIdentifier=identifier,
        aWidget=sbsenum.WidgetEnum.DROPDOWN_INT1,
        aDefaultValue=default,
        aLabel=label,
        aVisibleIf=visible_if,
    )
    param.setDropDownList(aValueMap=values)
    return param


def _add_float(
    graph,
    identifier: str,
    label: str,
    default: float,
    min_value: float,
    max_value: float,
    step: float,
    *,
    clamp: bool = False,
    visible_if=None,
    description=None,
):
    return graph.addInputParameter(
        aIdentifier=identifier,
        aWidget=sbsenum.WidgetEnum.SLIDER_FLOAT1,
        aDefaultValue=default,
        aOptions=_slider_options(min_value, max_value, step, clamp),
        aLabel=label,
        aDescription=description,
        aVisibleIf=visible_if,
    )


def _select_property(fc, rgba, prop):
    r = fc.swizzle_float1(rgba, [0])
    g = fc.swizzle_float1(rgba, [1])
    b = fc.swizzle_float1(rgba, [2])
    a = fc.swizzle_float1(rgba, [3])

    max_rgb = fc.maximum(r, g, b)
    min_rgb = fc.minimum(r, g, b)
    delta = max_rgb - min_rgb
    lightness = (max_rgb + min_rgb) * 0.5
    luminance = r * 0.2126 + g * 0.7152 + b * 0.0722

    eps = 1.0e-6
    safe_delta = fc.max_of(delta, eps)

    # HSL hue in [0, 1).
    h_r = ((g - b) / safe_delta) / 6.0
    h_r = h_r - fc.floor(h_r)
    h_g = (((b - r) / safe_delta) + 2.0) / 6.0
    h_b = (((r - g) / safe_delta) + 4.0) / 6.0
    hue_by_max = fc.if_else(max_rgb == r, h_r, fc.if_else(max_rgb == g, h_g, h_b))
    hue = fc.if_else(delta <= eps, 0.0, hue_by_max)

    sat_den = fc.max_of(1.0 - fc.abs_of(2.0 * lightness - 1.0), eps)
    saturation = fc.if_else(delta <= eps, 0.0, delta / sat_den)

    value = r
    value = fc.if_else(prop == 1, g, value)
    value = fc.if_else(prop == 2, b, value)
    value = fc.if_else(prop == 3, a, value)
    value = fc.if_else(prop == 4, luminance, value)
    value = fc.if_else(prop == 5, lightness, value)
    value = fc.if_else(prop == 6, hue, value)
    value = fc.if_else(prop == 7, saturation, value)
    return value


def _sample_map_property(fc, pos, prop):
    return _select_property(fc, fc.create_color_sampler(pos, 1), prop)


def _sample_soft_map(fc, pos, prop, softness_px, inv_size):
    # Fixed 9-tap cross/diagonal prefilter. This is deliberately internal:
    # the public UI keeps the CC Vector Blur control set without a Quality control.
    radius = softness_px
    dx = fc.cartesian(fc.swizzle_float1(inv_size, [0]) * radius, 0.0)
    dy = fc.cartesian(0.0, fc.swizzle_float1(inv_size, [1]) * radius)

    c = _sample_map_property(fc, pos, prop)
    x0 = _sample_map_property(fc, pos - dx, prop)
    x1 = _sample_map_property(fc, pos + dx, prop)
    y0 = _sample_map_property(fc, pos - dy, prop)
    y1 = _sample_map_property(fc, pos + dy, prop)
    d0 = _sample_map_property(fc, pos - dx - dy, prop)
    d1 = _sample_map_property(fc, pos + dx - dy, prop)
    d2 = _sample_map_property(fc, pos - dx + dy, prop)
    d3 = _sample_map_property(fc, pos + dx + dy, prop)
    return (c + x0 + x1 + y0 + y1 + d0 + d1 + d2 + d3) / 9.0


def _safe_normalize2(fc, vec):
    length = fc.sqrt(fc.dot(vec, vec))
    return vec / fc.max_of(length, 1.0e-6), length


def _rotate2(fc, vec, radians):
    x = fc.swizzle_float1(vec, [0])
    y = fc.swizzle_float1(vec, [1])
    cs = fc.cos(radians)
    sn = fc.sin(radians)
    return fc.cartesian(x * cs - y * sn, x * sn + y * cs)


def _kernel(fc):
    pos = fc.variable("$pos", sbsenum.ParamTypeEnum.FLOAT2, use_param_type=True)
    size = fc.variable("$size", sbsenum.ParamTypeEnum.FLOAT2, use_param_type=True)
    inv_size = 1.0 / size

    type_id = fc.variable("Type", sbsenum.ParamTypeEnum.INTEGER1, use_param_type=True)
    amount = fc.variable("Amount", sbsenum.ParamTypeEnum.FLOAT1, use_param_type=True)
    angle_deg = fc.variable("AngleOffset", sbsenum.ParamTypeEnum.FLOAT1, use_param_type=True)
    ridge = fc.variable("RidgeSmoothness", sbsenum.ParamTypeEnum.FLOAT1, use_param_type=True)
    revolutions = fc.variable("Revolutions", sbsenum.ParamTypeEnum.FLOAT1, use_param_type=True)
    prop = fc.variable("Property", sbsenum.ParamTypeEnum.INTEGER1, use_param_type=True)
    map_softness = fc.variable("MapSoftness", sbsenum.ParamTypeEnum.FLOAT1, use_param_type=True)

    angle_offset = angle_deg * (math.pi / 180.0)

    # Map Softness is interpreted in pixels. A zero value must be an exact
    # no-prefilter path, so collapse the radius rather than changing tap count.
    softness_px = fc.max_of(map_softness, 0.0)
    map_value = _sample_soft_map(fc, pos, prop, softness_px, inv_size)

    # Natural / Constant Length / Perpendicular derive their direction from
    # the scalar map slope. Ridge Smoothness widens the derivative baseline.
    ridge_radius = 1.0 + fc.max_of(ridge, 0.0) * 0.25
    dx = fc.cartesian(fc.swizzle_float1(inv_size, [0]) * ridge_radius, 0.0)
    dy = fc.cartesian(0.0, fc.swizzle_float1(inv_size, [1]) * ridge_radius)

    gx = (
        _sample_soft_map(fc, pos + dx, prop, softness_px, inv_size)
        - _sample_soft_map(fc, pos - dx, prop, softness_px, inv_size)
    ) * 0.5
    gy = (
        _sample_soft_map(fc, pos + dy, prop, softness_px, inv_size)
        - _sample_soft_map(fc, pos - dy, prop, softness_px, inv_size)
    ) * 0.5

    gradient = fc.cartesian(gx, gy)
    grad_unit, grad_len = _safe_normalize2(fc, gradient)

    # Gain is bounded so a pathological map cannot launch source sampling
    # arbitrarily far outside the amount-defined footprint.
    natural_strength = fc.clamp(0.0, 1.0, grad_len * 8.0)
    natural_vec = grad_unit * natural_strength
    constant_vec = grad_unit

    perp_vec = fc.cartesian(
        -fc.swizzle_float1(grad_unit, [1]),
        fc.swizzle_float1(grad_unit, [0]),
    ) * natural_strength

    # Direction modes encode angle directly from the selected map property.
    direction_angle = angle_offset + map_value * revolutions * (2.0 * math.pi)
    direction_vec = fc.cartesian(fc.cos(direction_angle), fc.sin(direction_angle))

    # Angle Offset also rotates slope-derived modes.
    natural_vec = _rotate2(fc, natural_vec, angle_offset)
    constant_vec = _rotate2(fc, constant_vec, angle_offset)
    perp_vec = _rotate2(fc, perp_vec, angle_offset)

    vector = natural_vec
    vector = fc.if_else(type_id == 1, constant_vec, vector)
    vector = fc.if_else(type_id == 2, perp_vec, vector)
    vector = fc.if_else(type_id >= 3, direction_vec, vector)

    # Amount is pixels, then converted to normalized UV independently per axis.
    uv_extent = (vector * amount) * inv_size

    # Direction Center samples both sides of the current pixel.
    # Other modes, including Direction Fading, sample from the current pixel
    # toward uv_extent. Keeping sample count internal preserves AE-like UI.
    centered = type_id == 3

    accum = fc.create_color_sampler(pos, 0)
    for i in range(1, SAMPLE_COUNT):
        t = float(i) / float(SAMPLE_COUNT - 1)
        t_centered = t - 0.5
        sample_t = fc.if_else(centered, t_centered, t)
        sample_pos = pos + uv_extent * sample_t
        accum = accum + fc.create_color_sampler(sample_pos, 0)

    return accum / float(SAMPLE_COUNT)


def create_document(output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)

    ctx = context.Context()
    doc = sbsgenerator.createSBSDocument(ctx, str(output_path))
    graph = doc.createGraph(
        aGraphIdentifier=GRAPH_ID,
        aParameters={
            sbsenum.CompNodeParamEnum.OUTPUT_FORMAT: sbsenum.OutputFormatEnum.FORMAT_16BITS,
        },
        aInheritance={
            sbsenum.CompNodeParamEnum.OUTPUT_FORMAT: sbsenum.ParamInheritanceEnum.ABSOLUTE,
        },
    )

    graph.setAttribute(sbsenum.AttributesEnum.Label, "Deadlimit Vector Blur")
    graph.setAttribute(sbsenum.AttributesEnum.Author, "Deadlimit")
    graph.setAttribute(sbsenum.AttributesEnum.Category, "Filters")
    graph.setAttribute(
        sbsenum.AttributesEnum.Description,
        "Vector-map-driven blur for Substance 3D Painter with a CC Vector Blur-compatible public control set.",
    )
    graph.setAttribute(sbsenum.AttributesEnum.Tags, "deadlimit;filter;vector blur;painter")

    _add_dropdown(graph, "Type", "Type", 0, TYPE_OPTIONS)
    _add_float(
        graph,
        "Amount",
        "Amount",
        0.0,
        -500.0,
        500.0,
        0.1,
        clamp=True,
        description="Blur amount in pixels. Negative values reverse the vector direction.",
    )
    _add_float(
        graph,
        "AngleOffset",
        "Angle Offset",
        0.0,
        -180.0,
        180.0,
        0.1,
        clamp=False,
        description="Angular offset in degrees.",
    )
    _add_float(
        graph,
        "RidgeSmoothness",
        "Ridge Smoothness",
        1.0,
        0.0,
        100.0,
        0.1,
        clamp=False,
        visible_if="input.Type < 3",
    )
    _add_float(
        graph,
        "Revolutions",
        "Revolutions",
        1.0,
        0.0,
        20.0,
        0.1,
        clamp=False,
        visible_if="input.Type >= 3",
    )

    source = graph.createInputNode(
        aIdentifier="Source",
        aColorMode=sbsenum.ColorModeEnum.COLOR,
        aGUIPos=[0, 0, 0],
        aAttributes={sbsenum.AttributesEnum.Label: "Source"},
        aSetAsPrimary=True,
    )
    vector_map = graph.createInputNode(
        aIdentifier="VectorMap",
        aColorMode=sbsenum.ColorModeEnum.COLOR,
        aGUIPos=[0, 180, 0],
        aAttributes={sbsenum.AttributesEnum.Label: "Vector Map"},
        aSetAsPrimary=False,
    )
    graph.setInputImageIndex("Source", 0)
    graph.setInputImageIndex("VectorMap", 1)

    _add_dropdown(graph, "Property", "Property", 4, PROPERTY_OPTIONS)
    _add_float(
        graph,
        "MapSoftness",
        "Map Softness",
        0.0,
        0.0,
        100.0,
        0.1,
        clamp=False,
        description="Prefilter radius for the Vector Map, in pixels.",
    )

    pp = graph.createCompFilterNode(
        aFilter=sbsenum.FilterEnum.PIXEL_PROCESSOR,
        aGUIPos=[260, 70, 0],
        aParameters={
            sbsenum.CompNodeParamEnum.COLOR_MODE: sbsenum.ColorModeEnum.COLOR,
        },
        aInheritance={
            sbsenum.CompNodeParamEnum.OUTPUT_FORMAT: sbsenum.ParamInheritanceEnum.PARENT,
        },
    )

    # Pixel Processor's input is variadic. Connection order is the sampler
    # index contract used by _kernel(): Source=0, Vector Map=1.
    graph.connectNodes(source, pp, aRightNodeInput=sbsenum.InputEnum.INPUT)
    graph.connectNodes(vector_map, pp, aRightNodeInput=sbsenum.InputEnum.INPUT)

    per_pixel = pp.setDynamicParameter(sbsenum.CompNodeParamEnum.PER_PIXEL)
    ag_functions.generate_function(
        _kernel,
        doc,
        fn_node=per_pixel,
        layout_nodes=True,
        remove_unused_nodes=True,
    )

    output = graph.createOutputNode(
        aIdentifier="Output",
        aGUIPos=[520, 70, 0],
        aOutputFormat=sbsenum.TextureFormatEnum.DEFAULT_FORMAT,
        aAttributes={sbsenum.AttributesEnum.Label: "Output"},
    )
    graph.connectNodes(pp, output, aRightNodeInput=sbsenum.InputEnum.INPUT_NODE_OUTPUT)

    doc.writeDoc()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path, help="Destination .sbs path")
    args = parser.parse_args()
    create_document(args.output.resolve())
    print(f"Generated {args.output.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
