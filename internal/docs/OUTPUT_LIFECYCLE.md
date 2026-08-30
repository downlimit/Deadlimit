# Deadlimit Aggregator — content/game output lifecycle

## 2026-08-23 — authoritative source and stale-output cleanup

### Fresh external state

Current CSDK12 documentation defines:

```text
content/citadel_addons/<addon>
= raw/editable source used by the tools

game/citadel_addons/<addon>
= compiled output produced from content and read by the game/tools
```

Compiled assets normally appear in `game` with `_c` suffixes. CSDK12 tools automatically compile source assets from the addon's `content` tree into the corresponding `game` tree.

### Deadlimit Aggregator contract

Deadlimit Aggregator treats the two trees asymmetrically:

```text
content = authoritative authoring state
game    = disposable compiled output
```

`game/citadel_addons/<addon>` must never be treated as an independent source of truth.

The addon name comes from the manifest's permanent `AddonId`. Before either transaction touches CSDK output, Deadlimit Aggregator verifies `.deadlimit-addon-owner.json` in the addon content root against the project's permanent `ProjectId`. An unreadable record, a foreign owner, or a pre-existing unclaimed folder stops the operation before recursive cleanup. Legacy projects may adopt their existing addon only when their stored source/compiled VMDL path proves the relationship.

All manifest resource paths, VMDL `RenderMeshFile` destinations and build-state output mappings are resolved beneath their declared project/addon root before filesystem access. Rooted paths and lexical traversal outside that root fail validation. The same containment rule guards the previous VPK path loaded from deployment ownership before old-slot cleanup.

## Two output policies

Deadlimit Aggregator now has two deliberately different transactions.

### PREPARE FOR CSDK — clean authoring transaction

The standalone authoring action remains conservative. Before changing the current addon's prepared content it removes the entire compiled output directory for **that addon only**:

```text
Reduced_CSDK_12/game/citadel_addons/<current_addon>
```

It does not delete:

- `Reduced_CSDK_12/game` globally;
- `game/citadel_addons` globally;
- another addon's compiled output;
- retail Deadlock files.

If the current addon's output cannot be removed because files are locked or inaccessible, PREPARE fails visibly rather than leaving a mixed stale/current authoring state.

This mode is intended for material/shader/ModelDoc authoring, where correctness is more important than retaining previous compiled artifacts.

### BUILD & TEST — incremental runtime transaction

The normal repeated in-game iteration action preserves the last successful compiled addon output while it runs the same authoring preparation over `content`.

After preparation it compares the current prepared content against `.deadlimit/build-test-state.json`, which contains hashes from the last successful BUILD & TEST.

The incremental rule is:

```text
unchanged source
→ keep existing compiled output

changed direct source
→ recompile that source

changed DMX dependency
→ recompile VMDL source(s)

changed image dependency
→ recompile VMAT source(s)

removed source with proven output mapping
→ delete that compiled output

removed source without proven output mapping
→ abandon incremental retention and perform a clean addon rebuild
```

The first BUILD & TEST has no trusted prior snapshot, so it performs a clean/full build and establishes the baseline.

The build snapshot is updated only after compile, required AG2 restoration and VPK packaging all succeed. A failed transaction therefore cannot declare partially built output current.

## Texture removal lifecycle

For Deadlimit Aggregator-managed CUSTOM materials, the project-root PNG set remains authoritative.

Example in the normal daily path:

```text
project root contains builder_metal.png
→ BUILD & TEST preparation copies it into addon content
→ managed VMAT binds TextureMetalness
→ changed image/material state is compiled
→ VPK is repacked directly to retail addons

later builder_metal.png is removed from project root
→ preparation removes the stale derived PNG from addon content
→ managed VMAT returns the slot to its safe default and disables required texture combo state
→ incremental cleanup removes the old mapped .vtex_c
→ VMAT is recompiled
→ the retail VPK is replaced without the obsolete texture
```

Thus file addition, update and deletion converge on the current authoritative source without requiring a full rebuild for ordinary mapped texture changes.

## Retail VPK deployment

BUILD & TEST packages:

```text
Reduced_CSDK_12/game/citadel_addons/<addon>
```

directly to the configured retail Deadlock installation:

```text
<Retail Deadlock>/game/citadel/addons/pak##_dir.vpk
```

where `##` is the project's Release ID (`01` through `99`). Existing numeric chunks for that same slot are removed before repacking.

Packaging is now owned in-process by Deadlimit Aggregator through ValvePak rather than the external CSDKCfgVPK GUI. The archive is built as VPK version 2 in a temporary retail-adjacent file, hash/file checksums are verified, and only then is the previous configured retail VPK replaced. This prevents the external packer's modal success dialog from interrupting the one-action BUILD & TEST workflow.

## Current implementation status

Confirmed in current code:

- standalone `PrepareAuthoringService` still deletes `game/citadel_addons/<current_addon>` recursively at the start of PREPARE when it exists;
- that deletion is scoped to the configured CSDK `game` root plus the normalized current addon name;
- `CustomMaterialAuthoringService.SyncTextureSourceFolder` removes derived PNG files that no longer exist in the project root;
- Deadlimit Aggregator-managed V4 VMATs reconcile texture assignments on every preparation, including add/remove behavior;
- `BuildAndTestService` preserves previous addon game output around PREPARE only when a prior successful Build & Test state exists;
- changed prepared content is hashed and compiled incrementally;
- known removed source outputs are pruned, while ambiguous removals force a clean rebuild;
- freshly recompiled character VMDLs receive the previously validated DeadlockTools `add ag2` post-process using a skeleton reference discovered from the project's own `0source`;
- ValvePak creates and verifies the final VPK in-process, then Deadlimit Aggregator deploys it transactionally into retail `game/citadel/addons`;
- Build & Test reports overall percentage progress to the UI while compiling/packing.

Live local validation of the new in-process VPK transaction and progress/completion UX is pending. The previous CSDKCfgVPK-based transaction already produced a working retail `pak01_dir.vpk`; the next acceptance run determines whether the silent ValvePak replacement is behaviorally equivalent for the current Deadlock addon loader.
