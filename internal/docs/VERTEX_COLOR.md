# Vertex Color FBX sidecars

## Purpose

The artist DMX starts as a normal Wall Worm export with all settings controlled by the Wall Worm window. A small universal MAXScript exports the currently selected geometry and renderable Shape/Spline objects through the Autodesk FBX exporter. Deadlimit Aggregator consumes that sidecar during PREPARE.

```text
<name>.dmx
<name>_vertexcolor.fbx  (temporary)
```

## Artist workflow

1. Export the normal DMX with Wall Worm.
2. Keep the same geometry and renderable Shape/Spline objects selected in 3ds Max.
3. Run `DeadlimitPipelineScripts.ms`, choose the `FIXED GAMMA` mode, and press **EXPORT VERTEX COLOR FBX**.
4. Run PREPARE normally.

The helper reads Wall Worm's last export folder from `3dsMax.ini` and finds the newest primary DMX there. It does not read a Deadlimit Aggregator project, launch Deadlimit Aggregator, or contain a path to `DeadlimitAggregator.exe`.

The FBX export is ASCII, selection-only, animation/cameras/lights disabled and triangulation enabled. Renderable Shape/Spline objects receive a temporary `Turn To Mesh` modifier before export. `FIXED GAMMA` is on by default; its adjacent GAMMA spinner applies `channel 0 RGB^(GAMMA/2.2)`. The default `1.0` applies the established `1/2.2` Source 2 correction, while `2.2` produces exponent `1.0` and preserves stored RGB. The spinner locks while FIXED GAMMA is off. The helper restores temporary modifiers, previous FBX settings, Max selection, and the scene dirty state after success or failure. During PREPARE, Deadlimit Aggregator patches the CSDK-bound DMX copy and reloads and validates its color streams. An applied `_vertexcolor.fbx` remains beside the artist DMX until the complete PREPARE transaction has saved the VMDL, materials, project metadata and final log. Only then is the temporary FBX removed. Rejection, cancellation, or any later PREPARE failure leaves the FBX available for retry and diagnosis.

Before export, stored Inner Lineart Skin groups on selected meshes are synchronized from each source vertex to its coincident clones. Ordinary weights, DQ masks, normalization, and supported rigid state are included. Stale topology rejects the export. A synchronization that changes clone data deliberately leaves the scene dirty so the repaired weights can be saved.

Inner Lineart's optional Alpha Marker writes black to Vertex Alpha (Max map channel `-2`) on participating corners while preserving Vertex Color RGB (channel `0`). DISPLAY LINEART / DISPLAY VERTCOLOR switches selected meshes between `#alpha` and `#color` viewport display without changing stored values.

The versioned helper and a short README live in `.deadlimit/maxscript-vertcolor-trans/` in the `Deadlimit` repository. Settings exposes `📂 Deadlimit Max Script` to open that folder.

## Material priority

A DMX mesh is selected for transfer when any assigned material identity contains the exact substring `vertexcolor`, case-insensitively. Deadlimit Aggregator checks the serialized material element name and its `mtlName` path.

The transfer contract is all-or-nothing:

- every selected mesh must have one exact-name FBX mesh;
- every selected mesh must pass topology/geometry validation;
- a selected mesh with map channel `0` receives its exported colors;
- a selected mesh without map channel `0` receives neutral gray RGBA `(128, 128, 128, 255)`;
- a failure on any selected mesh rejects the whole sidecar and reports all failed mesh names together;
- meshes whose assigned material names do not contain `vertexcolor` are ignored completely.

If no material name contains `vertexcolor`, the operation fails without changing the DMX.

## Validation and PREPARE

PREPARE applies the sidecar to its copied DMX target. The artist's primary DMX remains a normal Wall Worm export. Rejected sidecars are recorded in the PREPARE log and left beside the artist DMX. Successfully applied sidecars are queued for best-effort deletion after the complete PREPARE succeeds; a cleanup failure is logged and does not invalidate the prepared content.

For multi-color meshes, correspondence is resolved in this order:

1. exact control-point-index topology when DMX and FBX retain the same control-point count and polygon connectivity;
2. transformed polygon positions;
3. split-control-point geometric correspondence;
4. unambiguous per-control-point color correspondence.

The first path intentionally ignores absolute vertex positions. A modifier that deforms, bends, offsets or non-uniformly scales a mesh without changing its control-point numbering or polygon connectivity therefore does not invalidate Vertex Color transfer. Modifiers that add/remove vertices, edges or polygons still fail topology validation unless a later geometric path can prove an unambiguous correspondence.

Validation requires:

- a valid Autodesk ASCII FBX mesh graph;
- unique matching mesh node names after the DMX `_mesh` suffix is removed;
- equal polygon counts and corner counts for each transferred mesh;
- exact indexed polygon topology or a proven transformed/geometric correspondence for multi-color transfer;
- UV-less multi-color meshes when topology or polygon positions correspond;
- direct transfer for uniform-color meshes, where polygon order cannot affect the result;
- rejection of ambiguous coincident geometry carrying different colors;
- tolerance for topology-preserving modifiers even when DMX/FBX position bounds no longer share one uniform scale;
- tolerance for FBX/DMX vertex splitting and different internal triangulation when color remains unambiguous;
- FBX model transforms and coordinate-system conversion before geometric matching;
- a supported FBX color mapping (`ByPolygonVertex` or `ByControlPoint`) and reference mode (`Direct` or `IndexToDirect`);
- valid color indices and RGBA values;
- sidecar modification time at least as new as the primary DMX.

FBX per-corner colors can split the DMX logical vertex domain. After validation, Deadlimit Aggregator expands the prepared DMX position, normal, UV and skin streams onto that domain, preserving their values while adding `color$0` and `color$0Indices`.

All checks and a full DMX reload complete before the prepared DMX target is replaced. A missing, stale, malformed or rejected sidecar leaves the normal prepared copy unchanged.
