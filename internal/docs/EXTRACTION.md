# Deadlimit Manager — Hero extraction

## Purpose

`EXTRACT HERO SOURCE` replaces manual Source 2 Viewer/VPK browsing with one project-level action.

The artist-facing destination is:

```text
<ProjectFolder>\0source\
```

The project root remains the artist-owned handoff area for edited DMX and texture files. Extraction must never modify those root assets.

## Current external evidence — 2026-08-22

Official ValveResourceFormat/Source 2 Viewer documentation distinguishes two programs:

- `Source2Viewer` — the Windows GUI application;
- `Source2Viewer-CLI` — a separate command-line utility.

The CLI documentation explicitly states that command-line arguments and behavior are not guaranteed to remain stable across releases.

The current pinned NuGet package is:

```text
ValveResourceFormat 20.0.6980
Target: .NET 10
```

ValveResourceFormat exposes the VPK/resource parsing and decompilation primitives directly as a .NET library, including ValvePak `Package`, `Resource`, `GameFileLoader`, `FileExtract`, and texture extraction.

## Integration decision

Deadlimit Manager embeds the pinned ValveResourceFormat NuGet package and performs extraction in-process.

Consequences:

- the artist is not asked to locate or install `Source2Viewer-CLI.exe`;
- the ordinary `Source2Viewer.exe` GUI is optional and remains useful only for manual inspection;
- Deadlimit Manager does not depend on unstable CLI argument syntax;
- upgrading ValveResourceFormat is an explicit compatibility change and requires a fresh Deadlock extraction smoke test.

## Current implemented flow

```text
saved Deadlimit Manager project
→ EXTRACT HERO SOURCE
→ choose per-run extraction options
→ open current retail VPK(s) through ValveResourceFormat/ValvePak
→ discover a hero .vmdl_c candidate
→ decompile its resource folder into hidden staging
→ optionally resolve hero material/texture dependencies
→ optionally resolve the selected hero's ability visual dependencies
→ verify that files were actually produced
→ publish staging as 0source
→ persist discovered retail paths/version/timestamp/count and the extraction options used
```

The extraction options are intentionally **not global Settings**. Every extraction opens the same dialog with:

- `Extract textures` / `Извлекать текстуры`;
- `Extract abilities` / `Извлекать способности`.

The action buttons keep the existing refresh semantics:

- `YES / ДА` — run extraction and preserve the previous `0source` as the hidden backup;
- `YES, NO BACKUP / ДА, БЕЗ БЭКАПА` — run the same extraction, then delete the previous backup only after the new extraction succeeds;
- `NO / НЕТ` — cancel.

The dialog is shown even when `0source` does not exist yet. In that case the no-backup action is disabled because there is no previous extraction to discard.

Deadlimit uses the configured Deadlock location. The **Find** action in Settings can discover Steam library locations through the registry, `libraryfolders.vdf`, and common fixed-drive layouts. A typical candidate is:

```text
<SteamLibrary>\steamapps\common\Project8Staging\game\citadel\pak01_dir.vpk
```

and then scans other `*_dir.vpk` archives under the current retail `game` tree.

Candidate search is restricted to:

```text
models/heroes/
models/heroes_wip/
models/heroes_staging/
```

An exact normalized hero-model filename receives the strongest score. This remains discovery logic rather than a hardcoded hero path.

## Base resource decompilation

For each selected VPK entry in the discovered hero resource folder:

- uncompiled files are copied as raw bytes;
- compiled Source 2 resources are read as `Resource`;
- generic supported resources are decompiled through `FileExtract`;
- textures use `TextureExtract` only when `Extract textures` is enabled;
- additional and sub-files emitted by the decompiler are preserved.

When `Extract textures` is off, `.vtex/.vtex_c` entries physically located inside the hero resource folder are skipped as well. The checkbox therefore controls actual texture extraction, not only the external dependency pass.

## Texture option

When `Extract textures` is enabled, Deadlimit additionally follows the selected hero model/mesh dependency chain to referenced VMAT resources and then to referenced VTEX resources across the current retail VPK set.

