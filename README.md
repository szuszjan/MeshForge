# MeshForge

A desktop app (VB.NET, WPF, .NET 9) for processing 3D scans: import/export of common formats,
hole filling, tessellation (mesh subdivision and simplification), photo-based color texturing,
and a set of other useful mesh cleanup functions.

Real, working, tested code — not a prototype. The algorithmic core (22 unit tests) is kept
separate from the UI, so it's easy to extend or reuse in another project.

## Requirements

- Windows (WPF is a Windows-only technology)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) — to build and run

Visual Studio is **not required** — everything builds and runs from the command line — but you
can also open `MeshForge.sln` in Visual Studio 2022+ if you prefer.

## Running

```bash
dotnet run --project src/MeshForge.App
```

Build the whole solution and run the core tests:

```bash
dotnet build MeshForge.sln
dotnet test src/MeshForge.Tests
```

To get started, open one of the files in the [przyklady/](przyklady/) (examples) folder
(File → Open scan...).

## Installer

A ready-to-run `Setup.exe` (Inno Setup-style Windows installer — Start Menu shortcut, optional
desktop shortcut, uninstall from the apps list) builds with a single command:

```powershell
installer\build.ps1
```

Output: `installer_output\MeshForge-Setup-<version>.exe`. Requires [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`winget install JRSoftware.InnoSetup`) — the script publishes the app as a self-contained single
file (no .NET installation required on the target machine) and packages it into an installer.

## Project structure

```
MeshForge.sln
src/
  MeshForge.Core/     class library - the whole mesh model and algorithms, ZERO dependency on WPF/UI
    Geometry/            Mesh, Triangle, mesh topology (edges/holes), vertex welding
    IO/                   OBJ, STL (ASCII+binary), PLY (ASCII), XYZ (point cloud) import/export
    Algorithms/           HoleFiller, Tessellator (subdivide+QEM decimation), PointCloudTriangulator,
                          Smoother, Texturizer, MeshCleaner, MeshTransformer, NormalCalculator,
                          ConvexHull3D, AutoOrient
  MeshForge.App/       WPF (VB.NET) application - window, 3D viewport (HelixToolkit), event handling
  MeshForge.Tests/     xUnit tests for the Core algorithms (including tests against files from przyklady/)
przyklady/                sample files ready to try the features on
```

The core (`MeshForge.Core`) is deliberately independent of WPF — all the geometry logic also
works from a console/tests, with no window involved.

## Features

### Import / export
- **Import:** OBJ (with material/texture via MTL), STL (ASCII and binary, auto-detected), PLY
  (ASCII variant only — see "Limitations"), XYZ/TXT (raw point cloud: `x y z` or `x y z r g b`,
  space/tab/comma as separator)
- **Export:** OBJ (with MTL + a copy of the texture, if present), STL (binary), PLY (ASCII, with
  vertex color), XYZ

### Mesh repair
- **Fill holes** — detects boundary loops (edges belonging to a single triangle) and patches them
  with ear clipping in a plane fitted using Newell's method. Very large loops (>2000 vertices by
  default) are skipped — that's usually the outer boundary of an unclosed scan, not a hole to patch.
- **Clean mesh** — welds vertices closer together than an epsilon (typical after STL export, which
  doesn't index vertices), removes degenerate/duplicate triangles and orphaned vertices.
- **Recalculate normals** — per-vertex normals weighted by the area of neighboring triangles.
- **Smooth (Laplacian)** — reduces scanner noise; vertices on hole boundaries are deliberately
  skipped so smoothing doesn't "shrink" the boundary inward.

### Tessellation
- **Densify (subdivision)** — splits each triangle into 4 at the edge midpoints (shared midpoints,
  so the mesh stays watertight, with no cracks).
- **Simplify (decimation)** — the classic Garland-Heckbert quadric error metric (QEM) algorithm:
  collapses the cheapest edges to their optimal point (solving a 3×3 system) down to a target
  percentage of the current triangle count.
- **Tessellate point cloud** — for a raw point cloud (no triangles): PCA plane fitting + 2D
  Delaunay triangulation (Bowyer-Watson). Works very well for a single scanner pass (a surface
  seen from roughly one side, like a height map) — see "Limitations" below.

### Photo-based color texture
Generates UV coordinates via projection (planar / cylindrical / spherical — pick the type that
matches the model's shape) and applies the chosen photo as a texture. This is single-photo
projection mapping, not full multi-view photogrammetry.

### Transformations
Centering, normalizing size to 1.0, flipping normals (fixes a mesh that's "inside-out"),
**auto-orient to the flattest surface** — detects the model's flat surfaces using the same
approach as Orca/PrusaSlicer's "place on face" (builds a convex hull of the mesh, then flood-fills
coplanar hull facets into candidate surfaces), shows a list from largest to smallest with a live
3D preview highlighting each candidate directly on the model, and lets you pick which one becomes
the level base (the model is rotated and "placed" on that surface). Useful after scanning, when the
model ends up in a random orientation — and, because it works on the convex hull, it can correctly
find a surface even where the scanner never captured it (e.g. the underside that was resting on
the scan bed).

### Preferences
The **Preferences...** menu — interface language (Polish/English, switches live, no restart
needed) and dark mode. Both choices are remembered between runs
(`%AppData%\MeshForge\settings.txt`).

### Other
Undo the last operation (15-step history), live mesh statistics (vertex/triangle count,
dimensions, surface area, volume, whether the mesh is watertight), wireframe overlay,
rotate/pan/zoom camera in the 3D preview, a splash screen, and a custom app icon.

## Version 1 limitations (deliberate simplifications, not "coming eventually")

- **Point cloud tessellation** is a "2.5D" method (projection onto a single best-fit plane). It
  doesn't correctly reconstruct a closed solid from a point cloud scanned from every side (360°) —
  that would need something like Poisson surface reconstruction / ball-pivoting, which is
  considerably more complex. In practice most scanners already export a finished mesh
  (OBJ/STL/PLY), so this limitation only affects raw 360° point clouds.
- **PLY** — only the ASCII variant is supported (binary produces a clear error message with a hint
  on how to convert it in MeshLab).
- **Texture** is single-photo projection mapping, not automatic multi-photo blending from
  different angles (like RealityCapture/Metashape).
- **Merging multiple scans** — simple mesh concatenation is supported (`MeshTransformer.Append`),
  but without automatic alignment (ICP/registration). If two scans aren't already in the same
  coordinate system, they need to be aligned manually before merging.

None of the above is "faked" — it's simply a scope that's reasonable to actually build and test
in one sitting. The most natural extensions for later: Poisson reconstruction for full 360° point
clouds, ICP for automatic scan alignment, binary PLY import, multi-photo texture baking.

## Why these technical choices

- **WPF + HelixToolkit.Wpf** instead of plain WinForms+OpenGL: a 3D viewport with rotate/zoom/pan
  "for free," without writing a custom camera and without depending on native graphics libraries.
- **System.Numerics.Vector3/Vector2** in Core instead of custom vector types — this is a built-in,
  fast .NET math library, so there's no need to write one from scratch.
- **"Unified index" model** (one normal/UV/color per vertex) instead of separate v/vt/vn indexing
  like raw OBJ — simpler code, and it matches exactly what WPF's `MeshGeometry3D` expects.
