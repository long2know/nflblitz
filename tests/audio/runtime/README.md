# Synchronous sound-VM Runtime transport

## Original pregame process/output owner

`SourcePreGameSoundService` is a separate pure source process host, not the
unadvanced football backdrop. Capture the actual four physical inputs through
`CapturePhysical`, then call `AdvanceHostTick` from the real60Hz host clock.
The lossless FIFO/two-prior switch scan and registered menu processes run at57Hz.
Joining and local management are explicitly non-producing portable phases.

At actual accepted entry, `TryTakeEntryActivation` supplies the exact source
clock origin and known permanent-bank/reserved state from
`load_permanent_snds`/`plyr_strt_butn`. Runtime must accept that typed entry
before consuming the canonical process-call/control FIFOs. Source UI executes
only through `ISourcePreGameMenuDriver.OnSourceMenuWake`; `wake.Emit` accepts
the closed original operand enum in its actual caller/helper/input scope.
Retained scopes, async sound events, wrong helper/station and generic clicks
cannot allocate output ordinals. IO completion is data-only until the actual
source continuation resumes.

Actual processes include `pre_game_scrns` PID0x0c00, option/welcome PID0,
`player_cursor` PID200/202/204/206 in qcreate-head order, CAP/exiter PID0x1000,
and countdown PID601. Original welcome50/RNG3, option30-button gate/48 return,
YESNO33, letter8+1 and CAP flash21 timings are checked. CAP/normal helpers use
their distinct source operands; no PIN/name data enters the process contracts.

Only the actual versus continuation returns `SourcePreGameSoundHandoff`.
Runtime starts the legitimate new VS epoch while retaining the actual bank
map, then constructs the real Game using the once-claimed random factory and
typed `BankState` bootstrap overload. `ObserveGameClock(actualGame)` captures
the full actual source clock (not merely the last random draw) for fresh
rematches; `PrepareGameReset` must precede same-Game reset. The same underlying
random stream is retained without reseeding or selecting in the consumer.
The initial helper remains explicitly managed-preview/ExternalUnverified,
not a claim about the target PRNG. Old profile defaults are unchanged.

Focused commands, without a new proof platform:

```sh
bash tools/source-player/dotnet.sh build tests/audio/runtime/SourceSoundRuntime.Checks.csproj --no-restore -c Release
bash tools/source-player/dotnet.sh tests/audio/runtime/bin/Release/net10.0/SourceSoundRuntime.Checks.dll --pregame-only
bash tools/source-player/dotnet.sh tests/audio/runtime/bin/Release/net10.0/SourceSoundRuntime.Checks.dll --pregame-data "$APPROVED563_DATA"
```

These are actual production-process/output tests with bounded menu drivers;
they are not browser hearing, native graphics-load timing or whole-game proof.
The original assigned DCS timer programs remain real playback dependencies,
never silent holes or flattened substitute calls.

## Complete-original producer integration — October 30, 2026

The separate complete-original303 capability adds closed authenticated original
sound tuples without weakening the legacy six/15 bindings. Core owns shared RNG,
native speech-child eligibility/scheduling, and actual bank/track lifecycle
producers. Live `SourceProcessSoundControl` and sound calls share the canonical
output ordinal; sourceTick0 `SourceBootSoundOutput` is a distinct named versus
entry, not a fabricated live process. Actual `vs_screen` deletes the old cursor
bank and loads `cursors2`; this is source state, not a consumer alias.

Existing transport/producer checks retain their original approved input scope.
The optional complete-original action-tail runner exercises the new actual
producer/VM cases. Original playback, source-bank package, browser hearing and
target-DSP evidence remain separately owned and are not inferred from these
component assertions.

The transport retains the original ordered selection records. The portable
host additionally has an **explicit, opt-in qualified PCM output** described
below. Neither SDK checks nor command submission prove browser hearing,
DSP3 execution, full C1/SPFX or whole-game parity.