The resulting decoded texture content and the referenced materials are written into `0source` at their resource-relative paths. The last successful extraction records that textures were included. Downstream retail-texture override routing uses this project fact rather than a global application preference.

This is a targeted model/material/texture closure. It is not a claim that every possible Source 2 dependency type has been traversed.

## Ability option

When `Extract abilities` is enabled, Deadlimit resolves the selected hero through the current retail VData instead of guessing ability folders by hero name:

```text
selected retail hero model
→ scripts/heroes.vdata
→ m_mapBoundAbilities
→ scripts/abilities.vdata
→ bound ability definitions
→ _base / _multibase inheritance
→ visual resource references
→ recursive Source 2 visual dependencies
```

The current visual dependency scope includes particle systems, models, meshes, materials, snapshots/physics resources and animation-related resources used by those ability definitions.

If `Extract abilities` is enabled while `Extract textures` is off, VTEX resources are excluded from both direct ability roots and recursive ability dependency traversal.

If **both checkboxes are enabled**, ability VTEX references and texture dependencies are included as well. Duplicate dependencies shared by the hero model and an ability are naturally deduplicated by resource path during resolution/writing.

This ability resolver is deliberately based on `m_mapBoundAbilities` for the selected hero. It must not broaden into all abilities or all resources whose path happens to contain the hero name.

## Local validation — 2026-08-22

The embedded extraction path was exercised successfully against the current retail Deadlock install from a real Deadlimit Manager project.

Observed result:

- `EXTRACT HERO SOURCE` completed and populated `<ProjectFolder>\0source\`;
- the generated tree contained a decompiled hero model folder under `models\heroes_wip\ivy\`;
- the output visibly included the main `.vmdl` plus many `.dmx` files, including animation-related DMX resources;
- no separate `Source2Viewer-CLI.exe` was required.

This confirms the base in-process ValveResourceFormat extraction path. The newly added per-run texture/ability selection and hero-bound ability dependency closure still require live retail validation before being treated as practically confirmed for all heroes.

## Refresh safety

`0source` is generated retail-source data, but an existing folder may contain a useful prior extraction.

Refresh uses a publish-after-success rule:

1. decompile into `.deadlimit\source-extract-staging`;
2. require at least one output file;
3. move the current `0source` to hidden `.deadlimit\0source.previous`;
4. move staging into `0source`;
5. if the final move fails, attempt to restore the previous extraction;
6. only after a successful publish, `YES, NO BACKUP` may delete `.deadlimit\0source.previous`.

The artist's root assets remain outside this transaction.

## Persisted extraction facts

`project.json` records:

- discovered retail main model resource path;
- source VPK path;
- last extraction timestamp;
- pinned/runtime ValveResourceFormat version string (the property currently retains the historical `Source2ViewerVersion` name);
- extracted file count;
- whether the last successful extraction included textures;
- whether the last successful extraction included abilities.

These last-run flags are project facts, not persistent defaults for the next extraction dialog.

## Evidence status

### Confirmed by current external sources

- Source 2 Viewer GUI and Source2Viewer-CLI are separate binaries;
- CLI argument stability is not guaranteed;
- ValveResourceFormat exposes in-process extraction APIs used by Deadlimit;
- current Deadlock hero data exposes hero-bound abilities through `m_mapBoundAbilities`.

### Confirmed by our pipeline

- embedded ValveResourceFormat base extraction runs without requiring Source2Viewer-CLI;
- current retail VPK discovery found the selected hero source;
- `0source` was populated successfully with the decompiled hero model folder;
- the resulting hero folder included a main VMDL and many DMX files.

### Implemented, awaiting live retail validation

- per-run texture and ability extraction options;
- skipping all hero-folder VTEX resources when textures are disabled;
- selected-hero `m_mapBoundAbilities` resolution through `abilities.vdata`;
- recursive visual dependency extraction for selected abilities;
- inclusion of ability textures only when both ability and texture extraction are enabled.

Do not generalize the new ability/dependency closure across heroes until it has been exercised against current retail data.
