# Deadlimit

**Tools for Deadlock mod artists.**

Deadlimit turns the scattered Deadlock character-modding pipeline into one artist-facing workflow: get original game assets, author models and textures, prepare them for Reduced CSDK, iterate in the authoring tools, build a VPK, and test it in retail Deadlock.

It removes the repetitive Source 2 work that would otherwise mean digging through VPKs, editing resource paths by hand, moving files between toolchains, and rebuilding the same setup after every iteration.

**Free · Open source · Windows**

[Русская версия](README.ru.md)

---

## Download

### For artists

**[Download `Install-Deadlimit.cmd`](https://github.com/downlimit/Deadlimit/raw/refs/heads/main/Install-Deadlimit.cmd)**

Put the installer where you want to keep Deadlimit, then run it.

Deadlimit creates a `Deadlimit` folder next to the installer and installs itself there.

```text
D:\Tools\Install-Deadlimit.cmd
D:\Tools\Deadlimit\
```

You do not need to know Git or set up the .NET SDK manually. If Git for Windows or the .NET 10 SDK is missing, the installer shows what is required and asks permission before installing it through WinGet.

Early Deadlimit builds are unsigned, so Windows SmartScreen may show an unknown-publisher warning. Only run the installer downloaded from the official `downlimit/Deadlimit` repository.

### For developers and contributors

If you want to modify Deadlimit itself, submit fixes, or contribute features, clone the repository instead:

```powershell
git clone https://github.com/downlimit/Deadlimit.git
cd Deadlimit
.\DeadlimitManager.cmd
```

A development checkout requires the .NET 10 SDK. See [CONTRIBUTING.md](CONTRIBUTING.md) for the contribution workflow.

---

# Deadlimit Manager

**Deadlimit Manager** is the main desktop application and the center of the workflow.

### Projects

Each mod lives as a separate Deadlimit project. Manager keeps projects in a library and tracks the selected character, source files, generated authoring content, release slot, and pipeline state.

### Get original assets

Choose a Deadlock character and Manager retrieves the supported original resources needed as a working base from the current retail game data.

This replaces the usual manual search through VPKs, model paths, materials, and related resource dependencies.

### Prepare for CSDK

Manager turns artist-owned source files into a Reduced CSDK authoring workspace.

It handles file placement, known exporter/path repairs, model preparation, material scaffolding, texture binding, and other Source 2-specific transformations that would otherwise require manual file editing.

The prepared project remains an editable authoring stage. You can open it in CSDK/ModelDoc, work on materials and shaders, save those edits, and continue iterating without turning the workflow into an opaque one-click converter.

### Live Sync

Keep CSDK open while you work.

Deadlimit watches supported project changes and synchronizes them into the prepared CSDK project automatically. DMX, texture, and Vertex Color changes can be updated without repeating the full manual prepare/copy cycle. Structural or material-reference changes trigger the required full preparation while CSDK stays open.

### Build & Test

When the project is ready for an in-game check, **Build & Test** handles the release-side pipeline.

Deadlimit prepares the latest project state, compiles changed Source 2 resources, restores the required character animation bindings after compilation, verifies the output, packages the addon into a VPK, and deploys it to the configured local Deadlock addons slot.

The CSDK authoring stage stays clean: animation binding repair happens after compilation, so the artist can continue using CSDK for ModelDoc and material work before the final test build.

### Import and repair existing VPKs

Deadlimit can also import an existing `pak##_dir.vpk` as a project.

Imported compiled payload is preserved instead of being pushed through the normal authoring compiler. During **Build & Test**, Deadlimit can compare character animation bindings with the current retail Deadlock model, repair stale or missing bindings, rebuild the VPK, verify it, and deploy it back to the adopted release slot.

This repair path is intentionally narrow: it targets the animation-binding class of breakage rather than pretending to be a universal repair button for every possible mod problem.

### Toolchain management

Manager keeps the external Deadlock modding toolchain in one place.

It can locate and validate Deadlock, manage Reduced CSDK and DeadlockTools, check supported tool state, and invoke supporting utilities such as DepotDownloader when a workflow needs them.

---

# Deadlimit Scripts

**Deadlimit Scripts** are DCC-side tools for the model-authoring stage.

The current bundled implementation is MAXScript-based. Blender support is planned under the same Deadlimit Scripts product rather than as a separate tool.

### Bone Tools

Valve DMX skeletons are not always convenient to work with immediately after import.

Bone Tools can fit visual bone length and thickness to the hierarchy, flip display geometry without changing the rig, and restore compatible bones accidentally converted to Editable Poly while preserving node identity, hierarchy, animation, and Skin references.

### Vertex Color

Deadlimit Scripts make Vertex Color easier to author and verify before the model reaches the game.

You can transfer colors between object palette and Vertex Color, toggle viewport display, spread Vertex Color/material/palette data between meshes, and keep the existing modifier stack intact for supported operations.

For engine-side preview, Deadlimit Manager can prepare a material that displays Vertex Color in CSDK.

If a DMX exporter loses Vertex Color, **Export Vertex Color FBX** writes a companion `*_vertexcolor.fbx`. During Prepare, Manager can detect the sidecar and transfer the color data back to the matching DMX mesh automatically.

### Inner Lineart

**Inner Lineart** turns Deadlock's expanded-backface outline behavior into an authoring tool for graphic lines inside a character design.

Select the intended inner edges, set the line width, and Deadlimit Scripts builds separate line-art geometry with the required winding and normal direction. The generated result can preserve source UVs, Vertex Color, material IDs, transforms, and Skin where applicable.

This makes it possible to design internal graphic strokes as part of the model instead of limiting the Deadlock line-art look to the outer silhouette.

---

# Deadlimit Shade

**In development.**

**Deadlimit Shade** is the texture-authoring side of the toolkit for Substance 3D Painter.

Its goal is to make Painter a useful preview environment for Deadlock character materials instead of forcing texture artists to judge their work through a generic PBR viewport and only discover the real result later in Source 2.

The current prototype already includes Deadlock-oriented Painter shaders, character profiles, outline-preview tooling, retail material/texture inspection helpers, and a Painter dock that can apply the Deadlimit preview setup to a compatible project.

The target workflow is:

```text
Substance 3D Painter
        ↓
Deadlimit Shade
        ↓
Deadlimit Manager
        ↓
Deadlock
```

The goal is practical authoring parity, not a claim of pixel-perfect Source 2 reproduction inside Painter.

---

## Workflow

```text
Retail Deadlock assets
        ↓
Deadlimit Manager
        ↓
DCC + Deadlimit Scripts
        ↓
Substance 3D Painter + Deadlimit Shade
        ↓
Deadlimit Manager
Prepare / Live Sync / Build & Test
        ↓
Reduced CSDK
        ↓
VPK
        ↓
Retail Deadlock
```

Deadlimit Shade is still in development and is optional to the current model-authoring pipeline.

---

## Project status

Deadlimit is actively developed against a moving Deadlock / Source 2 ecosystem.

Windows is the current supported platform. The current bundled Deadlimit Scripts implementation is MAXScript-based, Blender support is planned, and Deadlimit Shade is under active development.

External game and tool updates can require pipeline changes. Exact tested versions and current support status live in [COMPATIBILITY.md](COMPATIBILITY.md).

---

## Help

- [Compatibility](COMPATIBILITY.md)
- [Changelog](CHANGELOG.md)
- [Support](SUPPORT.md)
- [Report a bug or request a feature](https://github.com/downlimit/Deadlimit/issues)

---

## Development

Deadlimit is open source. Development, contribution, DCO, and pull-request requirements are documented in [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License and independence

Deadlimit source code is distributed under the [MIT License](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency and external-tool notices and [SECURITY.md](SECURITY.md) for private vulnerability reporting.

Deadlimit interoperates with user-installed third-party tools and local game content; it does not distribute them. Deadlimit is an independent community project and is not affiliated with, sponsored by, endorsed by, or approved by Valve, Autodesk, Adobe, Wall Worm, or the maintainers of the other tools it can invoke.
