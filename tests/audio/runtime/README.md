# Synchronous sound-VM Runtime transport

This is the **next sound-runtime candidate**, built on published movement157.
It is not audible output, DSP3 execution, full C1/SPFX or whole-game parity.

From a clean coherent checkout, without original assets or Unity:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/audio/runtime/run.py
```

The default compiles the actual Core/pure dependencies,
`FixedStepBridge.cs` and `SourceSoundVmTransport.cs` as C#7.3, warnings as errors.
Synthetic dispatch fixtures exercise transport only; an actual legacy Core
also proves detached empty FIFO batches through Step/Restart. Sixteen isolated
mutants change real production transport/bridge code and must **compile** then
fail their exact offending-field assertion. Compilation failures are not mutant
rejection.

The recorded checkpoint passes **53 asset-free assertions / sixteen compiled
production mutants**. Explicit original157 Core inputs add **30 actual Core
assertions** (83 total), including the real60-to57 adapter's catch-up/skip and
restart paths. Complete actual Unity6.3 API compilation covers **89 runtime
sources / all17 owned UI/runtime files**, zero diagnostics for Editor/WebGL.
Those numbers identify distinct scopes, not audible or full-game acceptance.

Optional actual157 Core/ANI_CODE batches require both explicit original inputs:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/audio/runtime/run.py \
  --fixture-manifest "$REAL157_FIXTURE" \
  --sound-functions "$ORIGINAL_SAFN" \
  --unity-editor-data "$UNITY_EDITOR_DATA"
```

`REAL157_FIXTURE` is the strict real-input factory manifest used by the existing
Core campaigns. SAFN is the exact1983-byte payload with SHA256
`4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280`.
The actual tests run unchanged `t_dshoul_anim` ANI_CODE with named test actor
state and explicit raw draws, inspect real Core batches/offsets and demonstrate
zero/nonempty output, one-time drain, retained batches, partial failure and
Restart retaining the binding. They do not claim a public-input/full-match
scenario, original PRNG, original normal camera or C1 contact qualification.
No selector/private-method reflection or copied production factory is used.

`--unity-editor-data` additionally runs the complete actual Unity6000.3.24
C#7.3/.NET Standard2.1 Editor/WebGL API inventory with real engine/target
references, all owned Runtime/HUD/Bridge/helper sources and hashes. No Editor
process, Unity stubs, native resource import or fake audio player is used.
`--no-mutations` is an explicitly labeled author baseline, not full mutation
acceptance.

## Actual route and limits

- Services use a new sound-VM identity; canonical SAFN is read with Core's fixed
  pin and bound **after actual M1 construction/before the first source wake**.
  Old eight-argument M1 services are not silently upgraded.
- Core alone selects at six original ANI_CODE sites using its existing shared
  RNG. Runtime never selects, draws RNG or replays `player_down`.
- Bridge drains after **each** host `Step`, before raw cues, including catch-up,
  skipped/empty source wakes and a producer exception (`finally` drain).
- Pairing uses the **same OriginalCue reference**, not reconstructed context or
  event text. FIFO, exact subcall order/full unsigned raw words, duplicates,
  empty batches and retained batches are discriminated.
- The paired Resolved path replaces—not supplements—the old generic callback
  path. Resolved zero calls are legitimate, not missing output. Unsupported and
  unpaired callbacks remain visible unsupported; partial MissingInput records
  preserve calls/offsets and fault until Restart.
- `ISourceSoundVmOutputConsumer.ConsumeResolved` is a typed contract for an
  already-resolved record containing ordered Bank/PlaylistId/raw Volume/Pan/
  PriorityWord/callee/context. **No implementation is bound.** Nonempty output
  fails visibly rather than claiming it played. A failing output cannot retry
  the old calls. Restart clears transport/failure while Core retains the
  immutable data binding; focus/input clearing does not retry the selector.
- Raw `SourcePresentationCueObserved` and `SourceSoundVmDispatchRaised`
  observability are distinct from playback. Detached old records remain
  unchanged. One host/camera/cue lifecycle remains.

All eight public PM/DM firmware images and CRC deployment wrapping are available
and verified; they are **not missing**. Exact playback still needs the historical
`TARGET_SYS`/localset/build identity and matching legitimately supplied32768-byte
bootstrap: Seattle ADSP2115 `sound102.u95` SHA1
`db80aa4a645804a4574b07b9f34dec6b6b64190d`, or Vegas ADSP2104 `vegassio.bin`
SHA1 `f6e8405cfa604528c0224401bc374a6df9caccef`. Neither target is inferred here.
The existing pinball decoder is not a full DCS2 board runner. No gain/pan/
priority/stereo/playlist-control timing is approximated.

Original data, converted resources, generated project copies and receipts stay
ignored/local. Native licensing198, actual audio/FX/DSP board execution and all
sixteen source-parity rows remain separate open acceptance.

## Clean CI closure

The existing single `game-build.yml` job retains every legacy/core/oracle/
camera/fixture/mutation/tooling check and adds:

- Sound VM compile/input guards and the actual800,734-assertion/eleven-mutant
  original-C/VM campaign from QA's explicit clean fixture.
- This Runtime suite using the same fixture's actual SAFN, including all
  sixteen compiled mutants; no Unity reference or private authoring path.
- Team's exact asset-free package/resolved-consumer command and four default
  compiled package mutants. Its separate local58/14 actual-art qualification is
  not silently claimed as clean-CI coverage.
- `test_ci.py`: actual workflow preservation, the69-input union retaining the
  old65/camera closure, and eight isolated workflow regressions.

The existing kernel fetch/preparation now produces the audio-aware443-file
fixture, including `audio-functions/`. Verified oracle bytes are reused by the
kernel fetch; the four-input sound acquisition helper copies/verifies from that
69-pin root instead of redownloading. The existing standalone function producer,
original-C proof and mutation command remain.

The job has bounded30-minute headroom rather than dropping checks to fit the
former20-minute budget. Added commands are measured locally and recorded in the
ignored final handoff; local durations are not a hosted-runner timing guarantee.
