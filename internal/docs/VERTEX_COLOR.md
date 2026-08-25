# Vertex Color FBX sidecars

## Purpose

The artist DMX remains a normal Wall Worm export with all settings controlled by the Wall Worm window. A small universal MAXScript exports the currently selected geometry through the Autodesk FBX exporter so Deadlimit can recover map channel `0` colors during PREPARE.

```text
<name>.dmx
<name>_vertexcolor.fbx
```

## Artist workflow

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry selected in 3ds Max.
3. Run `DeadlimitVertexColorFBX.ms` and press **EXPORT SELECTED VERTEX COLOR**.
4. Run PREPARE or BUILD & TEST normally.

The helper reads Wall Worm's last export folder from `3dsMax.ini`, finds the newest primary DMX there and writes the FBX beside it. It does not read a Deadlimit project, hard-code an asset path or change the normal Wall Worm DMX.

The FBX export is ASCII, selection-only, animation/cameras/lights disabled and triangulation disabled. The helper restores the previous FBX settings and Max selection after success or failure.

## Material priority

A DMX mesh is priority when any assigned material identity contains the exact substring `vertexcolor`, case-insensitively. Deadlimit checks the serialized material element name and its `mtlName` path.

When at least one priority mesh exists:

- every priority mesh must have one exact-name FBX mesh;
- every priority mesh must pass geometry validation and contain a Vertex Color layer;
- a failure on any priority mesh rejects the whole sidecar;
- non-priority meshes are transferred only when their own name, geometry and color layer validate; their absence or mismatch does not reject valid priority meshes.

When no priority material exists, Deadlimit uses strict fallback mode: the entire unique mesh-name set and all mesh geometry must match, and at least one usable color layer must exist.

## PREPARE validation and fallback

For each primary DMX, Deadlimit looks for the sibling FBX and applies colors only to the copied CSDK DMX. The artist DMX remains unchanged.

Validation requires:

- a valid Autodesk ASCII FBX mesh graph;
- unique matching mesh node names after the DMX `_mesh` suffix is removed;
- equal control-point and polygon counts for each transferred mesh;
- equal polygon corner counts and stable polygon order;
- at least 90 percent direct control-point topology anchors, allowing the limited index normalization observed in the Autodesk exporter;
- a supported FBX color mapping (`ByPolygonVertex` or `ByControlPoint`) and reference mode (`Direct` or `IndexToDirect`);
- valid color indices and RGBA values;
- sidecar modification time at least as new as the primary DMX.

FBX per-corner colors can split the DMX logical vertex domain. After validation, Deadlimit expands the prepared DMX position, normal, UV and skin streams onto that domain, preserving their values while adding `color$0` and `color$0Indices`.

All checks complete before the prepared DMX is replaced. A missing, stale, malformed or rejected sidecar leaves the copied artist DMX unchanged, PREPARE continues, and the reason is written to its log and completion summary.
