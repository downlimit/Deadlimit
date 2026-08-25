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
2. copies a `fileIn @"..."` command to the clipboard;
3. leaves the project source and current Max scene untouched.

In 3ds Max:

1. select one or more geometry nodes to export;
2. open MAXScript Listener;
3. paste the copied command and press Enter.

The helper exports one `<node name>.dmx` file per selected geometry node directly to the project root.

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
- verifies that a color-bearing export contains both `color$0` and `color$0Indices`;
- deletes the temporary copy on success or failure;
- deletes a partial output file when export validation fails.

The artist node and its modifier stack are never modified.

If channel 0 is absent, the helper exports with `color:0`, preserving the existing no-vertex-color behavior.

## Naming contract

The output filename is derived from the selected Max node name.

For projects containing more than one artist DMX, existing Deadlimit rules still apply: node/file names must correspond to the retail render-mesh source filenames so the prepared VMDL can map them unambiguously.

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
