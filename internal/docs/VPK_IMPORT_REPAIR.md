# VPK Import and Animation Repair Plan

## Purpose

Add a second project-entry workflow to Deadlimit Manager for existing Deadlock mods:

```text
Import existing pak##_dir.vpk
→ adopt it as a Deadlimit project
→ preserve its compiled payload
→ inspect compiled character models
→ repair animation bindings against current retail Deadlock
→ rebuild the same VPK slot
→ test in retail Deadlock
```

This feature is intended to let Deadlimit Manager repair the class of model-replacement mods whose character animation bindings become invalid after Deadlock updates, while preserving the imported mod's payload as closely as possible.

## Scope boundary

The first implementation repairs **compiled model animation bindings** only.

Primary repair targets:

- `m_animGraph2Refs`
- `m_vecNmSkeletonRefs`

The current retail Deadlock model for the same hero/model family is the source of truth for these values.

This is not initially defined as a universal animation repair system. Other failure classes may include custom clips, IK chains, root motion, custom skeleton authoring, timing/FPS mismatches, unsupported compiled animation formats, or other resource changes. Those require separate evidence before becoming part of this pipeline.

## Existing confirmed foundation

Deadlimit Manager already has the major required primitives:

- VPK read support through ValvePak.
- VPK write/deploy support through ValvePak.
- hero/model discovery inside retail VPKs.
- direct retail addon deployment to `game/citadel/addons/pak##_dir.vpk`.
- Release ID ownership protection.
- ValveResourceFormat dependency for compiled Source 2 resources.
- a working post-compile AG2/NmSkeleton restoration stage through DeadlockTools.

The existing authoring pipeline must remain unchanged for projects created through the normal **Create Project** path.

## Execution rule

Implement and validate this plan strictly in order.

For each numbered stage:

```text
one implementation/check
→ result
→ conclusion
→ accept or revise
→ only then continue to the next stage
```

Do not combine later stages into an earlier implementation unless required by a discovered dependency.

---

## Stage 1 — Project creation choice

Status: **IMPLEMENTED — awaiting manual UI acceptance**

Change the Library `+` action from directly opening the new-project dialog to opening a small choice dialog first.

Actions:

- **Create Project**
- **Import VPK...**

Requirements:

- `Create Project` must continue into the current `NewProjectFolderDialog` workflow with no behavioral regression.
- `Import VPK...` must begin the new import path.
- The dialog must follow the current Deadlimit UI/theme/localization conventions.

Acceptance:

- Existing project creation behaves exactly as before after choosing `Create Project`.
- `Import VPK...` can be selected independently.

Implementation note:

- The Stage 1 import branch intentionally stops at an explicit boundary message. The VPK picker and validation belong to Stage 2 and are not pulled forward into this stage.

---

## Stage 2 — VPK picker and source validation

Status: **TODO**

Open the file picker by default in:

```text
<Retail Deadlock>\game\citadel\addons\
```

Filter/select existing VPK directory archives such as:

```text
pak42_dir.vpk
```

Requirements:

- Reject unsupported or unreadable VPKs safely.
- Read the archive with the existing ValvePak dependency.
- Derive `ReleaseTarget` from `pak##_dir.vpk` when the filename matches the retail addon slot convention.
- Preserve the original source VPK path and SHA-256 in project metadata.

Acceptance:

- Selecting `pak42_dir.vpk` produces Release ID `42` without manual entry.
- Invalid files do not create a partial project.

---

## Stage 3 — Imported project identity

Status: **TODO**

Inspect the archive and infer the most representative project identity.

Preferred identity evidence, in order:

1. a primary hero `.vmdl_c` under `models/heroes/`, `models/heroes_wip/`, or `models/heroes_staging/`;
2. a uniquely dominant model-replacement path;
3. another stable archive-derived resource identity;
4. fallback to a sanitized VPK-derived name if no model identity can be established safely.

Requirements:

