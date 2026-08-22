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

### Still unvalidated after this build

This compile success does not yet prove:

- that the model renders correctly in ModelDoc/Asset Browser;
- that all material assignments resolve visually;
- that custom materials/textures are prepared correctly;
- that the compiled model works in retail Deadlock;
- that VPK packaging/deployment is correct;
- that AG2/skeleton discovery works for another hero.

The next validation is the intermediate authoring stage: open the generated addon through CSDK12, inspect the model, distinguish reused retail materials from project-owned custom materials, and establish the exact custom VMAT/texture workflow before automating Stage 2.
