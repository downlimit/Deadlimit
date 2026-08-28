# MaxScript VertColor Trans

`DeadlimitVertexColorFBX.ms` exports the selected 3ds Max geometry as `<dmx-name>_vertexcolor.fbx` beside the latest DMX exported by Wall Worm.

Workflow:

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry selected.
3. Run the script and press `EXPORT SELECTED VERTEX COLOR FBX`.
4. Run `PREPARE` in Deadlimit.

During PREPARE, Deadlimit transfers Vertex Color to every DMX mesh whose assigned material name contains `vertexcolor`. A missing channel `0` produces neutral gray `(128, 128, 128, 255)`. The FBX remains beside the artist DMX as persistent project source data. PREPARE validates and reuses it for repeated PREPARE, BUILD FOR TEST, and ONLINE synchronization.

The MaxScript contains no project path and no path to `Deadlimit.exe`. It only reads Wall Worm's last export folder from `3dsMax.ini`.

Vertex Color tools operate on the selected geometry and do not use Deadlimit or Wall Worm:

- `To Vertex` fills channel `0` from each object's wire color, inserts the operation below the existing modifier stack, bakes it with `VertexPaint` + `Collapse To`, and enables shaded Vertex Color display. Skin and the other existing modifiers remain in the stack.
- `To Palette` copies the first used color from channel `0` to each object's wire color.
- `Copy Vertex` copies the first used channel `0` color from the first selected mesh to a shared in-session color buffer.
- `Copy Palette` copies the wire color from the first selected mesh to the same buffer.
- `Paste Vertex` fills channel `0` on every selected mesh from the buffered color, using the same stack-safe bake as `To Vertex`.
- `Paste Palette` assigns the buffered color to the wire color of every selected mesh.
- `Enable VertColor` enables shaded display of Vertex Color channel `0`.
- `Disable VertColor` returns the selected objects to normal viewport shading.

## Bone display tools

`BONE TOOLS` is collapsed by default. Its operations change only the selected `BoneGeometry` nodes and each operation has a single Undo step:

- `Fit Selected to Hierarchy` sets each selected bone's visual length to the average pivot distance to its direct bone children. A selected leaf bone uses half the distance to its parent pivot.
- `Length (cm)` + `SET` assigns the entered visual length in centimeters to every selected bone, independent of the scene's system-unit display.
- `Flip Selected X` reverses the selected bones' display geometry along local X. Node transforms, pivots, hierarchy, and names stay unchanged. Legacy negative-length flips are converted to positive-length rotated display geometry so the bone base remains convex.

These controls are display-authoring helpers. They do not rename bones, relink the skeleton, or alter animation transforms.