- Reuse or extract the existing hero/main-model scoring logic where appropriate instead of duplicating incompatible heuristics.
- Set `Hero` automatically only when confidence is sufficient.
- Set the project folder/name from the inferred identity.
- User must still be able to rename the project afterward through the normal Library workflow.

Acceptance:

- A normal single-hero replacement VPK imports under the expected hero/project identity.
- Ambiguous archives receive a stable fallback name rather than a guessed hero.

---

## Stage 4 — Imported project mode and manifest contract

Status: **TODO**

Add an explicit project mode distinction.

Minimum modes:

```text
Authoring
ImportedVpk
```

The manifest schema must be bumped if the serialized contract changes.

Imported-project metadata must retain at least:

- source VPK filename;
- source VPK path;
- source Release ID when known;
- original VPK SHA-256;
- inferred hero or heroes;
- detected primary model resources;
- import timestamp/version information sufficient for diagnostics.

Requirements:

- Existing manifests continue to load as `Authoring` by default.
- Imported projects cannot accidentally enter the normal DMX/ResourceCompiler authoring path.

Acceptance:

- Existing projects load without migration breakage.
- Imported VPK projects are unambiguously identified after restarting Deadlimit Manager.

---

## Stage 5 — Preserve compiled payload

Status: **TODO**

Extract the imported VPK into a dedicated payload subtree rather than mixing archive contents with Deadlimit metadata.

Preferred structure:

```text
<ProjectFolder>\
  payload\
    models\...
    materials\...
    ...
  .deadlimit\
    project.json
    original-vpk.json
    ...
```

The exact payload folder name may change if a stronger existing workspace convention is identified before implementation.

Requirements:

- Do not decompile/recompile imported compiled resources during import.
- Do not place `.deadlimit` metadata inside the future VPK payload.
- Record the original archive entry set and per-entry SHA-256.
- Preserve internal VPK paths exactly.

Acceptance:

- Repacking an untouched imported payload produces the same internal path set as the source archive.
- Deadlimit metadata is never included in the rebuilt VPK.

---

## Stage 6 — Release ID adoption and ownership

Status: **TODO**

Imported retail addon VPKs must become safely owned by the importing project without weakening the existing overwrite guard.

Requirements:

- If the selected source is `pak##_dir.vpk`, derive the candidate Release ID from the filename.
- Record the imported source SHA-256 before any deployment mutation.
- Extend `VpkSlotOwnershipService` with an explicit import/adopt operation.
- Adoption is valid only when the VPK currently occupying the slot matches the imported source identity expected by the project.
- Never silently claim an unrelated VPK at the same Release ID.

Acceptance:

- Importing the current `pak42_dir.vpk` lets that project later redeploy slot 42.
- Replacing `pak42_dir.vpk` externally with unrelated bytes restores the ownership conflict instead of overwriting it.

---

## Stage 7 — Detect repair targets

Status: **TODO**

Identify compiled character model resources in the imported payload that are eligible for binding repair.

Requirements:

- Prefer exact model-path correspondence with current retail resources.
- Reuse existing hero/model discovery rules where appropriate.
- Do not apply a hero-specific workaround to unrelated models merely because filenames look similar.
- Produce a repair inspection result before modifying bytes.

The result should distinguish at least:

```text
matched retail model
missing retail counterpart
bindings already current
bindings missing
bindings differ
unsupported/unreadable resource
```

Acceptance:

- A known replacement main model resolves to the corresponding current retail model.
- Non-model payload entries are ignored by this repair stage.

---

## Stage 8 — Repair AG2/NmSkeleton bindings from retail truth

Status: **TODO**

For each confirmed repair target:

1. load the imported compiled `.vmdl_c`;
2. load its exact current retail counterpart;
3. read retail:
   - `m_animGraph2Refs`
   - `m_vecNmSkeletonRefs`;
4. compare with imported values;
5. replace imported values when missing or different;
6. serialize the modified compiled model.

Requirements:

