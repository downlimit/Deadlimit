# Deadlimit Manager — imported VPK repair

## Purpose

Deadlimit Manager can import an existing Deadlock addon VPK as a separate project and
repair the compiled-model animation bindings that commonly become stale after game
updates.

This path is deliberately separate from normal DMX/CSDK authoring.

```text
Import existing pak##_dir.vpk
→ preserve compiled payload
→ identify project / hero / primary model
→ adopt the existing release slot safely
→ inspect compiled character models
→ compare AG2 / NmSkeleton bindings with current retail resources
→ repair only missing or different bindings
→ verify the preserved payload
→ rebuild and verify the VPK
→ deploy back to the adopted slot
```

## Scope

The automatic repair currently targets these compiled model fields:

- `m_animGraph2Refs`
- `m_vecNmSkeletonRefs`

The exact current retail model at the same Source 2 resource path is the authority.

This is **not** a universal animation fixer. Custom clips, IK behavior, custom
skeleton authoring, root motion, timing, unsupported resource formats, and unrelated
mod breakage remain outside this repair rule unless separate evidence establishes a
specific mechanism.

## Project creation

The Library `+` action offers:

- **CREATE PROJECT**
- **IMPORT VPK...**

The import picker opens in the configured Deadlock addons folder when available and
accepts VPK directory archives such as `pak42_dir.vpk`.

Before creating a project, Deadlimit Manager:

1. validates that the selected archive is readable;
2. records its SHA-256 and entry count;
3. derives the release slot from `pak##_dir.vpk` when possible;
4. infers the most defensible hero/project identity from compiled model paths;
5. revalidates the source VPK before extraction so a file changed during import is
   rejected instead of producing a partial project.

Imported projects use the explicit manifest mode `ImportedVpk`. Existing projects
remain normal authoring projects.

## Preserved payload

The archive is extracted without decompiling or recompiling its contents:

```text
<Project>\
  payload\
    models\...
    materials\...
    ...
  .deadlimit\
    project.json
    original-vpk.json
    repair-inspection.json
    repair-report.json
    repack-report.json
```

`original-vpk.json` records the original archive identity, VPK version, internal
path set, per-entry SHA-256, and size.

Extraction uses a staging directory and publishes the payload only after the complete
entry set has been read successfully. Windows-path collisions and unsafe internal paths
fail closed.

## Release-slot adoption

If the imported source is an existing `pak##_dir.vpk`, Deadlimit Manager may adopt
that slot for the imported project.

Adoption happens only after the raw payload snapshot exists. The current deployed VPK
must still match the imported source identity before ownership is recorded. Replacing
the slot externally with unrelated bytes restores the normal ownership conflict instead
of allowing a silent overwrite.

## Repair inspection

Imported character models under the supported hero resource roots are matched against
the **exact same resource path** in current retail Deadlock.

Each model is classified as one of:

```text
BindingsAlreadyCurrent
BindingsMissing
BindingsDiffer
MissingRetailCounterpart
UnsupportedOrUnreadable
```

Ambiguous current-retail matches are rejected. The retail addons folder is excluded
from the source-of-truth scan so an installed mod cannot become its own repair
authority.

Inspection is read-only and writes `.deadlimit/repair-inspection.json`.

## Animation-binding repair

Only `BindingsMissing` and `BindingsDiffer` targets are eligible.

For each eligible model Deadlimit Manager:

1. rereads the exact retail counterpart;
2. verifies that the retail bindings did not change since inspection;
3. replaces the imported model's AG2/NmSkeleton values with the current retail values;
4. leaves unrelated compiled fields untouched;
5. writes the repaired model atomically;
6. reinspects the result and requires the repaired target to compare as
   `BindingsAlreadyCurrent`.

If verification fails, committed payload changes are rolled back. Models whose
bindings already match retail are not serialized or rewritten.

The repair report records the before/after SHA-256 and whether each target changed.

## Repack verification

Repacking uses ValvePak and treats the original VPK snapshot as an invariant.

Before accepting an output Deadlimit Manager verifies that:

- the payload contains the same internal path set as the imported archive;
- an entry differs only when a recorded repair explains the change;
- unchanged entries retain their original bytes/hash;
- the rebuilt archive exposes the expected path set;
- VPK hashes/checksums verify;
- bytes read back from the rebuilt archive match the verified payload;
- the original VPK version is preserved when supported.

The verified rebuild is committed from a staging VPK family so an incomplete package
does not replace the accepted output.

## BUILD FOR TEST behavior

Imported projects use `ImportedVpkBuildAndTestService`.

They **do not** run:

- normal authoring PREPARE;
- DMX preparation;
- ModelDoc generation;
- ResourceCompiler recompilation of the imported mod.

The imported build path validates project/slot state, repairs eligible bindings,
rebuilds and verifies the VPK, deploys transactionally to the adopted retail slot, and
records deployment ownership.

Authoring-only actions are disabled while an imported VPK project is selected.

## Evidence and limits

The import, inspection, repair, repack, ownership, and deployment paths are implemented
and covered by dedicated smoke tests in the repository.

A successful binding repair proves only that the supported AG2/NmSkeleton fields were
brought in line with the current retail model. If a mod is still broken afterward,
classify the next failure before adding another automatic repair rule.

Hero-specific fixes must not be generalized merely because filenames or symptoms look
similar.
