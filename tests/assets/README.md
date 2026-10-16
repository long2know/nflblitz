# Asset-import regression checks

From the repository root, with the .NET 10 SDK:

```sh
dotnet run --project tests/assets/ImportChecks.csproj --configuration Release
python3 tests/assets/run.py
```

The dependency-free console harness runs **2,751 artifact assertions**: the
original 419 importer checks plus 2,332 original synthetic pose checks. An exact
inventory guard fails if checks disappear or the count changes. The library links
the seven actual production files below and compiles as **C# 7.3 / netstandard2.1**.
The executable targets .NET 10. No NuGet test framework, copied implementation,
Unity stub, source archive, or original art is needed.

Linked from `Assets/Scripts/AssetImport/`:
`GeneratedCInitializer.cs`, `PlayerImportPlan.cs`, `GeneratedPlayerReader.cs`,
`BindPoseMeshBuilder.cs`, `GeneratedPlayerData.cs`, `AuthorizedAssetFiles.cs`,
and `SourcePlayerPose.cs`.

The unchanged synthetic C strings, two-limb quad, and generated 2×2 pixels cover
parser diagnostics/invalid inputs, packed indices, material/UV seams, all 48
signed axis maps, winding, normals, pivots, TGA shape, confined paths, and source
hash/provenance artifacts. The direct command creates and removes its own
uniquely named fixture directory under ignored `bin/`.

The added, unchanged `PoseChecks.cs` corpus constructs only a two-joint synthetic
pose with 90-degree XYZW rotations, generated matrices, arbitrary authoring
offsets, and dummy provenance hashes. It checks finite transformed results,
quaternion/matrix agreement and zero-quaternion rejection, parent-relative pivot
composition, all 48 signed basis maps (including reflection parity), preserved
scaled root Y with anchored X/Z rather than invented horizontal root motion,
frame bounds, mapping/schema errors, and explicit content-pin rejection. It does
not contain the original standing pose's 15 quaternions or read a WAN/pose file.

The Python command retains the five original mutations (packed vertex stride,
UV state, pivot subtraction, reflection winding, and source hashing) and adds
three pose mutations: reflection parity, root anchoring, and active pivot
composition. Each edits only a separate source tree under `.artifacts/`, requires
the intended assertion to reject it, reruns the unmodified baseline, checks
production SHA-256 hashes, and removes its own source/fixture trees. Its report
is `.artifacts/result.json`. The hash/copy guard covers exactly the seven linked
pure-library inputs, including the pose layer, not native adapters.

Paths are repository-relative and both runners work independently of shell path
separators. Windows needs permission to create symlinks (for example Developer
Mode); symlink checks fail rather than silently skipping when unavailable.
Case-sensitive filesystems exercise ambiguous sibling names; case-insensitive
filesystems instead verify rejection of the same-file uppercase alias without
overwriting the lower-case fixture. Both branches preserve the 2,751-check count.

**Not native Unity coverage:** the machine-specific editor compile project and
local-art import/preparation tools are not part of this suite. Real adapter checks
require an actual licensed Unity editor and the separate
`Blitz.Editor.PlayerAssetImporter.VerifySyntheticAdapter` entry point.
The actual source standing-pose/343-vertex comparison remains separate; these
synthetic checks do not establish native pose integration, continuous animation,
browser rendering, or full-game fidelity.
