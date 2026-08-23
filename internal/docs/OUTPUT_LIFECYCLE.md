# Deadlimit — content/game output lifecycle

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

### Deadlimit contract

Deadlimit treats the two trees asymmetrically:

```text
content = authoritative authoring state
game    = disposable compiled output
```

`game/citadel_addons/<addon>` must never be treated as an independent source of truth.

### PREPARE FOR CSDK cleanup

Before changing the current addon's prepared authoring content, `PREPARE FOR CSDK` removes the entire compiled output directory for **that addon only**:

```text
Reduced_CSDK_12/game/citadel_addons/<current_addon>
```

It does not delete:

- `Reduced_CSDK_12/game` globally;
- `game/citadel_addons` globally;
- another addon's compiled output;
- retail Deadlock files.

If the current addon's output cannot be removed because files are locked or inaccessible, PREPARE must fail visibly rather than leave a mixed stale/current runtime state.

Deleting the whole current-addon output is intentional. Deadlimit does not try to infer a one-to-one mapping between source files and every compiled derivative (`.vtex_c`, `.vmat_c`, `.vmdl_c`, dependencies, generated resources, etc.). A clean rebuild is safer than retaining orphaned compiled files.

### Texture removal lifecycle

For Deadlimit-managed CUSTOM materials, the project-root PNG set is authoritative.

Example:

```text
project root contains builder_metal.png
→ PREPARE copies it into addon content
→ managed VMAT binds TextureMetalness
→ CSDK rebuild produces current compiled output in game

later builder_metal.png is removed from project root
→ PREPARE removes the stale derived PNG from addon content
→ managed VMAT returns the slot to its safe default and disables required texture combo state
→ PREPARE has already cleared game/citadel_addons/<addon>
→ next CSDK rebuild cannot retain the obsolete compiled metal texture
```

Thus file addition, update and deletion all converge on one state:

```text
project root / authored content
→ PREPARE synchronizes authoritative content
→ current addon game output is clean
→ CSDK rebuilds game from the new content state
```

### Current implementation status

Confirmed in current code:

- `PrepareAuthoringService` deletes `game/citadel_addons/<current_addon>` recursively at the start of PREPARE when it exists;
- the deletion is scoped from the configured CSDK `game` root plus the normalized current addon name;
- `CustomMaterialAuthoringService.SyncTextureSourceFolder` removes derived PNG files that no longer exist in the project root;
- Deadlimit-managed V4 VMATs reconcile texture assignments on every PREPARE, including add/remove behavior;
- PREPARE does not compile; CSDK12 owns rebuilding the clean `game` output from `content`.

This lifecycle is an implementation invariant for later `RELEASE` / `RELEASE & TEST` work as well: release-time compilation may use a different transaction, but stale compiled artifacts must never survive merely because their source was deleted.
