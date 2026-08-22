# Deadlimit — Prepare / compile evidence

This file records concrete local build results. Hero-specific observations remain scoped to the tested project until separately validated.

## 2026-08-22 — first successful `PREPARE + COMPILE`

### Confirmed by our pipeline

A real artist project completed the current Stage 1B action from the Deadlimit GUI.

Observed result:

```text
Addon: ivymason
Top-level artist DMX inputs: 1
Automatic material-path remaps: 3
Compiled model:
C:\WorkProjects\Deadlock\Reduced_CSDK_12\game\citadel_addons\ivymason\models\heroes_wip\ivy\ivy.vmdl_c
AG2/NmSkeleton: restored
Skeleton reference used:
models/heroes_staging/tengu/tengu_v2/dmx/mesh/ivy.vnmskel
```

The build therefore confirmed, for this project, that Deadlimit can perform the following sequence without manual CMD work:

```text
artist DMX in project root
→ generated CSDK addon source
→ minimal generated VMDL
→ narrow Wall Worm material remaps
→ bin_cs2 ResourceCompiler
→ expected VMDL_C discovery
→ DeadlockTools add ag2 / NmSkeleton restoration
```

The success dialog reported three material remaps. This validates that the current non-destructive remap mechanism was exercised in a real build; it does not prove that every Wall Worm material-path case is covered.

The `.vnmskel` path above is evidence for the tested Ivy project only. Its `heroes_staging/tengu/tengu_v2/...` location must not be generalized to other heroes.

## 2026-08-22 — first CSDK ModelDoc visual inspection

### Confirmed by our pipeline

The generated model opens and renders in CSDK12 ModelDoc. The preserved/base character materials are broadly visible and the geometry is usable for authoring inspection.

Two visible issues were observed:

1. the character eyes render black;
2. the new costume material is absent, as expected, because no project-owned custom VMAT has been created yet.

The costume-material issue belongs to Stage 2 custom material authoring and is not treated as a build regression.

### Black-eye issue: evidence and current hypothesis

Black-eye rendering is a known class of Source 2 asset-conversion problem. Source 2 has a dedicated eyeball material/shader path and an eye-occlusion rendering system. Current external examples explicitly document that incorrect eye-material occlusion values can produce black-eye artifacts, and Deadlock exposes eye-occlusion runtime controls.

For this Deadlimit build, the exact cause is not yet proven. The strongest current hypothesis is that the minimal generated VMDL preserves the mesh/material reference but omits some eye-specific retail model/material configuration that the original hero model carries.

Do not add a hero-specific Ivy patch or a blanket material override yet.

The universal-fix direction, if the hypothesis is confirmed, is:

```text
detect that the retail model uses eye-specific configuration
→ preserve/transplant the required eye-related model/material data from the extracted retail source
→ keep retail eye material references intact
→ apply only the required eye configuration to the generated VMDL
```

The next diagnostic must discriminate between model-source loss and material/extraction loss. Compare the original decompiled retail VMDL/eye configuration from `0source` with the minimal generated VMDL. If the original decompiled source renders normal eyes while the generated model renders black, the missing VMDL configuration is confirmed. If both render black, investigate the eye material/texture extraction path instead.

### Still unvalidated

- exact eye-specific VMDL/material fields required by current Deadlock heroes;
- whether the same mechanism applies across multiple heroes;
- custom material/texture creation and persistence;
- retail Deadlock loading;
- VPK packaging/deployment;
- AG2/skeleton discovery on another hero.
