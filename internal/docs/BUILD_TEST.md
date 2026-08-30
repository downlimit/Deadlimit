# Deadlimit Aggregator — BUILD & TEST

## 2026-08-23 — accepted iteration workflow

After the initial CSDK authoring/material pass, routine character-skin iteration is a single Deadlimit Aggregator action:

```text
edit project-root DMX / PNG
→ BUILD & TEST
→ verify/repair retail mod loading when necessary
→ verify the configured retail VPK slot is safe to replace
→ prepare/synchronize authoring content
→ compile changed Source 2 assets headlessly
→ restore required AG2/NmSkeleton data on a freshly compiled character model
→ create VPK
→ write VPK directly into retail Deadlock game/citadel/addons
→ optionally launch Deadlock from the completion dialog
```

The authoring path remains separate:

```text
PREPARE FOR CSDK
→ use while changing material/shader/ModelDoc structure
→ leaves a clean authoring state for CSDK tools

BUILD & TEST
→ normal repeated in-game iteration after authoring is established
```

### Retail mod-loading guard

Current Deadlock mod installation guidance still requires this search path in retail `game/citadel/gameinfo.gi`:

```text
Game                citadel/addons
```

Deadlock updates can replace `gameinfo.gi`, so BUILD & TEST now checks this automatically before doing expensive build work.

The user does not get an extra routine confirmation dialog:

```text
entry already exists
→ continue silently

entry missing + normal SearchPaths layout recognized
→ back up gameinfo.gi under project .deadlimit/backups
→ insert Game citadel/addons immediately before Game citadel
→ validate the patched SearchPaths block
→ continue

layout not safely recognized
→ fail closed
→ do not edit retail gameinfo.gi
```

If Deadlock was already running when the search path had to be repaired, that one run requires a game restart because filesystem search paths are mounted by the running process. This is an exceptional post-update repair case, not the normal per-build workflow.

### VPK destination and slot ownership

The project `Release ID` is the retail VPK slot and must be `01` through `99`.

Example:

```text
Release ID: 01
→ <Retail Deadlock>/game/citadel/addons/pak01_dir.vpk
```

Lower VPK numbers have higher override priority according to current Deadlock mod-loading guidance.

Deadlimit Aggregator now tracks ownership of its deployed slot in:

```text
.deadlimit/vpk-deployment.json
```

The record stores the slot, full VPK path and SHA-256 of the last successfully deployed file.

Safety contract:

```text
slot empty
→ safe

slot contains file whose hash matches this project's ownership record
→ safe to replace

slot contains file whose recorded Deadlimit Aggregator hash no longer matches
→ stop; somebody changed the file outside this project

slot contains unknown VPK with no ownership evidence
→ stop; do not overwrite another mod
```

For projects that already completed BUILD & TEST before ownership tracking existed, the presence of the old `.deadlimit/build-test-state.json` is accepted once as migration evidence for the current configured slot. The next successful build records a proper VPK hash.

If the project later changes Release ID, the previous slot is removed automatically only when its file still matches the hash that Deadlimit Aggregator previously recorded. Unknown/modified files are never deleted as cleanup.

### VPK packaging

BUILD & TEST creates the VPK **in-process** through the already embedded ValvePak library rather than launching `CSDKCfgVPK.exe`. Current ValvePak explicitly supports creating new VPK archives with `Package.AddFile(...)` and `Package.Write(...)`; new packages default to VPK version 2.

This change is intentional UX behavior: the external CSDKCfgVPK success MessageBox cannot provide Deadlimit Aggregator-owned actions or progress. In-process packing removes that extra modal window and lets Deadlimit Aggregator own the complete transaction.

Packaging is transactional:

```text
compiled addon game folder
→ build temporary VPK version 2
→ verify archive hashes + file CRCs
→ atomically replace matching retail family files while retaining per-file backups
→ verify archive hashes + file CRCs again from the final retail path
→ on success remove transaction backups and obsolete numeric chunks
→ on any deployment/final-verification error restore the previous VPK family
```

