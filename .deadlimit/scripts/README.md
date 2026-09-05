# Deadlimit Scripts

`DeadlimitPipelineScripts.ms` is the current MAXScript implementation of **Deadlimit Scripts (Deadlimit Pipeline Scripts)**. It groups Vertex Color authoring, bone display helpers, Inner Lineart topology, and Vertex Color FBX sidecar export in one window for the Deadlock pipeline.

The implementation filename and existing MAXScript class/global identifiers are retained for compatibility. `Deadlimit Scripts` is the product name; `Deadlimit Pipeline Scripts` is its long form. Additional DCC implementations, including Blender, share this product scope.

The window uses four open stacked sections: BONE TOOLS, VERTEX COLOR, INNER LINEART, and the always-last EXPORT VERTEX COLOR section. The native MAXScript rollout floater keeps the sections in flow when any section is collapsed or reopened, so they cannot overlap.

## Vertex Color FBX export

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry and renderable Shape/Spline objects selected.
3. Run `DeadlimitPipelineScripts.ms` and press EXPORT VERTEX COLOR FBX.
4. Run PREPARE in Deadlimit Manager.

The sidecar is written as `<dmx-name>_vertexcolor.fbx` beside the latest DMX exported by Wall Worm. Renderable Shape/Spline objects are evaluated through a temporary Turn To Mesh modifier. Object types, modifier stacks, selection, scene dirty state, and FBX settings are restored after success or failure.

During PREPARE, Deadlimit Manager transfers Vertex Color to every DMX mesh whose assigned material name contains `vertexcolor`. A missing channel 0 produces neutral gray (128, 128, 128, 255). Uniform-color meshes transfer directly. Multi-color meshes are matched by UV topology or polygon positions, so UVs are optional when the FBX and DMX geometry correspond.

FIXED GAMMA is on by default. Its adjacent GAMMA spinner uses the export power `RGB^(GAMMA/2.2)`. The default `1.0` applies the established `1/2.2` Source 2 correction; `2.2` produces exponent `1.0` and preserves the stored RGB. The spinner locks while FIXED GAMMA is off. The correction exists only in the exported FBX.

## Vertex Color tools

VERTEX COLOR operates on selected geometry:

- The Convert row contains Palette to Vertex, Vertex to Palette, and ON/OFF Vertex.
- Palette to Vertex fills channel 0 from each selected object's wire color. On an Editable Poly base object it writes the map channel directly, preserving the exact base-object instance, vertex/edge/face selections, and every existing modifier instance, order, enabled state, and setting. It enables shaded Vertex Color display.
- Vertex to Palette copies the first used channel 0 color to each selected object's wire color.
- ON/OFF Vertex counts the selected meshes whose shaded Vertex Color display is enabled. When a strict majority is enabled, every selected mesh is forced OFF. Otherwise every selected mesh is forced ON. The display change is forced through the Nitrous cache immediately, including while the mesh remains selected.
- The next row contains VERT, PALETTE, MAT, and SPREAD in that order. SPREAD treats the first selected mesh as the reference and applies enabled data to every later selected mesh in one Undo step. Its VERT path uses the same non-collapsing Editable Poly write described above. PALETTE copies the wire color, and MAT assigns the reference material. All three switches are on by default; the reference mesh stays unchanged.

Vertex Color write commands retain exact before/after channel-0 snapshots for affected Editable Poly meshes during the current host session. Their Undo and Redo restore the matching map channel, invalidate the stale Nitrous Vertex Color cache, and complete a viewport redraw, so restored colors become visible without deselecting objects or adding Vertex Paint modifiers. This compensates for direct `polyop` map writes, which do not create their own channel RestoreObj. The callbacks and snapshots are not saved into the scene, and restoration does not alter the base-object instance, modifier stack, topology, sub-object selections, or unrelated RGB/Alpha channels.

## Bone display tools

BONE TOOLS operations affect selected BoneGeometry display meshes and use one Undo step:

- Fit to Hierarchy uses the average pivot distance to direct bone children. A leaf uses half the distance to its parent pivot.
- Length (cm) plus SET assigns an exact visual length in centimeters.
- Flip X reverses display geometry along local X while preserving node transforms, pivots, hierarchy, animation, and names. The bone base remains convex. The adjacent `_R/_r` checkbox is off by default; when enabled, Flip X filters the current selection to names ending exactly in `_R` or `_r`.
- Restore Converted Branch repairs an accidental Convert to Editable Poly on selected native-bone roots and their compatible converted descendants. It recognizes the native converted bone display cage (`Editable Poly`, 9 vertices, 9 faces, no modifiers), recovers length/width/height from that cage, and swaps only the base object back to `BoneGeometry`. Node handles, names, layers, transforms, object offsets, hierarchy, animation controllers, and external Skin references remain attached to the original nodes. The complete branch repair is one `Restore Converted Skeleton Bones` Undo step. Ordinary child geometry and modifier stacks are excluded.