- Retail model values are authoritative.
- Do not rely on `DeadlockTools add ag2` alone for imported VPK repair because it skips fields that already exist.
- If an imported field is stale but present, it must still be replaceable.
- Do not mutate unrelated compiled fields.
- Record which resource paths changed and why.

Acceptance:

- A model with missing bindings receives the current retail bindings.
- A model with stale existing bindings receives the current retail bindings instead of being skipped.
- A model whose bindings already match retail remains byte-unmodified if serialization is unnecessary.

---

## Stage 9 — Repack integrity and verification

Status: **TODO**

Rebuild the imported project from the preserved compiled payload using the existing ValvePak writer and verification path.

Requirements:

- Package the payload subtree only.
- Preserve the same internal path set unless the user intentionally changed payload contents.
- Compare against the import manifest before deploy.
- Report exactly which entries changed because of repair.
- Verify the generated VPK before replacing the retail destination.
- Preserve source VPK version when practical and supported; otherwise document and validate the chosen output version.

Acceptance:

- Untouched entries retain their original SHA-256.
- Only intended repaired resources differ.
- Output VPK verification succeeds before deployment.

---

## Stage 10 — Imported-project Build/Test path

Status: **TODO**

Route `ImportedVpk` projects through a compiled-payload build path instead of the normal authoring pipeline.

Imported flow:

```text
validate imported project
→ inspect payload
→ repair eligible compiled models
→ verify payload invariants
→ pack VPK
→ verify VPK
→ deploy adopted Release ID
→ test in retail Deadlock
```

Must not run by default:

- `PrepareAuthoringService`;
- DMX preparation;
- ModelDoc authoring generation;
- ResourceCompiler recompilation of the imported mod.

Acceptance:

- Existing authoring projects still use the current BUILD & TEST pipeline unchanged.
- Imported projects never enter DMX/ResourceCompiler stages merely to repair bindings.

---

## Stage 11 — Real broken-mod validation

Status: **TODO**

Before treating animation repair as proven, validate on at least one real VPK that is currently broken after a Deadlock update.

Required experiment:

1. capture source VPK hash and entry manifest;
2. identify the broken model;
3. inspect its AG2/NmSkeleton values;
4. inspect the current retail counterpart;
5. record the exact difference;
6. run only the binding repair;
7. rebuild and deploy;
8. test the mod in the retail Deadlock client.

Acceptance:

- The previously broken character animation works in retail after repair.
- The changed archive entries are explainable and limited to the intended repair.

If the mod remains broken:

- do not broaden the automatic repair immediately;
- classify the next failure first;
- only add another repair rule when a concrete cause is demonstrated.

---

## Stage 12 — Secondary repair profiles

Status: **BLOCKED until Stage 11 evidence**

Possible later repair profiles include:

- `unitstatus` normalization equivalent to DeadlockTools `fix unitstatus`;
- detection/reporting of likely harmful custom packed `vnmskel_c` resources;
- detection/reporting of unsupported or obsolete custom `vnmclip_c` resources;
- other current-Deadlock compiled-resource compatibility repairs proven by real failures.

These are not part of the initial animation-binding fix and must not be enabled speculatively.

---

## Product wording

Until Stage 11 proves the repair class in retail, avoid claiming a universal **Fix Animations** feature.

Preferred technical description:

> Repair model animation bindings against current Deadlock resources.

User-facing wording may become shorter after the behavior is validated, but the implementation must continue to distinguish binding repair from unrelated animation problems.

## Safety invariants

The imported-VPK path must preserve all of these properties:

1. Existing authoring projects behave exactly as before.
2. Imported compiled resources are not unnecessarily decompiled/recompiled.
3. Deadlimit metadata never enters the rebuilt VPK.
4. Original source VPK identity is recorded before mutation.
5. Release ID ownership protection remains active.
6. Retail resources are the authority for current animation bindings.
7. Only evidence-backed repair rules are automatic.
8. Every rebuilt VPK is verified before deployment.
9. Every changed compiled resource is reportable.
10. Hero-specific exceptions do not become global rules without separate validation.
