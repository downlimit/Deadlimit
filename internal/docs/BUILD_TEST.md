# Deadlimit — BUILD & TEST

## 2026-08-23 — accepted iteration workflow

After the initial CSDK authoring/material pass, routine character-skin iteration is a single Deadlimit action:

```text
edit project-root DMX / PNG
→ BUILD & TEST
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

### VPK destination

The project `Release ID` is the retail VPK slot and must be `01` through `99`.

Example:

```text
Release ID: 01
→ <Retail Deadlock>/game/citadel/addons/pak01_dir.vpk
```

BUILD & TEST now creates the VPK **in-process** through the already embedded ValvePak library rather than launching `CSDKCfgVPK.exe`. Current ValvePak explicitly supports creating new VPK archives with `Package.AddFile(...)` and `Package.Write(...)`; new packages default to VPK version 2.

This change is intentional UX behavior: the external CSDKCfgVPK success MessageBox cannot provide Deadlimit-owned actions or progress. In-process packing removes that extra modal window and lets Deadlimit own the complete transaction.

Packaging is transactional:

```text
compiled addon game folder
→ build temporary VPK version 2
→ verify archive hashes + file CRCs
→ remove previous configured pak##_dir.vpk / old numeric chunks
→ move verified temporary VPK into final retail slot
```

### Completion UX

Successful BUILD & TEST ends in a Deadlimit-owned dialog with two actions:

```text
OK
→ close the dialog

LAUNCH DEADLOCK GAME
→ launch Steam URI steam://rungameid/1422450
→ close the dialog
```

`OK` remains the default Enter/Escape action so the game cannot be launched accidentally from a stray key press.

The Steam app ID was rechecked against Valve's current official Deadlock Steam listing on 2026-08-23: `1422450`.

### Overall progress UX

BUILD & TEST reports one overall 0–100 progress value across the whole transaction rather than showing independent per-tool progress.

The window title remains the compact high-visibility status surface and now has an animated spinner:

```text
Deadlimit — [34% \] - Comparing prepared content...
Deadlimit — [56% |] - Compiling Source 2 assets — batch 2/4...
Deadlimit — [98% /] - Verifying VPK checksums...
```

Spinner frames rotate as:

```text
|  /  —  \
```

At 100% the spinner becomes a check mark.

A real horizontal progress bar is also shown on the right side of Deadlimit's existing status bar while BUILD & TEST is running. The standard Windows/WinForms caption is not custom-drawn, so the title itself stays textual; no fragile custom non-client title-bar rendering is introduced.

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

This is deliberately fail-safe: incremental speed is used only where Deadlimit can prove what should be retained or invalidated.

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

Deadlimit therefore reuses the command shape that was already confirmed by the earlier local Deadlimit pipeline whenever the main VMDL was recompiled:

```text
DeadlockTools add ag2 <compiled vmdl_c>
  -h <project hero>
  -f <family inferred from this project's resource path>
  --override-skeleton <NmSkeleton reference discovered from this project's 0source>
```

The skeleton path is discovered from the current project's extracted retail VMDL. No Ivy-specific skeleton path is hardcoded. `fix unitstatus` remains conditional and is not part of BUILD & TEST.

### Current external evidence

Checked 2026-08-23:

- current CSDK12 documents `content/citadel_addons/<addon>` as authoring source and `game/citadel_addons/<addon>` as compiled output;
- the current ValvePak API supports creating VPKs in-process and defaults new packages to VPK version 2;
- Valve's current official Deadlock Steam page is app `1422450`;
- current Deadlock modding guidance still identifies Dotryen DeadlockTools AG2 restoration as required for CSDK12 character replacements.

### Validation status

The previous external `CSDKCfgVPK.exe` path successfully produced the retail `pak01_dir.vpk` in the live Ivy Build & Test run on 2026-08-23.

The replacement in-process ValvePak packaging, new completion dialog and overall progress UI are implemented but still require one local acceptance run after Updater.

Acceptance check:

```text
Updater
→ BUILD & TEST
→ no separate CSDKCfgVPK "Success" dialog appears
→ title visibly animates [percent spinner] + current stage
→ bottom status-bar progress advances
→ final Deadlimit dialog offers OK / LAUNCH DEADLOCK GAME
→ pak##_dir.vpk exists in retail addons
→ LAUNCH DEADLOCK GAME opens Deadlock through Steam
```
