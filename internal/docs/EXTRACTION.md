# Deadlimit — Hero extraction

## Purpose

`EXTRACT HERO SOURCE` exists to replace the manual Source 2 Viewer/VPK browsing step with one project-level action.

The artist-facing destination is:

```text
<ProjectFolder>\0source\
```

The project root remains the artist-owned handoff area for edited DMX and PNG files. Extraction must never modify those root assets.

## Current external evidence — 2026-08-22

Source 2 Viewer / ValveResourceFormat release `20.0` is the current release as of this check (released 2026-08-17).

The current official CLI documentation confirms:

- the binary name is `Source2Viewer-CLI`;
- `-i` / `--input` accepts VPK input;
- `--vpk_list` lists archive resources;
- `--vpk_filepath` filters archive resource paths and supports comma-separated filters;
- `-o` / `--output` selects the output directory;
- `-d` / `--vpk_decompile` decompiles supported resources;
- CLI argument stability is explicitly not guaranteed across future versions.

Therefore Deadlimit keeps all Source 2 Viewer command syntax inside a single adapter and records the detected CLI version with each extraction. Compatibility must be rechecked when Source 2 Viewer is updated.

Recent ValveResourceFormat releases also include Deadlock-specific model/resource work, including support for Deadlock model gamedata nodes and export/decompilation improvements involving NmSkeletonRefs and AnimGraph2Refs. These are external capabilities; Deadlimit must still validate its own concrete extraction output before depending on them.

## Implemented extraction slice

Current flow:

```text
saved Deadlimit project
→ EXTRACT HERO SOURCE
→ resolve Source2Viewer-CLI.exe
→ scan current retail Deadlock VPK(s)
→ discover a hero .vmdl_c candidate
→ decompile its resource folder into hidden staging
→ verify that files were actually produced
→ publish staging as 0source
→ persist discovered retail paths/version/timestamp/count
```

The locator currently prioritizes:

```text
D:\Program Files (x86)\Steam\steamapps\common\Project8Staging\game\citadel\pak01_dir.vpk
```

and falls back to other `*_dir.vpk` archives under the current retail `game` tree.

Candidate search is restricted to current hero model namespaces:

```text
models/heroes/
models/heroes_wip/
models/heroes_staging/
```

An exact hero model filename receives the strongest score. This is discovery logic, not a hardcoded hero path.

## Source 2 Viewer location

Deadlimit does not assume a permanent Source 2 Viewer install path.

It first reuses the saved path from:

```text
%LOCALAPPDATA%\Deadlimit\settings.json
```

It checks a small set of likely locations under the Deadlock workspace. If the CLI still cannot be found, the user selects `Source2Viewer-CLI.exe` once; Deadlimit persists that path for future extraction.

## Refresh safety

`0source` is generated retail-source data, but an existing folder may contain a useful prior manual extraction.

Refresh therefore uses a publish-after-success rule:

1. decompile into `.deadlimit\source-extract-staging`;
2. require a successful CLI exit and at least one output file;
3. move the current `0source` to hidden `.deadlimit\0source.previous`;
4. move staging into `0source`;
5. if the final move fails, attempt to restore the previous extraction.

The artist's root DMX/PNG files are outside this transaction and are never touched.

## Persisted extraction facts

`project.json` now records:

- discovered retail main model resource path;
- source VPK path;
- last extraction timestamp;
- detected Source 2 Viewer version string;
- extracted file count.

These facts are project evidence and should be reused by later Prepare/Release stages rather than rediscovered blindly.

## Current hypothesis / next validation

The first implementation decompiles the resource folder containing the discovered main hero model.

This is intentionally narrower than claiming a complete dependency closure. The next real-project test must determine whether the generated folder already contains all render meshes/materials/textures required for the artist workflow or whether Deadlimit must inspect VMDL dependencies and extract additional shared resources from other retail paths.

Do not generalize the dependency strategy until that test is observed.
