#!/usr/bin/env python3
"""Execute the AO button against mocked 9.1/10+ Painter APIs, no GUI needed."""

import ast
from pathlib import Path
from types import SimpleNamespace


source = (Path(__file__).resolve().parents[1] / "painter_plugins" /
          "deadlimit_apply.py").read_text(encoding="utf-8")
tree = ast.parse(source)
dock = next(node for node in tree.body
            if isinstance(node, ast.ClassDef) and node.name == "DeadlimitApplyDock")
method = next(node for node in dock.body
              if isinstance(node, ast.FunctionDef) and
              node.name == "_create_artistic_ao_channels")
module = ast.fix_missing_locations(ast.Module(body=[method], type_ignores=[]))


class Stack:
    def __init__(self, user1=False):
        self.channels = {"User0"}
        if user1:
            self.channels.add("User1")
        self.added = []

    def has_channel(self, channel):
        return channel in self.channels

    def add_channel(self, channel, fmt, label):
        self.channels.add(channel)
        self.added.append((channel, fmt, label))


class Fill:
    def __init__(self):
        self.name = None
        self.active_channels = set()
        self.source = None

    def set_name(self, name):
        self.name = name

    def set_source(self, channel, source):
        self.source = (channel, source)


def run_case(layerstack_available, baked_resource, user1=False):
    stack = Stack(user1)
    fill = Fill()
    texture_set = SimpleNamespace(
        name=lambda: "ivy_builder_body",
        get_mesh_map_resource=lambda usage: baked_resource)
    status = SimpleNamespace(text="", setText=lambda value: setattr(status, "text", value))
    bound = []
    owner = SimpleNamespace(
        status_label=status,
        _artistic_ao_stacks=lambda: [(texture_set, stack)],
        _bind_artistic_ao_presence=lambda stacks: bound.extend(stacks))
    painter = SimpleNamespace(
        project=SimpleNamespace(is_open=lambda: True),
        textureset=SimpleNamespace(
            ChannelFormat=SimpleNamespace(L8="L8"),
            MeshMapUsage=SimpleNamespace(AO="AO")))
    layerstack = None
    if layerstack_available:
        layerstack = SimpleNamespace(
            get_root_layer_nodes=lambda stack: [],
            InsertPosition=SimpleNamespace(
                from_textureset_stack=lambda stack: "bottom"),
            insert_fill=lambda position: fill)
    namespace = {
        "substance_painter": painter,
        "painter_layerstack": layerstack,
        "painter_colormanagement": SimpleNamespace(
            Color=lambda r, g, b: (r, g, b)),
        "ARTISTIC_AO_CHANNEL": "User1",
        "ARTISTIC_AO_LABEL": "Deadlimit Artistic AO",
        "ARTISTIC_AO_BASE_LABEL": "Deadlimit Artistic AO Base",
    }
    exec(compile(module, "deadlimit_apply.py", "exec"), namespace)
    namespace["_create_artistic_ao_channels"](owner)
    return stack, fill, status.text, bound


legacy, legacy_fill, legacy_status, legacy_bound = run_case(False, None)
assert legacy.channels == {"User0", "User1"}
assert legacy.added == [("User1", "L8", "Deadlimit Artistic AO")]
assert legacy_fill.name is None
assert legacy_status == (
    "Artistic AO channel created. Add a Fill Layer and assign "
    "baked AO to Deadlimit Artistic AO.")
assert legacy_bound

baked, baked_fill, baked_status, baked_bound = run_case(True, "baked-AO")
assert baked.channels == {"User0", "User1"}
assert baked_fill.name == "Deadlimit Artistic AO Base"
assert baked_fill.active_channels == {"User1"}
assert baked_fill.source == ("User1", "baked-AO")
assert baked_bound and "base Fill Layers created" in baked_status

white, white_fill, _, _ = run_case(True, None)
assert white_fill.source == ("User1", (1.0, 1.0, 1.0))

existing, _, _, _ = run_case(False, None, user1=True)
assert existing.added == [] and existing.channels == {"User0", "User1"}

shader = (Path(__file__).resolve().parents[1] / "shaders" /
          "Deadlock_Hero.glsl").read_text(encoding="utf-8")
assert "float ambientOcclusion = dl_artistic_ao_present" in shader
assert "textureSparse(dl_artistic_ao_tex, inputs.sparse_coord).r" in shader
assert 'dl_artistic_ao_present: artisticAoTextureSets.indexOf(textureSetName) >= 0' in source

print("Painter 9.1 User1 fallback and Painter 10+ AO base contracts passed.")