From a clean coherent checkout, without original assets or Unity:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/audio/runtime/run.py
```

The default compiles the actual Core/pure dependencies,
`FixedStepBridge.cs`, `SourceSoundVmTransport.cs` and the portable
`SourceAudioPlayback.cs` as C#7.3, warnings as errors.
Generated SAUD/dispatch fixtures exercise PCM output and transport; an actual
legacy Core also proves detached empty FIFO batches through Step/Restart. Sixteen
original transport mutants plus two PCM/native-boundary mutants change real
production code and must **compile** then fail their exact offending-field assertion. Compilation failures are not mutant
rejection.

Each isolated tree includes both actual sound selectors, the real
`SourceC2Composition.cs`, and its shared `FullKernelSources.props`. They are
copied and fingerprinted with the other production inputs; staged builds cannot
fall through to a different checkout's selector or factory.

The historical pre-PCM checkpoint passed **53 asset-free assertions / sixteen compiled
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
  PriorityWord/callee/context. The default PlayerHost remains recording-only.
  An explicitly admitted PCM composition binds the qualified output below;
  without a consumer, nonempty transport output fails visibly. A failing output
  cannot retry the old calls. Restart clears transport/failure while Core retains the
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
The existing pinball decoder is not a full DCS2 board runner. The opt-in PCM
profile below is a disclosed portable playback policy, not reconstruction of
that missing target-specific DSP execution.

Original data, converted resources, generated project copies and receipts stay
ignored/local. Native licensing198, actual audio/FX/DSP board execution and all
sixteen source-parity rows remain separate open acceptance.

## Clean CI closure

The existing single `game-build.yml` job retains every legacy/core/oracle/
camera/fixture/mutation/tooling check and adds:

- Sound VM compile/input guards and the actual800,734-assertion/eleven-mutant
  original-C/VM campaign from QA's explicit clean fixture.
- This Runtime suite using the same fixture's actual SAFN, including all
  eighteen compiled mutants; no Unity reference or private authoring path.
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

## Opt-in portable qualified PCM output

`tools/source-player/Runtime/SourceAudioPlayback.cs` implements both existing
sound-output interfaces. `PlayerData(..., decodedAudio: admission)` is the
deliberate opt-in; its default is null. It does not add entries, exceptions or
consumption exemptions to the fixed563 catalog, change gameplay compatibility,
or enable audio through an implicit URL option. The sink must additionally
implement `ISourceAudioSink`; otherwise construction fails by name.

An admission requires a name, a separately established authorization reference,
exact package/bank/source-blob/source-SHA256 identities, and an explicit
bank:playlist allowlist. Identity validation does **not** establish rights.
All allowlisted programs are preflighted through the real SAUD reader and must
be isolated-qualified, once-only programs. Nonstandard end waits, loops,
unprepared IDs, control-only programs, missing banks and unknown priority bits
fail explicitly. No original banks or derivatives are included by this change.

The host drains the same VM FIFO at its existing after-Step boundary and
interleaves it with direct calls using the existing common `OutputSequence`.
`DeliverNextNative` consumes one exact native dispatch so a later whistle cannot
overtake an intervening direct call. ANI cues still pair by object identity.
No selector, random stream, draw or post-Step reselection is added. Every
dispatch is preflighted before its subcalls are scheduled. Unsupported selectors
or runtime tuples fault the output, discard unsent commands, stop the receiver,
and require Restart; partial delivery is never retried.

Six tracks, reservation masks, stop-all clearing reservations, explicit track
overrides, and equal-priority `SND_PRI_SET_NOINT` protection are grounded in the
pinned Goose `include/goose/sound.h` and `lib/sound/sound.c`. Lower numeric
priority is stronger per pinned `include/sndcalls.h`. Bank deletion removes
the host mapping without stopping an already-playing voice, as the original
`snd_bank_delete` documents. Samples remain cached for that receiver's lifetime.

This named **portable** policy picks the lowest eligible slot for allocator
ties, scales the source-clamped volume byte linearly by255, and maps pan0/127/255
to -1/0/+1. Those are disclosed output projections, **not** verified DSP3
transfer curves, stereo mixing or arbitration equivalence. Playlist mix is
already baked into PCM and must not be applied again. The exact decoded sample
count includes the two decoder tail frames; looping that PCM would not reproduce
the compressed-stream decoder's loop state and is forbidden.

### Frontend receiver contract

Implement `ISourceAudioSink` on the existing Browser sink; C6 does not edit JS
or `Browser.cs`.

* `AudioSample(string id, int sampleRate, int channels, byte[] pcm16Le,
  string metadata)` uploads an immutable resource once. Decode signed
  little-endian PCM16 to float by dividing by32768. Current data are mono31250Hz.
  Metadata schema `source-audio-sample/v1` supplies SHA256, sampleCount,
  `mixAlreadyBaked=true`, admission name and `nativeDspEquivalent=false`.
  The ID is `pcm16-` plus the PCM hash; equal resources are deduplicated.
* `AudioCommands(string json)` receives a small
  `source-audio-playback/v1` batch, never base64 sample data. Fields:
  `policy`, `sampleRate`, `sourceTicksPerSecond`, `epoch`, `sourceTick`,
  `commands`. Commands use camelCase serialized `SourceAudioPlaybackCommand`.
* Every command has monotonic `sequence`, `epoch`, `sourceTick`, and `atSample`
  on the31250Hz epoch timeline. Source calls retain `outputSequence` and
  `subcallOrder`. Starts are quantized by integer
  `sourceTick * 31250 / sourceTicksPerSecond`, not the render frame.
* `play` identifies `voiceId`, track0..5, sampleId/count, bank/playlistId,
  packageSha256, sourceGitBlob, complete programHex, original raw
  volume/pan/priorityWord, clamped bytes and `portableGain`/`portablePan`.
  `loop` is always false. A preempted voice receives its ordered `stop` first.
  Start one real buffer source and retain its voice ID; use the supplied gain
  and pan without recomputing playlist mix.
* `stop` targets an exact voice/track at `atSample`; completed buffers also end
  naturally. An old node's `onended` must not remove a newer voice on that track.
  `adjust` changes the specified voice's gain/pan. `rejected` is an explicit
  allocation result, not a played call.
* `reserved`, `bank-load`, and `bank-delete` are ordered lifecycle notifications.
  Bank deletion must not dispose a resource still used by a voice.
  `stop-all` stops all voices and clears reservations.
* `reset` is a new, increasing host epoch: cancel **all** old nodes/queued
  scheduling, retain uploaded immutable resources, and establish a fresh clock
  anchor at source tick0. Discard commands belonging to older epochs.
  Command sequences and voice IDs never rewind; sequence gaps can occur when
  an unsent old queue is deliberately discarded.
* `active` freezes/resumes the audio clock with the source clock, without
  restarting nodes or replaying elapsed cues. `reset.active` preserves pause
  across a new match. Duplicate pause/focus calls emit nothing. Pause/focus
  behavior is a **portable browser policy**, not an original cabinet process.
  Require a real user-gesture audio unlock before enabling this composition;
  do not accumulate/replay pre-unlock sounds or claim autoplay success.

The synchronous receiver must validate and accept the batch or throw. It must
not silently ignore unknown samples, malformed commands or a failed audio
context. Submission counters are not evidence of audible browser output.
Frontend owns the Web Audio clock anchor/look-ahead, gesture handling, actual
buffer scheduling, suspension and browser verification.

### Available inputs and remaining source domains

The approved563 manifest SHA256
`150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6`
contains only `kernel/sound-functions.safn`, not decoded samples.
Known local preparation receipts describe41 decoded CURSORS/PLAYERS programs,
of which23 satisfy the current isolated-qualified timing. They explicitly
retain **unverified rights/local-only** restrictions; this is not an approved
game admission and none were copied or converted by C6.

Those packages do not prepare PLAYERS127/139/140; PLAYERS57 has a nonstandard
end wait. Their CURSORS bank has98 slots and cannot supply current audible
IDs100..102. This is an exact version/input mismatch, not proof original banks
are absent: the pinned original source tree contains78 audio-named bank/WAV
entries, including ROOT/GENERIC/MAIN*/HALFTIME/OTIME/GAMEOVER/team banks.
No additional asset payloads were downloaded.

Selection-timer, possession-speech and actual cabinet-random-selection emissions
now use the existing common direct/VM `OutputSequence`. Their immutable `Output`
references identify the same `SourceProcessSoundCall` drained by
`GameSimulation.TakeSourceProcessSoundCalls()`; old constructor-only views remain
explicitly unbound at zero. The cabinet receives a scoped typed producer on its
actual game_proc input frame, not from an extra input scan or rendering callback.
Original match music/crowd,
announcer/bank/stop lifecycle producers are not fabricated from HUD state.
The scheduler's lifecycle operations are ready for a separately owned,
source-timed producer. Full playlists/loops/DSP execution, additional reachable
selectors and authorized original data admission remain
separate work.

The actual instruction dispatcher stamps child/page/tick operands immediately
before its executing `Effects.PlaySound`. A synchronous adapter can obtain that
same record once through `TakeExecutingSourceInstructionSound(sound, context)`
or `TakeExecutingSourceKickoffInstructionSound(sound)`. Wrong/stale caller,
tick, ordinal, operand, duplicate and out-of-process requests fail; these are not
public next-number allocators. Same-Game Restart retains the shared counter;
only a fresh Game is a new producer. `SourceAudioPlayback.Consume(SourceProcessSoundCall)`
uses the unchanged exact-program admission and positive-order guards.

Focused current-source producer checks use the unchanged approved563 data:

```sh
python3 -B tests/audio/runtime/run.py --producer-data "$APPROVED563_DATA" \
  --producer-mutations-only
```

This normal suite executes the actual natural kickoff child, four-page process,
timer, cabinet random gesture/controller and a labelled CAMBOT message-admission
fixture. A separate labelled managed player-process/emitter fixture executes the
real direct/native/direct producers, then the actual instruction body; it does
not claim default-audibles admission or a public-input OOB play. No test supplies
their positive output IDs. Four isolated compiled controls cover missing timer/
cabinet metadata, same-Game counter rewind and an instruction-context alias.
Generated PCM remains an explicitly non-original test fixture; missing original
programs still fail. No bank authorization, native ABI/DSP or hearing is inferred.

The asset-free suite adds three focused generated-fixture scenarios:
transport -> PCM/output ordering (including a native/direct boundary), admission
and explicit failure, and priority/stop/delete/pause/restart/partial-sink
lifecycle. The normal entrypoint also tests same-/fresh-producer restarts,
fresh-epoch rejection and retained PCM resources. Eighteen compiled semantic
controls remain available through the existing runner. These checks do not
prove original whole-game or audible parity.
