# Narrow B1 camera-stage caller follow-up

This layer changes `Runtime/BlitzRuntime.cs`, the authorized brace/scope repair in
`Presentation/PlayHud.cs`, and focused caller checks. It does not replace or
rewrite the protected 33-file UI manifest.

## Activation and ownership

- Explicit `?actors=graybox` still selects `GameSimulation(int seed=1)` and
  legacy controls/HUD. It does not configure the source camera.
- Source actor resolution still requires the compiled original team, final117,
  HUD and match-service Resources. Missing new resources remain a visible
  `SOURCE MODE ERROR`, not a silent legacy/texture/font fallback.
- Source-only setup calls the final Team **four-argument** `ConfigureSourceStage`
  with Core's actual stage ordinal, shared camera RNG and canonical source roots.
  `SampleAtSourceTick` is registered before the Core provider is exposed and
  before the first bridge wake.
- Core invokes the wrapper inside its actual source stage; the wrapper invokes
  the registered producer and checks the consumed source tick/heading.
  No second accumulator, numeric raw-GM fabrication or render getter is used.

The four-argument producer intentionally supports the **current B1 semantic
`GamePhase` lifecycle**. Full original kickoff/gopher/unpublished raw-mode
branches remain open; this follow-up does not certify them.

## Exactly one path

Presentation cues travel through the existing per-Step bridge event, then one
Runtime event, then the single Team `ConsumeSourceCue` subscriber. Runtime does
not also call `SourcePlayback.Execute`. Known constructor/restart cue batches
use the same path once.

Normal host observations use one Team subscriber. Restart explicitly gives that
same observer the rewound state before initial cues/render; Team owns reset and
suspension/resume. The Runtime transport counters are not a second camera clock.
Subscriptions are removed once on destruction. Unbound original effects/audio
still throw; no noop/silence/fake-click consumer is installed.

## Validation

The permanent default works from a clean checkout/current release source root.
It reads no protected `.squad` snapshots, original assets, absolute machine paths
or Unity SDK. It checks publishable presence/XML for the three interface project
files as well as the actual Runtime/HUD caller:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/interface/stage_followup.py
```

Optional actual C#7.3/.NET Standard2.1 API compilation in that release checkout
dynamically includes its real runtime dependencies and the **complete
sixteen-source owned UI inventory**:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/interface/stage_followup.py \
  --unity-editor-data "$UNITY_EDITOR_DATA"
```

It starts no Editor. Each profile records the actual compiled owned-file list
and hashes. Omitting/replacing `BlitzRuntime`, `PlayHud` or `FixedStepBridge` is
rejected by six inventory mutations. The scope repair has its own regression
mutation. The WebGL profile references the real target
`UnityEngine.WebGLModule.dll`, not just the Editor engine assemblies.

Concurrent authoring validation is a **different explicit mode**. It requires
all four roots/manifests; missing/wrong/partial options fail rather than falling
back to Primary. An explicit dependency overlay requires its exact SHA256:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/interface/stage_followup.py \
  --core-root PATH_TO_PROTECTED_B1 \
  --team-root PATH_TO_FROZEN_TEAM \
  --base-root PATH_TO_PROTECTED_BASE33 \
  --team-manifest PATH_TO_FINAL_TEAM_MANIFEST \
  --dependency-overlay PATH_TO_PARENT_OVERLAY/SourceAnimationLibraryAsset.cs \
  --dependency-overlay-sha256 6f5043f50a36426ac59065e68724a826b35877cbc81d162da3caa93300e93f52 \
  --verify-clean-checkout \
  --unity-editor-data "$UNITY_EDITOR_DATA"
```

The parent overlay explicitly initializes four serialized fields to their
existing CLR `null` default. Guards require **only** that change: field names,
load guards, editor-only initializer, APIs and resource identities cannot be
silently changed. It is not warning suppression. No Core/Team/Primary source is
edited by this script. Nine asset-free tests cover the caller, scope,
inventory, explicit-root/overlay failures and missing/invalid project files.

`--verify-clean-checkout` constructs a selected release-source layout and runs
the copied script with **no arguments**, with no `.squad`, original art or Unity.
This proves that explicit authoring inputs are not hidden permanent defaults.
When the explicit Unity reference option is also supplied, it additionally runs
the copied optional API command using only that clean release checkout—no
snapshot options or hidden dependency overlay—and verifies both profiles there.

The owned `PlayHud` event methods are now corrected to class scope. The old33
snapshot intentionally preserves the historical defect. The previous
`UnityAPICompile.csproj` checked only three new adapters; it did **not** compile
`BlitzRuntime`, `PlayHud` or `FixedStepBridge`. Its historical green result must
not be described as a complete UI/caller compile. This new complete inventory
closes that false-pass possibility instead of suppressing warnings or methods.

The final explicit authoring validation compiled **80 actual sources, all 16
owned UI files, with zero warnings/errors in both `UNITY_EDITOR` and
`UNITY_WEBGL`** against actual Unity6000.3.24f1 .NET Standard2.1 and engine/target
assemblies. Its one external overlay is the exact parent-supplied file above.
Protected B1, final Team and the historical old33 remain unchanged.
`api-inventory.json` preserves both profile results, source origins, overlay pin
and the complete owned list.

This is an API compile, **not** an Editor/native/browser/audio/full-game parity
pass. Licensing exit198 is a separate native entitlement limitation, not an API
compile result. M1 runtime-data injection remains a separate follow-up.
