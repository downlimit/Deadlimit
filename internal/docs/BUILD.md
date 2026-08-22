# Deadlimit — Prepare / compile evidence

This file records concrete local build results. Hero-specific observations remain scoped to the tested project until separately validated.

## 2026-08-22 — first successful headless compile experiment

### Confirmed by our pipeline

A real artist project completed the earlier headless Stage 1B experiment from the Deadlimit GUI.

Observed result:

```text
Addon: ivymason
Top-level artist DMX inputs: 1
Automatic material-path remaps: 3
AG2/NmSkeleton: restored
Skeleton reference used:
models/heroes_staging/tengu/tengu_v2/dmx/mesh/ivy.vnmskel
```

The `.vnmskel` path above is evidence for the tested Ivy project only. Its `heroes_staging/tengu/tengu_v2/...` location must not be generalized to other heroes.

This successful direct compile remains useful evidence, but the authoring workflow has since been changed: `PREPARE FOR CSDK` prepares `content` only and leaves normal authoring compilation to CSDK12.

## 2026-08-22 — failed minimal-VMDL approach

Repeated CSDK visual tests showed that rebuilding a small VMDL from scratch was too destructive:

- the model could render, but Ivy's eyes were black;
- the sequence/animation picker disappeared;
- preserving `BoneMarkupList`, then full retail `Skeleton + AttachmentList`, did not fix the black eyes;
- adding `NmSkeletonList` and `AnimGraph2List` directly to the source VMDL caused the current Reduced CSDK12 ResourceCompiler to reject the document.

Confirmed compiler error:

```text
Failed to allocate an instance of class 'NmSkeletonList'
Failed to allocate an instance of class 'AnimGraph2List'
```

Current Deadlock animation guidance independently requires clearing those two ModelDoc nodes before the relevant CSDK12/AG2 workflow.

Conclusion: Deadlimit must stop synthesizing a fresh approximation of the retail ModelDoc. The extracted retail VMDL is the authoring template; Deadlimit should apply narrow structural edits to it.

## 2026-08-22 — known-good Ivy files supplied from the older manual workflow

The user supplied two files that are known to render correctly in Asset Browser/ModelDoc and do not exhibit the black-eye problem:

```text
ivy.vmdl
ivy_ivy.dmx
```

The attached DMX is binary DMX encoding 9, model format 22. The attached VMDL is a ModelDoc28 document.

### Confirmed structure of the known-good VMDL

Root child classes, in source order:

```text
BoneMarkupList
RenderMeshList
MaterialGroupList
BodyGroupList
LODGroupList
AttachmentList
WeightListList
AnimationList
AnimConstraintList
GameDataList
HitboxSetList
Skeleton
PhysicsShapeList
```

Notably absent:

```text
NmSkeletonList
AnimGraph2List
```

This matches the earlier remembered manual workaround: remove the two current-CSDK-incompatible AG2 source nodes, keep the rest of the retail ModelDoc.

The known-good `RenderMeshList` contains six retail source meshes:

```text
ivy
 gun
ivy_lod1
gun_lod1
ivy_lod2
gun_lod2
```

The corresponding `BodyGroupList` and `LODGroupList` reference those names. Therefore replacing the whole `RenderMeshList` with a single artist mesh breaks an internally coherent retail structure even when the artist only modified the main body mesh.

The known-good `AnimationList` contains 254 `AnimFile` entries pointing to source DMX animations such as:

```text
models/heroes_wip/ivy/ivy_primary_run_e.dmx
models/heroes_wip/ivy/ivy_primary_run_w.dmx
...
```

This is direct evidence for why the old prepared model exposed a sequence list in the authoring tools: the normal retail `AnimationList` and its DMX source files were retained.

### Confirmed material evidence and black-eye fix

The known-good `MaterialGroupList` contains five remaps. Four are decompiler/import compatibility mappings and one is the project custom material mapping.

Relevant non-custom remaps include:

```text
materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat
→ models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat

materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_gearv3.vmat
→ models/heroes_staging/tengu/tengu_v2/materials/ivy_gearv3.vmat

materials/models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3.vmat
→ models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3.vmat

materials/dev/vertcolor_pbr_basic.vmat
→ models/heroes_staging/tengu/tengu_v2/materials/ivy_bodyv3.vmat
```

The supplied DMX contains the mesh/string identifiers `ivy_eyes` and `ivy_eyes_mesh`, plus `materials/dev/vertcolor_pbr_basic.vmat`. It does not contain `eyeball_l`, `eyeball_r`, or `eye_target` bone-name strings.

This is strong direct evidence that the previously working Ivy black-eye correction was the VMDL material remap from the generic dev material to the retail body material, rather than an eye-helper-bone workaround.

The previous Deadlimit generator discarded the retail `MaterialGroupList` and recreated only the three `materials/models/... → models/...` remaps. That explains why it consistently reported exactly three remaps while the known-good VMDL has the additional dev-material remap required by the eye mesh.

### Corrected authoring invariant

Deadlimit must preserve the retail `MaterialGroupList` and merge project/path additions into it. Replacing the table destroys decompiler-authored compatibility redirects that are not inferable from a simple material-path scan.

This is a generic rule; it does not hardcode Ivy's eye material.

## Current authoring strategy

`PREPARE FOR CSDK` now follows this structure:

```text
0source retail model source tree
→ copy to CSDK content at the original resource path
→ preserve original VMDL header/version
→ preserve original root-node ordering and all compatible retail nodes
→ remove only proven current-CSDK-incompatible NmSkeletonList / AnimGraph2List
→ preserve retail RenderMeshList / BodyGroupList / LODGroupList
→ overlay artist DMX onto the matching original RenderMeshFile resource path
→ preserve retail MaterialGroupList
→ merge only missing Deadlimit material-path repairs
→ leave normal content → game compilation to CSDK12
```

For the current Ivy project the artist file `ivy_ivy.dmx` matches the retail `RenderMeshFile` filename directly, so Deadlimit can replace that generated content copy while keeping gun and LOD meshes untouched.

This approach intentionally reconstructs the known-good manual workflow instead of approximating the model with a newly generated ModelDoc.

## `content` vs `game` ownership

Current rule:

```text
PREPARE FOR CSDK
→ prepares content only
→ does not delete game
→ does not invoke ResourceCompiler
→ does not apply compiled-binary AG2 patches

CSDK12 authoring session
→ compiles prepared content into game as needed
```

A later explicit `RELEASE` / `RELEASE & TEST` transaction may own clean runtime compilation, post-compile AG2 restoration, VPK packaging, and deployment. That is separate from authoring preparation.

## Still unvalidated

- whether the corrected template-preserving prepare reproduces the supplied known-good Ivy behavior in the current CSDK12 build;
- whether the retail MaterialGroupList preservation restores normal Ivy eyes in the new automated flow;
- whether preserving the original AnimationList/source DMX tree restores the sequence picker;
- custom material/texture creation and persistence as an automated Stage 2 feature;
- retail Deadlock loading from a Deadlimit-built VPK;
- the release-time AG2 patch sequence in the new separated authoring/release architecture;
- cross-hero RenderMesh matching and material-remap preservation.