## Inner Lineart

INNER LINEART builds a separate bevel-like strip for Deadlock's expanded-backface outline:

1. Make the source base Editable Poly or the intended Edit Poly modifier active.
2. Enter Edge mode and select a connected network of at least two inner edges.
3. Set the full strip Width in millimeters.
4. Configure taper, layer, polygon winding, normals, and optional Skin copying.
5. Press CREATE LINEART.

The command reads the selected-edge graph from the active authoring level and creates a new Editable Poly. Source topology, Skin vertex IDs, modifier instances, sub-object data, UVs, and vertex paint stay unchanged. The generated node copies the source world transform together with its object-offset position, rotation, and scale, so lineart remains aligned under arbitrary position, rotation, non-uniform scale, parent transforms, and object offsets. Generated objects use `lineart_<source>_01`, `lineart_<source>_02`, and the next available two-digit suffix. The default destination is the source object's layer. Custom Layer enables the adjacent name field, whose default is `lineart`; the named layer is reused or created.

Width is the full world-space width centered on the source edges. The builder uses the two adjacent source face planes for each strip segment and stitches adjacent segments through face sectors around every selected vertex. A two-edge turn shares one cross-section edge. A three-edge junction adds one triangle, four edges add one quad, five edges add one quad plus one triangle, and higher valences continue with quads plus at most one triangle. No center fan or n-gon is created. Every generated face uses smoothing group 1 and the corresponding source material ID.

Taper Ends is on by default. Each open endpoint collapses to one geometry vertex. Width along an open connected component follows `1 - (1 - t)^EXP`, where `t` is normalized graph distance from the nearest endpoint. EXP `1` gives a linear roof profile; EXP `2` gives the default rounded bridge-like profile. Closed loops have no endpoints and keep full width. The EXP spinner locks while Taper Ends is off.

Flip Polygons and Invert Normals are on by default. The complete mesh and all map faces are generated and stitched first. CREATE LINEART then performs one native Editable Poly element flip over the finished face set and bakes reversed normals through a temporary Edit Normals modifier. ALTERNATIVE FAST writes the same final geometry/map winding into the completed in-memory records and writes persistent reversed vertex normals during a brief Editable Mesh conversion. Both paths finish as a clean Editable Poly; Copy Skin, when enabled, is added afterwards. Together the defaults produce inward-facing geometry whose explicit normals point back toward the original surface, matching the intended expanded-backface outline. Either operation can be disabled independently.

CREATE LINEART is the reference transaction. ALTERNATIVE FAST uses the same geometry kernel, map-channel transfer, final polygon winding, evaluated-normal result, layer assignment, and optional Skin copy. It writes final winding into completed in-memory face records, writes persistent normals directly on Editable Mesh, and disables repeated undo snapshots while the new node is populated. Creating that node stays inside one named Undo transaction, so one Ctrl+Z removes the complete result. The Mesh-to-Poly return may cyclically rotate which corner is listed first for a polygon; vertex IDs, winding, UV/color/alpha corner values, smoothing, materials, transforms, and evaluated normals remain equivalent. The status line reports the fast operation time.

Autodesk documents that scene-changing loop operations can store complete internal object copies for every undo record and that `setNormal` on Editable Mesh creates persistent explicit normals in modern MAXScript hosts. The fast path uses those two contracts to avoid the reference Modify-panel flip/normal workflow while preserving the evaluated result. Keep CREATE LINEART available as the conservative fallback while the alternative path is validated on production assets.

Every supported face-corner map channel is copied from the source references, including UV channels, Vertex Color, Vertex Alpha, and Vertex Illumination. Existing UV seams and painted data on the source remain untouched. Generated map faces follow their new strip and junction faces.

## Optional Skin copy

Copy Skin is off by default. The generated lineart is an independent unskinned Editable Poly unless this checkbox is enabled.

When enabled, every Skin modifier above the active authoring level is copied to the generated object. Each generated vertex stores one authoritative source vertex ID, then receives the source vertex's weights, DQ blend mask, normalization, and supported rigid state. Bind state and bone order come from the copied Skin modifier. The source Skin is read-only during this operation.

Creation is one Undo step. Input validation happens before the new object is committed. The command rejects multiple selected objects, fewer than two selected edges, disconnected one-edge components, border or zero-length edges, unsupported active stack levels, invalid map topology, and incompatible Skin input when Copy Skin is enabled. Temporary working geometry is discarded after success or failure.

The previous zero-width in-place implementation remains internal for scene/script compatibility. The INNER LINEART rollout uses the detached strip path described above.

Skin exclusion lists and Skin gizmo internals remain outside the per-vertex copy contract. Keep the normal Skin backup workflow when those features are used.
