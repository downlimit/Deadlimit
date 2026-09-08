# Deadlimit Manager — Hero texture extraction and retail override

Tracking issue: #161

## Goal

Make retail hero textures an optional first-class project source and replacement target.

When enabled, `EXTRACT HERO SOURCE` must follow the selected hero's material/texture dependencies, extract the referenced retail textures into `0source` with their original Source 2 resource paths, and expose a strict replacement contract for artist-edited root textures.

## User contract

- Settings contains `Extract hero textures` / `Извлекать текстуры персонажа`.
- Default: disabled.
- Disabled mode preserves the current extraction behavior.
- Enabled mode extracts only texture dependencies actually referenced by the selected hero/material graph; it must not dump unrelated VPK texture trees.
- Extracted texture outputs remain generated reference data under `<Project>\0source\`.
- Artist-edited replacements remain top-level project files, consistent with the current artist DMX handoff.
- A supported root image whose filename, including extension, uniquely matches an extracted retail logical texture source targets that retail texture's original Source 2 resource path during PREPARE.
- Same-stem files with a different extension fail closed. Current retail evidence shows the logical source type participates in the compiled VTEX resource identity.
- Duplicate retail filenames in different resource paths fail closed with an explicit ambiguity error. Deadlimit never guesses between multiple retail targets.
- Existing custom-material project-root texture binding remains independent and must not regress.

Supported artist image source extensions follow the existing project texture policy:

```text
.png
.tga
.jpg
.jpeg
.tif
.tiff
```

## Implementation stages

1. Persist the extraction preference and expose the localized Settings checkbox.
2. Extend hero extraction from folder-only decompilation to material/texture dependency closure while preserving retail resource paths.
3. Persist or deterministically rebuild a per-project retail texture target index.
4. Resolve project-root images against that index by strict unique filename matching, including the original source extension.
5. Stage matched replacement sources at the original retail resource path before CSDK compilation.
6. Apply the same replacement mapping during Online Preparation.
7. Add smoke coverage for enabled/disabled extraction, unique mapping, extension mismatch refusal, ambiguous refusal, alpha-bearing sources, deletion cleanup, and custom-material non-regression.
8. Validate a complete replacement in current retail Deadlock before closing #161.

## Invariants

- `0source` remains generated retail-source data.
- Project-root artist files remain authoritative replacement inputs.
- Retail texture replacement must not rewrite original retail VMAT paths merely to point at addon-private texture paths; the replacement resource itself occupies the original referenced resource path.
- A user texture must never replace more than one retail resource unless that mapping is explicitly supported later.
- Extraction failure must preserve the last successfully published `0source` according to the existing publish-after-success transaction.
