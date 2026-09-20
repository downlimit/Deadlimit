# Deadlimit Manager — Roadmap

This roadmap describes the current product state and the remaining validation work.
Detailed evidence and implementation notes live in the focused documents linked from
`internal/docs/README.md`.

## Current baseline — in use

The core artist workflow is implemented:

```text
select project / hero
→ extract original resources when needed
→ edit model and textures
→ PREPARE FOR CSDK
→ CSDK / ModelDoc material work
→ LIVE SYNC for supported iteration
→ BUILD FOR TEST
→ compile
→ repair current animation bindings when required
→ verify and package VPK
→ deploy to Deadlock
```

Current accepted capabilities include:

- project creation, persistence, and library management;
- hero source extraction through ValveResourceFormat;
- optional hero material/texture dependency extraction;
- preservation of the artist-owned `1authoring` workspace;
- CSDK authoring preparation without destroying manual material edits;
- LIVE SYNC for supported DMX, texture, and Vertex Color changes;
- incremental Source 2 compilation and verified ValvePak VPK packaging;
- post-compile AG2/NmSkeleton restoration for normal authoring projects;
- import of existing VPK projects without forcing them through the authoring compiler;
- inspection and repair of imported compiled-model animation bindings against current retail resources;
- guarded deployment to the configured Deadlock addon slot;
- English, Russian, Simplified Chinese, and Brazilian Portuguese UI localization.

## Validation still worth expanding

These are compatibility/coverage tasks, not blockers for the established Ivy-tested
workflow:

- exercise extraction, material routing, and model replacement on more heroes;
- validate more Wall Worm DMX material/layout variants before generalizing new rules;
- complete current-retail proof for the optional hero texture replacement path;
- validate imported-VPK animation repair on additional real mods broken by game updates;
- revalidate compatibility whenever Deadlock, Reduced CSDK, DeadlockTools, Wall Worm,
  ValveResourceFormat, or supported DCC versions change.

## Deadlimit Scripts

The current MAXScript implementation is usable and bundled. Ongoing work should focus
on production validation of existing tools, removing experimental labels only after
their behavior is proven on real assets, and expanding to additional DCC hosts only
when there is a maintained implementation.

## Deadlimit Shade

Deadlimit Shade remains experimental. The repository already contains working shader,
preset, Painter integration, preview, extraction, and analysis components. The next
work is evidence-driven refinement and cross-character validation; research results
must remain explicitly separated from assumptions and unverified parity claims.

## Product rule

Do not turn a hero-specific observation or one captured scene into a global rule.
One check → result → conclusion → next check remains the preferred validation loop.
