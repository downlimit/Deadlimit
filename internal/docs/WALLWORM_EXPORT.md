# Wall Worm DMX22 export

## Confirmed problem

The current artist pipeline stores vertex color in 3ds Max map channel `0`. Wall Worm 7.35.1 can read that channel through `WallWormS2MeshOps.getAllMapColors`, but the native `WallWormS2DMXExport` keyword `color:0` means that the color stream is disabled. A normal DMX22 export can therefore omit `color$0` even though channel 0 is present in Max.

The native exporter accepts a positive Max map channel through `color:<channel>`. The confirmed working bridge is:

```text
Max map channel 0
→ temporary ChannelMod 0→N
→ WallWormS2DMXExport color:N
→ DMX22 color$0/color$0Indices
```

`N` must be a free positive map channel. Channel 1 is reserved by the normal UV workflow, so the Deadlimit helper searches `2..99` and never hardcodes channel 2.

## Deadlimit implementation

The desktop UI exposes **MAX EXPORT / ЭКСПОРТ ИЗ MAX** for a saved project.

The button:

1. writes a project-specific helper to `.deadlimit/wallworm/DeadlimitWallWormExport.ms`;
2. injects the current project's safe Max-node → DMX-filename mapping;
3. copies a `fileIn @"..."` command to the clipboard;
4. leaves the project source and current Max scene untouched.

In 3ds Max:

1. select one or more geometry nodes to export;
2. open MAXScript Listener;
3. paste the copied command and press Enter.

The helper writes the resolved DMX22 files directly to the project root.

## Safety contract

For every selected node the helper:

- snapshots channel support only for detection;
- creates a temporary scene copy;
- finds a free positive map channel;
- if source channel 0 exists, adds `ChannelMod` with:

```text
fromChannel = 0
toChannel   = N
mode        = 0
multiplier  = 1.0
normalize   = false
clearSource = false
```

- exports text DMX22 through `WallWormS2DMXExport` with `color:N`;
- writes first to a temporary DMX;
- verifies that a color-bearing export contains both `color$0` and `color$0Indices`;
- replaces the previous project-root DMX only after validation succeeds;
- deletes the temporary scene copy and temporary DMX on success or failure.

The artist node and its modifier stack are never modified. A failed export also preserves the previous good project-root DMX.

If channel 0 is absent, the helper exports with `color:0`, preserving the existing no-vertex-color behavior.

## Naming contract

Deadlimit does not assume that a Max node name is identical to the retail DMX filename. Current retail ModelDoc data can legitimately contain entries such as a render-mesh `name` and a different `filename`.

When preparing the project-specific helper, Deadlimit builds aliases from:

- the current prepared `SourceVmdl`, when available;
- otherwise the extracted retail VMDL in `0source`;
- existing project-root DMX basenames.

For every `RenderMeshFile`, both its ModelDoc `name` and DMX basename map to the actual DMX filename. Conflicting aliases are removed rather than guessed.

If mapping evidence exists and a selected Max node cannot be resolved uniquely, the export fails before any final DMX is written. The artist can then rename the node to the retail `RenderMeshFile` name or DMX basename and retry.

Only when the project has no VMDL/root-DMX mapping evidence yet does the helper fall back to `<node name>.dmx`.

## Downstream proof

The verified DMX22 output contains:

```text
"vertexFormat" "string_array"
[
    "position$0",
    "normal$0",
    "texcoord$0",
    "color$0",
    "blendindices$0",
    "blendweights$0"
]
```

and:

```text
"color$0Indices" "int_array"
"color$0" "color_array"
```

The tested Reduced CSDK compiler at `game/bin_cs2/win64/resourcecompiler.exe` accepts this DMX22 color representation and produces `VMDL_C`; no conversion to `vector4` is required.

The existing BUILD & TEST pipeline remains unchanged after the DMX is written to the project root.
