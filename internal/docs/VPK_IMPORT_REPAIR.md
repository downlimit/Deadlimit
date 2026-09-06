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

Status: **TODO**

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
- Ambiguous multi-hero or non-character VPKs do not receive a false hero identity.

---

## Stage 4 — Preserve the imported compiled payload

Status: **TODO**

Do not decompile and recompile the imported mod as part of the normal import transaction.

Store the exact archive payload under a dedicated project subdirectory:

```text
<ProjectFolder>\payload\...
```

Do not place imported archive files directly beside project metadata and authoring inputs in the project root.

Requirements:

- Preserve archive-relative resource paths exactly.
- Store a per-file import manifest containing at least relative path, size, and SHA-256.
- Record the original VPK SHA-256.
- Keep `.deadlimit` metadata outside the payload set that will later be repacked.

Acceptance:

- The extracted `payload` file set hashes back to the original archive contents.
- Deadlimit metadata cannot accidentally enter the rebuilt VPK.

---

## Stage 5 — Adopt the original VPK slot safely

Status: **TODO**

Extend the current VPK ownership model with an explicit import/adoption transaction.

Current behavior correctly refuses to overwrite an unknown existing `pak##_dir.vpk`; imported projects need a controlled exception because that exact file is their source artifact.

Requirements:

- Adoption is permitted only during successful VPK import.
- Record the imported VPK path, Release ID, and original SHA-256.
- After adoption, normal Deadlimit ownership checks apply.
- If the VPK changes externally after import, the existing external-modification protection must still stop automatic overwrite.

Acceptance:

- An imported `pak42_dir.vpk` can later deploy back to slot `42`.
- Another project cannot silently claim or overwrite that slot.
- External edits after adoption are detected.

---

## Stage 6 — Detect repairable compiled models

Status: **TODO**

Scan the imported payload for candidate `.vmdl_c` resources.

For each candidate, determine whether it is a character/model resource eligible for animation-binding repair.

Inspect at minimum:

- resource type is Model;
- current `m_animGraph2Refs` state;
- current `m_vecNmSkeletonRefs` state;
- resource path / hero-family identity.

Requirements:

- Do not patch every `.vmdl_c` indiscriminately.
- Classify candidates as repairable, already current, ambiguous, or unsupported.
- Ambiguous candidates remain untouched and must be reported rather than guessed.

Acceptance:

- A known hero main model is selected for inspection.
- unrelated compiled models remain byte-identical.

---

## Stage 7 — Resolve the current retail reference model

Status: **TODO**

For each repairable imported model, locate the corresponding model in the user's current retail Deadlock installation.

The current retail compiled model is the authoritative source for animation binding values.

Requirements:

- Prefer exact resource-path identity where possible.
- Support hero internal-name/family resolution when exact paths changed.
- Reuse existing retail VPK scanning infrastructure.
- Do not copy geometry, materials, render meshes, or unrelated model data from retail.

Acceptance:

- The system resolves the correct current retail model for tested imported replacements.
- Failure to resolve produces a non-destructive error/warning and no guessed patch.

---

## Stage 8 — Replace stale or missing animation bindings

Status: **TODO**

Read the current retail values for:

```text
m_animGraph2Refs
m_vecNmSkeletonRefs
```

Compare them with the imported model.

Repair policy:

- missing imported value + valid retail value → copy retail value;
- imported value differs from valid current retail value → replace with retail value;
- imported value already matches retail → leave model untouched;
- retail reference cannot be established safely → do not modify.

This stage intentionally differs from the current `DeadlockTools add ag2` behavior, which only adds missing fields and skips fields that already exist. Imported old mods may contain stale existing references, so repair must support replacement.

Requirements:

- Modify only the required model DATA fields.
- Preserve all unrelated model data.
- Log the exact fields changed per resource.
- Keep an untouched backup/hash reference from the import manifest.

Acceptance:

- Missing refs are restored.
- Stale refs are replaced with current retail refs.
- Already-current models remain byte-identical whenever no serialization is required.

---

## Stage 9 — Repack only the imported payload

Status: **TODO**

Build the VPK directly from the imported `payload` tree after repair.

Import-project Build & Test path:

```text
payload
→ inspect/repair compiled resources
→ pack VPK
→ verify VPK
→ deploy to adopted Release ID
```

The normal authoring path remains:

```text
project authoring sources
→ PrepareAuthoringService
→ ResourceCompiler
→ post-process compiled model
→ pack VPK
→ deploy
```

Requirements:

- Imported projects must not run through `PrepareAuthoringService` or ResourceCompiler merely to rebuild the archive.
- The output archive must contain the same relative file set as the imported archive unless a future explicitly accepted feature changes that rule.
- Only repair-target files may differ in content.

Acceptance:

- File set before/after rebuild is identical.
- Hash differences are limited to intentionally repaired resources.

---

## Stage 10 — Verification and deployment

Status: **TODO**

Before replacing the retail addon VPK:

- verify the temporary VPK can be read;
- verify archive-relative file set matches the import manifest;
- verify untouched resources retain their imported SHA-256;
- record all deliberately changed resources;
- deploy through the existing verified VPK deployment mechanism;
- update the project's ownership hash after successful deployment.

Acceptance:

- A failed verification never replaces the existing retail VPK.
- A successful rebuild becomes the new owned VPK state for the project.

---

## Stage 11 — Real broken-mod validation

Status: **TODO**

Validate the feature against real mods that currently exhibit broken character animations after a Deadlock update.

Required test matrix:

1. mod with missing AG2/NmSkeleton refs;
2. mod with existing but stale refs;
3. already-working/current mod;
4. mod whose animation problem is unrelated to these bindings;
5. ambiguous or multi-model VPK.

For each fixture record:

- original VPK hash;
- imported candidate model(s);
- fields detected before repair;
- current retail reference fields;
- fields changed;
- rebuilt VPK hash;
- in-game result.

Acceptance:

- At least one previously broken real mod is restored in retail Deadlock by the binding repair.
- An already-working mod is not unnecessarily modified.
- An unrelated animation failure is reported as not repaired rather than falsely declared fixed.

---

## Stage 12 — Product wording and final UX

Status: **TODO**

Only after Stage 11 confirms the supported failure class, finalize user-facing wording.

Preferred technical description:

> Repair model animation bindings against the current Deadlock retail resources.

Avoid claiming universal animation repair unless additional failure classes have been independently implemented and validated.

Potential imported-project status information:

- imported from `pak##_dir.vpk`;
- Release ID;
- detected hero/model identity;
- last repair result;
- number of repaired model resources;
- current/changed externally state.

Acceptance:

- UI wording accurately reflects the validated repair scope.
- The normal authoring-project UX remains unaffected.

## Non-goals for the first implementation

- rebuilding arbitrary source animation assets;
- automatically repairing custom IK authoring;
- regenerating custom `vnmclip` data;
- repairing root-motion authoring;
- recompiling an imported mod through CSDK by default;
- changing unrelated assets while repairing animation bindings;
- guessing hero/skeleton identities when evidence is ambiguous.

## Completion definition

This plan is complete when an existing retail-addon VPK can be imported as a Deadlimit project, safely adopted in its original Release ID slot, preserved as a compiled payload, compared against current retail character animation bindings, minimally repaired where necessary, verified, repacked, deployed, and confirmed in-game on real previously broken mods.