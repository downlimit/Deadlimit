# Vertex Color sidecars

## Purpose

Wall Worm 7.35.1 treats `color:0` as a disabled color stream even though 3ds Max stores painted vertex color in map channel `0`. Deadlimit keeps the normal Wall Worm exporter UI as the owner of the artist DMX and uses a companion file only as a validated color donor.

```text
<name>.dmx
<name>_vertexcolor.dmx
```

The sidecar is also DMX22. Using the same exporter for both files avoids FBX vertex splitting, reindexing and a second geometry parser.

## Artist workflow

1. Save the Deadlimit project.
2. Press **MAX VERTEX COLOR / VERTEX COLOR ИЗ MAX** in Deadlimit.
3. Paste the copied `fileIn @"..."` command into MAXScript Listener once.
4. Export the normal DMX with the Wall Worm window and all desired Wall Worm settings.
5. Keep the same geometry objects selected.
6. Press **EXPORT SELECTED VERTEX COLOR** in the small Max window.
7. Run PREPARE or BUILD & TEST normally.

The Max helper finds the most recently modified primary DMX in the current project root and writes the sidecar beside it. Files already ending in `_vertexcolor.dmx` are excluded from primary-file discovery.

## Max-side safety

The helper:

- accepts selected geometry objects only;
- requires at least one selected object with map channel `0`;
- finds a map channel unused by every selected mesh in `2..99`;
- temporarily adds Wall Worm `ChannelMod` from `0` to the shared temporary channel on each color-bearing selected mesh;
- calls `WallWormS2DMXExport` with `color:<temporary channel>`;
- validates `color$0` and `color$0Indices` before replacing the previous sidecar;
- removes every temporary modifier and restores the original selection on success or failure;
- removes a uniquely named stale Deadlimit bridge from selected meshes before a later retry, covering an interrupted previous export.

Channel `0`, the final modifier stacks and the artist DMX are preserved. During the sidecar export the selected stack contains the temporary bridge because this is required for Wall Worm to retain the original skinned-mesh topology.

## PREPARE validation and fallback

`*_vertexcolor.dmx` files are excluded from normal artist-DMX overlays and material scanning. For each primary DMX, Deadlimit looks for the matching sidecar and applies it only to the copied CSDK DMX.

Transfer requires:

- matching DMX format and format version;
- equal uniquely named `DmeMesh` sets;
- equal face-set count, material order and polygon sequence for every mesh;
- matching indexed surface attributes (`position`, normals, UVs and any additional indexed streams) for every polygon vertex;
- unambiguous direct attributes such as skin joint indices and weights when surface-identical vertices exist;
- color-index count equal to the corner-index count;
- every color index inside the color array;
- sidecar modification time at least as new as the primary DMX.

Vertex Color seams can split the sidecar's logical vertices. After topology validation, Deadlimit expands the prepared DMX's original position/normal/UV/skin streams onto that split vertex domain, preserves their original values, copies the sidecar color stream and updates face indices. This allows skinned meshes to keep their original joint data while receiving per-corner color.

All checks complete before the prepared DMX is saved. A missing, stale, malformed or mismatched sidecar leaves the copied artist DMX unchanged. PREPARE continues and records the skip reason in its log and completion summary.

Both keyvalues2 and binary DMX inputs are loaded and written through Datamodel.NET. The artist project-root DMX is always preserved byte-for-byte.
