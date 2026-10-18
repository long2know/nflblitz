# Pure camera regression checks

From the repository root, with the .NET 10 SDK:

```sh
dotnet run --project tests/camera/CameraChecks.csproj --configuration Release
python3 tests/camera/run.py
```

This **C# 7.3 / .NET 10**, dependency-free console project links the exact
production `NormalCameraModel.cs`, contracts, gameplay kernel, and existing
`Assets/Scripts/Editor/NormalCameraChecks.cs` suite. It also links the actual pure
`AssetImport/*.cs` dependencies, with the recursive `Contracts/**/*.cs`
include preserved. No camera implementation or
synthetic tests are duplicated. The small entry point calls that original suite
and checks its actual counter is exactly **3,568 behavioral assertions**, not
zero or an unchecked success message.

The existing checks cover independent pinhole projection, geometry visibility
and finite bounds, Home/Away state transitions, passes, incomplete/dead/final
states, restart/continue, source units, tracking/dead zone, frame-rate independence,
suspended-frame handling, viewport aspects/field edges, supplied actor geometry,
and a real public-API gameplay-kernel trace.

The Python command reuses the six original mutations: focal length, QB lead,
dead-ball orientation, silhouette fitting, archive units, and frame-time response.
Each modifies only its own copied source tree under `.artifacts/`, must fail the
intended assertion, and is followed by a clean production baseline. Production
SHA-256 hashes **and inventories**, including the real AssetImport dependency,
are guarded. The mutation copies include that dependency too. The script removes its copied sources and leaves only
ignored SDK/build outputs and `.artifacts/result.json`. Paths have no editor/home
dependency and are portable across Windows/Linux.

Both actual sound selectors, `SourceAudioFunctions.cs` and
`SourceC2SoundFunctions.cs`, are copied and hash-pinned with every isolated kernel.
Removing either must fail its real compilation guard, without executing the
camera test or counting that failure as one of the six assertion-mutant kills.

**Not native Unity coverage:** `UNITY_EDITOR` and `BLITZ_CAMERA_VALIDATION` are not
defined. The original suite's Unity projection, real `PlayCamera`, and HUD viewport
checks remain conditional and need an actual licensed Unity editor with
`Blitz.Editor.NormalCameraChecks.Verify`; this pure run neither stubs nor claims
those checks. The 3,568 count matches the original pure harness, not the separate
3,595-assertion native run.
