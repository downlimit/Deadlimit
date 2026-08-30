# Deadlimit Max Script

`DeadlimitPipelineScripts.ms` is the current 3ds Max implementation of Deadlimit Max Script. It groups Vertex Color authoring, bone display helpers, Inner Lineart topology, and Vertex Color FBX sidecar export in one window for the Deadlock pipeline.

The implementation filename and existing MaxScript class/global identifiers are retained for compatibility. The project name is `Deadlimit Max Script`.

The window uses four open stacked sections: BONE TOOLS, VERTEX COLOR, INNER LINEART, and the always-last EXPORT VERTEX COLOR section. The native 3ds Max rollout floater keeps the sections in flow when any section is collapsed or reopened, so they cannot overlap.

## Vertex Color FBX export

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry and renderable Shape/Spline objects selected.
3. Run `DeadlimitPipelineScripts.ms` and press EXPORT VERTEX COLOR FBX.
4. Run PREPARE in Deadlimit Manager.

The sidecar is written as `<dmx-name>_vertexcolor.fbx` beside the latest DMX exported by Wall Worm. Renderable Shape/Spline objects are evaluated through a temporary Turn To Mesh modifier. Object types, modifier stacks, selection, scene dirty state, and FBX settings are restored after success or failure.

During PREPARE, Deadlimit Manager transfers Vertex Color to every DMX mesh whose assigned material name contains `vertexcolor`. A missing channel 0 produces neutral gray (128, 128, 128, 255). Uniform-color meshes transfer directly. Multi-color meshes are matched by UV topology or polygon positions, so UVs are optional when the FBX and DMX geometry correspond.

FIXED GAMMA is on by default. It exports channel 0 RGB as value^(1/2.2), intended for Source 2. Disable it for unchanged stored values and the regular/Marmoset path. The correction exists only in the exported FBX.

## Vertex Color tools

VERTEX COLOR operates on selected geometry:

- The Convert row contains Palette to Vertex, Vertex to Palette, and ON/OFF Vertex.
- Palette to Vertex fills channel 0 from each selected object's wire color. It bakes below Skin and the existing modifier stack and enables shaded Vertex Color display.
- Vertex to Palette copies the first used channel 0 color to each selected object's wire color.
- ON/OFF Vertex counts the selected meshes whose shaded Vertex Color display is enabled. When a strict majority is enabled, every selected mesh is forced OFF. Otherwise every selected mesh is forced ON.
- The next row contains VERT, PALETTE, MAT, and SPREAD in that order. SPREAD treats the first selected mesh as the reference and applies enabled data to every later selected mesh in one Undo step. VERT copies the first used channel 0 color, PALETTE copies the wire color, and MAT assigns the reference material. All three switches are on by default; the reference mesh stays unchanged.

## Bone display tools

BONE TOOLS operations affect selected BoneGeometry display meshes and use one Undo step:

- Fit to Hierarchy uses the average pivot distance to direct bone children. A leaf uses half the distance to its parent pivot.
- Length (cm) plus SET assigns an exact visual length in centimeters.
- Flip X reverses display geometry along local X while preserving node transforms, pivots, hierarchy, animation, and names. The bone base remains convex.

## Inner Lineart

INNER LINEART creates zero-width topology for normal-driven inner outlines:

1. Use a bare Editable Poly, a bare Editable Mesh, or make the intended Edit Poly modifier active anywhere inside the stack.
2. Enter Edge mode and select a connected chain of at least two non-border edges. T- and X-junctions are supported.
3. Optionally enable VertexColor Marker.
4. Press Create Lineart.

The deterministic sector builder records source vertices and face corners, partitions faces around selected vertices into sectors, creates coincident sector vertices, rebuilds original faces, and closes selected edges with zero-area polygons. It does not call 3ds Max Chamfer. A three-way junction receives one visible triangle cap. A four-way junction receives one visible quad cap. Junctions with more sectors are filled by real visible faces no larger than quads. Cap faces inherit a neighboring source smoothing group and material ID. Isolated one-edge selections are rejected.

Every supported map channel is reconstructed corner-for-corner. New seam faces receive collapsed map faces at matching source corners, so their UV area is zero and existing UV seams remain unchanged. With VertexColor Marker enabled, each participating channel 0 corner receives half of its own current color. Different colors and color seams remain distinct. When channel 0 is absent, the tool creates a white channel with participating corners set to neutral gray.

Smoothing groups, material IDs, and edge visibility are restored for original faces. The generated topology is written directly into the Editable Poly where the selected edges live. The tool does not add a helper modifier.

When an Edit Poly modifier is active anywhere inside a stack, that same modifier receives the topology and stays in its original position. Modifiers above and below it, including Skin, remain in place. An active base Editable Poly is changed directly, including when other modifiers exist above it. A bare Editable Mesh is converted to Editable Poly first while retaining its physical edge selection. Conversion and topology creation use one Undo step.

Border edges, zero-length edges, isolated selected edges, existing zero-width lineart edges, multiple selected objects, unsupported base types, incomplete map channels, and non-Edge sub-object modes are rejected before the source is changed. Runtime failures discard the working copy before reporting the error.
