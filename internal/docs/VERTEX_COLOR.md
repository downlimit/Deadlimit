# Vertex Color FBX sidecars

## Purpose

The artist DMX starts as a normal Wall Worm export with all settings controlled by the Wall Worm window. A small universal MAXScript exports the currently selected geometry through the Autodesk FBX exporter. Deadlimit consumes that sidecar during PREPARE.

```text
<name>.dmx
<name>_vertexcolor.fbx  (temporary)
```

## Artist workflow

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry selected in 3ds Max.
3. Run `DeadlimitVertexColorFBX.ms` and press **EXPORT SELECTED VERTEX COLOR FBX**.
4. Run PREPARE normally.

The helper reads Wall Worm's last export folder from `3dsMax.ini` and finds the newest primary DMX there. It does not read a Deadlimit project, launch Deadlimit, or contain a path to `Deadlimit.exe`.

The FBX export is ASCII, selection-only, animation/cameras/lights disabled and triangulation enabled. The helper restores the previous FBX settings and Max selection after success or failure. During PREPARE, Deadlimit patches the CSDK-bound DMX copy, reloads and validates its color streams, then deletes `_vertexcolor.fbx` only after that operation succeeds. On rejection PREPARE continues with the normal DMX and leaves the FBX available for diagnosis.

The versioned helper and a short README live in `.deadlimit/maxscript-vertcolor-trans/` in the Deadlimit repository. Settings exposes `📂 MaxScript VertColor Trans` to open that folder.

## Material priority

A DMX mesh is selected for transfer when any assigned material identity contains the exact substring `vertexcolor`, case-insensitively. Deadlimit checks the serialized material element name and its `mtlName` path.

The transfer contract is all-or-nothing:

- every selected mesh must have one exact-name FBX mesh;
- every selected mesh must pass geometry validation;
- a selected mesh with map channel `0` receives its exported colors;
- a selected mesh without map channel `0` receives neutral gray RGBA `(128, 128, 128, 255)`;
- a failure on any selected mesh rejects the whole sidecar and reports all failed mesh names together;
- meshes whose assigned material names do not contain `vertexcolor` are ignored completely.

If no material name contains `vertexcolor`, the operation fails without changing the DMX.

## Validation and PREPARE

PREPARE applies the sidecar to its copied DMX target. The artist's primary DMX remains a normal Wall Worm export. Rejected sidecars are recorded in the PREPARE log and left beside the artist DMX.

Validation requires:

- a valid Autodesk ASCII FBX mesh graph;
- unique matching mesh node names after the DMX `_mesh` suffix is removed;
- equal polygon counts and corner counts for each transferred mesh;
- direct topology, UV/color correspondence, or transformed geometric control-point correspondence;
- tolerance for FBX/DMX vertex splitting and different internal triangulation when color remains unambiguous;
- FBX model transforms and coordinate-system conversion before geometric matching;
- a supported FBX color mapping (`ByPolygonVertex` or `ByControlPoint`) and reference mode (`Direct` or `IndexToDirect`);
- valid color indices and RGBA values;
- sidecar modification time at least as new as the primary DMX.

FBX per-corner colors can split the DMX logical vertex domain. After validation, Deadlimit expands the prepared DMX position, normal, UV and skin streams onto that domain, preserving their values while adding `color$0` and `color$0Indices`.

All checks and a full DMX reload complete before the prepared DMX target is replaced. A missing, stale, malformed or rejected sidecar leaves the normal prepared copy unchanged.
