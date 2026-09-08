# Deadlimit Manager — Hero texture extraction and retail override

Tracking issue: #161

## Goal

Make retail hero textures an optional first-class project source and replacement target.

When enabled, `EXTRACT HERO SOURCE` follows the selected hero's material/texture dependencies, extracts referenced retail textures into `0source` with their original Source 2 resource paths, and exposes a strict replacement contract for artist-edited root textures.

## User contract

- Settings contains `Extract hero textures` / `Извлекать текстуры персонажа`.
- Default: disabled.
- Disabled mode preserves the previous folder-only extraction behavior.
- Enabled mode resolves retail dependencies through ValveResourceFormat RERL references: selected VMDL -> referenced VMAT resources -> referenced VTEX resources.
- Extracted material and texture outputs preserve their retail Source 2 resource paths under `<Project>\0source\`.
- Artist-edited replacements remain top-level project files, consistent with the current artist DMX handoff.
- A supported root image whose filename, including extension, uniquely matches an extracted retail logical texture source targets that texture's original Source 2 resource path during PREPARE.
- Same-stem files with a different extension fail closed. Current retail evidence shows the logical source type participates in the compiled VTEX resource identity.
- Duplicate retail filenames in different resource paths fail closed with an explicit ambiguity error. Deadlimit never guesses between multiple retail targets.
- ONLINE PREPARATION routes edits to an existing retail replacement back to the same retail resource path; ordinary project/custom-material textures retain their existing addon texture target.
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

## Implementation status

1. Persist extraction preference and localized Settings checkbox — **implemented**.
2. Hero VMDL/VMAT/VTEX dependency extraction with retail resource-path preservation — **implemented**.
3. Deterministic project retail-texture target index rebuilt from extracted VMAT sources — **implemented**.
4. Strict unique filename + original-extension root override resolution — **implemented**.
5. PREPARE staging at the original retail resource path — **implemented**.
6. ONLINE PREPARATION routing for existing root retail replacements — **implemented**.
7. Dedicated Windows CI for dependency/wiring and override-resolution smokes — **implemented; live CI proof required before merge**.
8. Complete replacement validation in current retail Deadlock — **pending live proof**.

## Invariants

- `0source` remains generated retail-source data.
- Project-root artist files remain authoritative replacement inputs.
- Retail texture replacement does not rewrite original retail VMAT paths to addon-private paths; the replacement source occupies the original referenced resource path.
- A user texture never replaces more than one retail resource through an ambiguous filename match.
- Removing a root retail override restores the extracted retail source on the next normal PREPARE because the retail source tree/dependencies are copied before root overrides are applied.
- Extraction failure preserves the last successfully published `0source` according to the existing publish-after-success transaction.
