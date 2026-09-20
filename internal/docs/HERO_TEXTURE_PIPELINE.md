# Deadlimit Manager — Hero texture extraction and retail override

Tracking issue: #161

## Goal

Make retail hero textures an optional first-class project source and replacement target.

When enabled, `EXTRACT HERO SOURCE` follows the selected hero's material/texture dependencies, extracts referenced retail textures into `0source` with their original Source 2 resource paths, and exposes a strict replacement contract for artist-edited textures under `1authoring`.

## User contract

- Settings contains `Extract hero textures` / `Извлекать текстуры персонажа`.
- Default: disabled.
- Disabled mode preserves the previous folder-only extraction behavior.
- Enabled mode resolves retail dependencies through ValveResourceFormat RERL references: selected VMDL -> referenced VMAT resources -> referenced VTEX resources.
- Extracted material and texture outputs preserve their retail Source 2 resource paths under `<Project>\0source\`.
- Artist-edited replacements may be placed anywhere under `1authoring`; subfolder names do not affect resource routing.
- Same-stem authoring images use TGA, then PNG, then PSD priority. Equal-priority duplicates use the alphabetically first relative path.
- A supported authoring image whose basename uniquely matches an extracted retail logical texture source targets that texture's original Source 2 resource path during PREPARE.
- Duplicate retail basenames in different resource paths open an explicit selection dialog. The user may target one or all matches and remember the choice for that specific authoring file.
- ONLINE PREPARATION routes edits to an existing retail replacement back to the same retail resource path; ordinary project/custom-material textures retain their existing addon texture target.
- Existing custom-material project-root texture binding remains independent and must not regress.

Supported artist image source extensions follow the existing project texture policy:

```text
.png
.tga
.psd
.jpg
.jpeg
.tif
.tiff
```

## Implementation status

1. Persist extraction preference and localized Settings checkbox — **implemented**.
2. Hero VMDL/VMAT/VTEX dependency extraction with retail resource-path preservation — **implemented**.
3. Deterministic project retail-texture target index rebuilt from extracted VMAT sources — **implemented**.
4. Recursive `1authoring` basename resolution, image priority, and persistent ambiguity choices — **implemented**.
5. PREPARE staging at the original retail resource path — **implemented**.
6. ONLINE PREPARATION routing for existing root retail replacements — **implemented**.
7. Dedicated Windows CI for dependency/wiring and override-resolution smokes — **implemented; live CI proof required before merge**.
8. Complete replacement validation in current retail Deadlock — **pending live proof**.

## Invariants

- `0source` remains generated retail-source data.
- `1authoring` artist files remain authoritative replacement inputs; root-level files are ignored.
- Retail texture replacement does not rewrite original retail VMAT paths to addon-private paths; the replacement source occupies the original referenced resource path.
- A user texture replaces multiple retail resources only after an explicit “replace all” selection.
- Removing a `1authoring` retail override restores the extracted retail source on the next normal PREPARE because the retail source tree/dependencies are copied before authoring overrides are applied.
- Extraction failure preserves the last successfully published `0source` according to the existing publish-after-success transaction.