The directory VPK is deployed after any numeric chunks so it acts as the final family commit point. Backup cleanup failures are logged and leave recoverable `.deadlimit-backup-*` files without invalidating the verified deployed archive.

### Completion UX and running Deadlock

Successful BUILD & TEST ends in a Deadlimit Aggregator-owned dialog.

When Deadlock is not running:

```text
OK
LAUNCH DEADLOCK GAME
```

The launch action uses Steam app `1422450`.

When Deadlock is already running, Deadlimit Aggregator does **not** force a restart. The dialog reports that the game is already running and suggests first trying to make the game reload/reselect the hero. The launch button is disabled in that state.

Reason: a mandatory restart for retail model-replacement VPK iteration has not yet been experimentally proven in our pipeline. Source 2 can cache loaded resources, so hot replacement may depend on whether the relevant hero/model is recreated or remains cached. Until tested, Deadlimit Aggregator must not kill a running match/client merely on a hypothesis.

The concrete acceptance experiment is:

```text
run Deadlock with the current skin
→ change a clearly visible texture/model detail
→ BUILD & TEST while Deadlock remains open
→ switch away from the hero / reload the relevant preview or scene
→ select the hero again
→ observe whether the new asset appears
```

If this works consistently, no restart control is needed for normal iteration. If it does not, a restart action can be added later as an explicit fallback, not as the default behavior.

### Overall progress UX

BUILD & TEST reports one overall 0–100 progress value across the whole transaction rather than showing independent per-tool progress.

The window title remains the compact high-visibility status surface and has an animated spinner:

```text
Deadlimit Aggregator — [34% \] - Comparing prepared content...
Deadlimit Aggregator — [56% |] - Compiling Source 2 assets — batch 2/4...
Deadlimit Aggregator — [98% /] - Verifying VPK checksums...
```

Spinner frames rotate as:

```text
|  /  —  \
```

At 100% the spinner becomes a check mark.

A real horizontal progress bar is also shown on the right side of Deadlimit Aggregator's existing status bar while BUILD & TEST is running. The standard Windows/WinForms caption is not custom-drawn, so the title itself stays textual; no fragile custom non-client title-bar rendering is introduced.

Progress weighting is based on real pipeline phases:

```text
0–30   prepare / source synchronization
33–39  diff + clean/incremental decision
40–76  ResourceCompiler batches
79     compiled-output verification
83     AG2/NmSkeleton restoration when needed
90–96  files added to VPK
97     VPK write
98     VPK verification
99     retail deployment
100    success
```

This means compilation and VPK file packing advance according to actual batch/file counts rather than a purely time-based fake animation. The spinner continues moving while an individual external compile/process call is busy.

### Incremental compile contract

The first successful `BUILD & TEST` establishes `.deadlimit/build-test-state.json` as the hash snapshot of prepared addon `content`.

Later runs:

1. preserve the last compiled addon `game` output while normal authoring PREPARE synchronizes DMX/material/texture sources;
2. compare the prepared content hashes against the last successful Build & Test snapshot;
3. compile only changed direct Source 2 inputs;
4. if a DMX dependency changed, force the addon's VMDL source(s) into the compile set;
5. if an image source changed, force the addon's VMAT source(s) into the compile set;
6. if a known source was removed, delete its proven one-to-one compiled output before packing;
7. if a removed source has no proven compiled-output mapping, fall back to a clean addon rebuild instead of risking stale runtime data;
8. save the new hash snapshot only after compilation, required AG2 restoration and VPK packing all succeed.

This is deliberately fail-safe: incremental speed is used only where Deadlimit Aggregator can prove what should be retained or invalidated.

Build-state relative paths are treated as untrusted input. Entries that are rooted or contain an escaping `..` invalidate the snapshot and force a clean build; they are never joined into a deletion target. Source 2 resource paths read from the manifest or VMDL are likewise containment-checked before any addon write.

### Vertex Color warning

