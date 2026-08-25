# MaxScript VertColor Trans

`DeadlimitVertexColorFBX.ms` exports the selected 3ds Max geometry as `<dmx-name>_vertexcolor.fbx` beside the latest DMX exported by Wall Worm.

Workflow:

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry selected.
3. Run the script and press `EXPORT SELECTED VERTEX COLOR FBX`.
4. Run `PREPARE` in Deadlimit.

During PREPARE, Deadlimit transfers Vertex Color to every DMX mesh whose assigned material name contains `vertexcolor`. A missing channel `0` produces neutral gray `(128, 128, 128, 255)`. The temporary FBX is deleted only after a successful validated transfer.

The MaxScript contains no project path and no path to `Deadlimit.exe`. It only reads Wall Worm's last export folder from `3dsMax.ini`.
