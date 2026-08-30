# Deadlimit Aggregator — Hero extraction

## Purpose

`EXTRACT HERO SOURCE` replaces manual Source 2 Viewer/VPK browsing with one project-level action.

The artist-facing destination is:

```text
<ProjectFolder>\0source\
```

The project root remains the artist-owned handoff area for edited DMX and PNG files. Extraction must never modify those root assets.

## Current external evidence — 2026-08-22

Official ValveResourceFormat/Source 2 Viewer documentation distinguishes two programs:

- `Source2Viewer` — the Windows GUI application;
- `Source2Viewer-CLI` — a separate command-line utility.

The CLI documentation explicitly states that command-line arguments and behavior are not guaranteed to remain stable across releases.

The current NuGet package checked on 2026-08-22 is:

```text
ValveResourceFormat 20.0.6980
Target: .NET 10
Published: 2026-08-17
```

ValveResourceFormat exposes the VPK/resource parsing and decompilation primitives directly as a .NET library, including ValvePak `Package`, `Resource`, `GameFileLoader`, `FileExtract`, and texture extraction.

## Integration decision

Deadlimit Aggregator embeds the pinned ValveResourceFormat NuGet package and performs extraction in-process.

Consequences:

- the artist is not asked to locate or install `Source2Viewer-CLI.exe`;
- the ordinary `Source2Viewer.exe` GUI is optional and remains useful only for manual inspection;
- Deadlimit Aggregator does not depend on unstable CLI argument syntax;
- upgrading ValveResourceFormat is an explicit compatibility change and requires a fresh Deadlock extraction smoke test.

## Current implemented flow

```text
saved Deadlimit Aggregator project
→ EXTRACT HERO SOURCE
→ open current retail VPK(s) through ValveResourceFormat/ValvePak
→ discover a hero .vmdl_c candidate
→ decompile its resource folder into hidden staging
→ verify that files were actually produced
→ publish staging as 0source
→ persist discovered retail paths/version/timestamp/count
```

The locator prioritizes:

```text
D:\Program Files (x86)\Steam\steamapps\common\Project8Staging\game\citadel\pak01_dir.vpk
```

and then scans other `*_dir.vpk` archives under the current retail `game` tree.

Candidate search is restricted to:

```text
models/heroes/
models/heroes_wip/
models/heroes_staging/
```

An exact normalized hero-model filename receives the strongest score. This remains discovery logic rather than a hardcoded hero path.

## Resource decompilation

For each VPK entry in the discovered hero resource folder:

- uncompiled files are copied as raw bytes;
- compiled Source 2 resources are read as `Resource`;
- generic supported resources are decompiled through `FileExtract`;
- textures use `TextureExtract`;
- additional and sub-files emitted by the decompiler are preserved.

## Local validation — 2026-08-22

The embedded extraction path was exercised successfully against the current retail Deadlock install from a real Deadlimit Aggregator project.

Observed result:

- `EXTRACT HERO SOURCE` completed and populated `<ProjectFolder>\0source\`;
- the generated tree contained a decompiled hero model folder under `models\heroes_wip\ivy\`;
- the output visibly included the main `.vmdl` plus many `.dmx` files, including animation-related DMX resources;
- no separate `Source2Viewer-CLI.exe` was required.

This confirms that the in-process ValveResourceFormat path can discover the selected retail hero and produce a substantial decompiled source package in the expected destination.

This observation does **not** yet prove complete dependency closure. The screenshot validates the hero model folder and its decompiled files, but does not by itself prove that every required material, texture, shared mesh, skeleton, or other dependency outside that folder has been included.

## Refresh safety

`0source` is generated retail-source data, but an existing folder may contain a useful prior extraction.

Refresh uses a publish-after-success rule:

1. decompile into `.deadlimit\source-extract-staging`;
2. require at least one output file;
3. move the current `0source` to hidden `.deadlimit\0source.previous`;
4. move staging into `0source`;
5. if the final move fails, attempt to restore the previous extraction.

The artist's root DMX/PNG files remain outside this transaction.

## Persisted extraction facts

`project.json` records:

- discovered retail main model resource path;
- source VPK path;
- last extraction timestamp;
- pinned/runtime ValveResourceFormat version string (the property currently retains the historical `Source2ViewerVersion` name);
- extracted file count.

The field name should be migrated later when schema migration work exists; preserving compatibility is more important than renaming it during this first extraction validation.

## Evidence status

### Confirmed by current external sources

- Source 2 Viewer GUI and Source2Viewer-CLI are separate binaries;
- CLI argument stability is not guaranteed;
- ValveResourceFormat 20.0.6980 is available as a .NET 10 NuGet package and exposes in-process extraction APIs.

### Confirmed by our pipeline

- embedded ValveResourceFormat extraction runs without requiring Source2Viewer-CLI;
- current retail VPK discovery found the selected hero source;
- `0source` was populated successfully with the decompiled hero model folder;
- the resulting hero folder included a main VMDL and many DMX files.

### Hypotheses requiring validation

- hero discovery scoring selects the intended current retail main model across other heroes, not only the current tested project;
- decompiling only the discovered hero resource folder includes every material/texture/shared dependency needed by the full authoring workflow;
- shared dependencies outside that folder can be identified and added generically if later stages prove they are missing.

Do not generalize dependency closure until those dependencies are exercised by Prepare/authoring or inspected explicitly.
