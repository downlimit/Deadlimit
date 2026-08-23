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
→ restart Deadlock if it was already running
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

`BUILD & TEST` invokes the CSDK12 VPK packer directly against the current addon's compiled `game/citadel_addons/<addon>` folder. The previous `pak##_dir.vpk` and its numeric chunk siblings are removed immediately before successful repacking.

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
- CSDK12 exposes `Compile All Assets` and `Create VPK` without requiring the Asset Browser/Material Editor authoring loop;
- the current public Deadlock-Mod-Compiler independently uses `game/bin_cs2/win64/resourcecompiler.exe`, `game/bin/win64/CSDKCfgVPK.exe`, repeated `-i` compilation batches, and supports packing directly to retail `game/citadel/addons`;
- current Deadlock modding guidance still identifies Dotryen DeadlockTools AG2 restoration as required for CSDK12 character replacements.

### Validation status

Implementation: complete in code.

Live local validation of the new one-click transaction is still pending. The first acceptance test should use the current Ivy project and verify:

```text
BUILD & TEST
→ main model + custom material compile
→ AG2 succeeds
→ pak01_dir.vpk (or configured Release ID) appears directly in retail addons
→ Deadlock loads the replacement

then change only one PNG
→ BUILD & TEST reports incremental mode
→ recompiles only the required material dependency set
→ repacks the same retail VPK slot

then change only the artist DMX skinning
→ BUILD & TEST recompiles the model path
→ reapplies AG2
→ repacks the same retail VPK slot
```
