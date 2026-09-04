# Third-Party Notices

Deadlimit source code uses the following NuGet dependencies. This inventory is
based on the resolved dependency graph for `internal/src/Deadlimit/Deadlimit.csproj`
on 2026-09-04. The project license does not replace the licenses of these
components.

| Component | Resolved version | License |
| --- | ---: | --- |
| KeyValues2 | 0.8.0 | MIT |
| ValveResourceFormat | 20.0.6980 | MIT |
| Blake3 | 3.0.2 | BSD-2-Clause; bundled native BLAKE3 is CC0-1.0 or Apache-2.0 |
| K4os.Compression.LZ4 | 1.3.8 | MIT |
| SharpGLTF.Core, SharpGLTF.Runtime, SharpGLTF.Toolkit | 1.0.6 | MIT |
| SkiaSharp and SkiaSharp.NativeAssets packages | 4.151.1 | MIT; package notices may include native-component terms |
| System.IO.Hashing | 10.0.10 | MIT |
| TinyBCSharp | 0.1.2 | MIT |
| TinyEXR.NET | 1.1.0 | MIT |
| ValveKeyValue | 0.70.0.499 | MIT |
| ValvePak | 5.0.2.177 | MIT |
| Vortice.SPIRV | 1.0.5 | MIT |
| Vortice.SpirvCross | 1.5.4 | MIT |
| ZstdSharp.Port | 0.8.8 | MIT |

Release packaging must carry the exact license and notice files resolved with
the shipped package versions. The release audit remains incomplete until that
payload is generated and verified.

## External tools and content

Deadlimit interoperates with user-installed software and local content from
Valve/Deadlock, Reduced CSDK, Wall Worm, Autodesk 3ds Max, Adobe Substance 3D
Painter, DeadlockTools, DepotDownloader, and Source 2 Viewer. Those products,
services, binaries, and content are not part of Deadlimit and are not licensed
under Deadlimit's MIT license.

Deadlimit is an independent community project. It is not affiliated with,
endorsed by, sponsored by, or approved by Valve, Autodesk, Adobe, the Wall Worm
authors, or the maintainers of the other external tools it can invoke.

Users and contributors are responsible for complying with the licenses, terms,
account requirements, and redistribution rules that apply to their tools and
content. Deadlimit releases must not bundle retail game content, Reduced CSDK
content, extracted resources, or third-party executables unless a later audit
documents explicit redistribution permission.