BUILD & TEST carries the PREPARE Vertex Color result into its completion dialog. If a DMX references a material whose normalized name contains `vertexcolor` and its sidecar was missing or rejected, the VPK may still be deployed, but the success summary shows a prominent non-blocking warning with the DMX filename and reason. This keeps iteration moving while making loss of expected vertex color visible.

### Atomic state writes

Project/settings JSON, build state, ownership registries, catalog cache and `gameinfo.gi` are written to a temporary file in the same folder, flushed, then atomically replace the previous file. A process crash or full disk before the final replacement leaves the previous committed file intact. `gameinfo.gi` still receives the separate project backup used for manual recovery.

### Forced clean rebuild escape hatch

Normal `BUILD & TEST` remains incremental.

Holding Shift while clicking it temporarily hides the previous build-state snapshot from the build transaction:

```text
SHIFT + BUILD & TEST
→ force first/clean/full rebuild behavior
```

If that forced build fails, the previous incremental snapshot is restored. If it succeeds, the new successful snapshot replaces it.

This is intentionally a hidden recovery gesture rather than another permanent button. The BUILD & TEST tooltip documents it.

### Current directly compiled source classes

The headless compile adapter currently accepts the model-replacement-relevant CSDK source classes already supported by the current ResourceCompiler workflow:

```text
.vmdl
.vmat
.vtex
.vpcf
.vsndevts
.wav
.xml
.css
.js
.vsvg
```

Raw image changes are treated as VMAT dependencies. Project DMX changes are treated as VMDL dependencies.

Compilation is batched at 25 direct inputs and uses the validated CSDK12 `game/bin_cs2/win64/resourcecompiler.exe` with repeated `-i <file>` arguments plus `-nop4`.

### AG2 restoration

Current external Deadlock character-replacement guidance still requires AnimGraph2 data after CSDK12 character-model export; otherwise replacement characters can A-pose.

Deadlimit Aggregator therefore reuses the command shape that was already confirmed by the earlier local Deadlimit pipeline whenever the main VMDL was recompiled:

```text
DeadlockTools add ag2 <compiled vmdl_c>
  -h <project hero>
  -f <family inferred from this project's resource path>
  --override-skeleton <NmSkeleton reference discovered from this project's 0source>
```

The skeleton path is discovered from the current project's extracted retail VMDL. No Ivy-specific skeleton path is hardcoded. `fix unitstatus` remains conditional and is not part of BUILD & TEST.

### Current external evidence

Rechecked 2026-08-23:

- current CSDK12 documents `content/citadel_addons/<addon>` as authoring source and `game/citadel_addons/<addon>` as compiled output;
- current Deadlock installation guidance still requires `Game citadel/addons` in retail `gameinfo.gi`, notes that updates can replace that file, and uses `pak01_dir.vpk` through `pak99_dir.vpk` with lower numbers having higher priority;
- the current ValvePak API supports creating VPKs in-process and defaults new packages to VPK version 2;
- SteamDB's current Windows launch configuration points at `game/bin/win64/deadlock.exe`; the older `project8.exe` process name is also recognized by Deadlimit Aggregator when detecting an already-running client;
- Valve's current official Deadlock Steam page is app `1422450`;
- current Deadlock modding guidance still identifies Dotryen DeadlockTools AG2 restoration as required for CSDK12 character replacements.

### Validation status

Confirmed by the user's live Ivy run on 2026-08-23:

- the earlier BUILD & TEST path successfully compiled and produced a retail `pak01_dir.vpk`;
- the previous external CSDKCfgVPK success dialog appeared, proving direct retail destination and packaging worked before the in-process packer UX replacement.

Implemented after that live proof but **not yet locally acceptance-tested**:

- in-process ValvePak packaging;
- Deadlimit Aggregator-owned completion dialog;
- overall progress bar/spinner;
- automatic `gameinfo.gi` mod-loading guard;
- VPK slot ownership/hash protection;
- Shift forced full rebuild;
- running-Deadlock detection / no forced restart;
- English/Russian UI setting.

The next local acceptance run must verify those changes before they are classified as live-confirmed.
