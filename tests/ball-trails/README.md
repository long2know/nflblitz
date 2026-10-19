# Original non-fire football-mesh trails

This increment implements `player.c:trail_ball_proc`, not a particle substitute.
It shares the explicit `source-fire=portable` opt-in and existing level-zero
scheduler. Default/StrictSource factories, physics, AI, RNG, clocks, entry
profiles, source selectors, scenes, prefabs, packages and settings are unchanged.
Publication/material review is separate; this is not whole-game acceptance.

## Exact lifetime and draw boundary

- PCLOUD falls through BTRAIL: both queue `balltrl` on **every** selected ball
  wake, with four zero arguments and `BALL_PID=0x0900`. The commented RL_BALL
  setter is not executed. A birth sleeps1 before its first level-zero visit.
- The new typed `ReadBallRender` callback reads actual XYZ/fwd/fwd2/phi/scale/flags
  **at process entry**. It does not reuse the fire point-only callback, capture
  at queue time, re-pose an actor, or track the live ball afterward.
- The immutable render projection retains every field used by `draw_ball`.
  Native unused `ball_data` physics fields and its memory layout are not cloned
  or integrated. This is source draw behavior, not a byte-copy/ABI claim.
- Local flags are overwritten with `BF_ALT`. Five source wakes publish
  alpha **215/200/185/170/155** and scale **2.25/2.15/2.05/1.95/1.85**. Each
  stage sleeps1, including the last. The following wake deletes the object.
- Existing suspension preserves the signed-short sleep field: no hidden/menu
  aging. Selection hides/suspends existing BALL nodes; SPFX cleanup/resume does
  not delete or resume them. Selection exit resumes/unhides them. New-play
  SPFX/puff cleanup is not invented BALL cleanup. Host restart clears the epoch.
- Draw visibility uses the **current global** carrier/Phantom; white uses
  **current global** BF_WHITE; the alternate decal is **local**. Current global
  `pup_bigball` multiplies only the copied basis by1.5 at draw time.
- Both consumers reuse genuine `medball2` SDL/source-authored geometry,
  HEDSHU/HEDSHU2, original TRIGS and Y*X(-elevation)*Z(roll). Main-ball output
  retains alpha255 and its existing20-value packet. Ghost20-value packets follow
  it without changing those first20 values.
- CC_ALPHA drives output alpha **and** TEXTURE_TIMES_ALPHA multiplies RGB;
  SRC_ALPHA/ONE_MINUS_SRC_ALPHA then blends that result. This is not merely a
  translucent texture. The current GLIDE3 white convention overwrites CC_ALPHA
  withFF, so white ghosts do not receive an invented fade.

Unity appends the main ball then football ghosts in source equal-priority arrival
order through the existing command buffer, before the puff pass. WebGL uses the
same order, source321 winding/back culling, LESS, depth writes and separate
SRC_ALPHA/ONE_MINUS_SRC_ALPHA, ONE/ZERO blending. This qualifies the declared
portable ball/effect boundary, not the native global object list, target cull
inheritance, alpha-test inheritance, WBUFFER or pixels.

Killed stack-backed owners and externally deleted objects keep the existing
explicit stale policy: Strict rejects undefined access; Portable records
orphan-freeze or next-access cancellation. No native heap/pointer behavior or
automatic kill cleanup is fabricated.

## Source provenance and safe original-C comparison

The pinned NFL archive is `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`,
tree `a7e0352125d46075b7d98e87253ab89b9160a4c7`.
`oracle.py` checks exact SHA256s for player/pipe/TRIGS/id/process/object/nfl/playsel.
In particular, `include/id.h` is blob
`d2cc50865dd105c4143ac6b769765bd859cc4ff4`, SHA256
`06748a93cc40f76bd1a38a4315744449f6c743147a2ad5439221d564583720ea`.
It authenticates BALL_PID/OID_BALL/DRAWORDER_BALL/RL_BALL instead of treating
an unverified cached header as source truth. Verified nfl/playsel blobs are
required; their known stale raw-cache copies are rejected.

Only exact original trail/draw/math bodies are extracted to **ignored private**
output. The authored typed shim uses an unsigned alpha at GL color packing to
avoid the original signed `alpha<<24` overflow. Its cooperative stack remains
alive during sleeps, and object data is never read after return. Undefined
sanitizers pass. The dispatch adapter is explicitly declared host behavior,
not native qsort/tie/restart/stack ABI execution.

The regression first compiled against the unchanged base and failed because
BTRAIL returned a selector without queuing a process. Final production checks:

| Evidence | Observed result |
| --- | --- |
| Focused actual C# | 3,495 assertions; normal7-row and suspended14-row process traces;240 original-table global/local draw cases |
| Extracted original C | Exact output field/length comparisons, typed state/material/matrix comparisons, undefined sanitizer passed |
| Causal controls | 13 actual-production C# controls compile and fail named assertions;6 actual JS consumer/shader-contract controls parse and fail named assertions |
| Real Core/preview composition | 2,642 assertions;121 natural ghosts,116 deleted; actual skipped-host hold, pause and public restart |
| Retained related suites | Core156, fire18,800/original-C12 cases, main-ball1,752 and fire-adapter63; existing main-ball7 compiled/4 JS controls also pass |
| Renderer/artifact gates | 14 renderer tests; actual browser artifact gate plus5 artifact-only negative controls |
| Unity reference-only | All5 archived6000.3.24f1 C#7.3/netstandard2.1 profiles:115 runtime/14 Editor sources; no Editor executed |
| Asset metadata | 144 assets/meta/GUIDs; no new Assets files or changed existing metadata |

Example commands, with explicit authorized read-only inputs and new output paths:

```sh
python3 -B tests/ball-trails/run.py \
  --bundle "$READONLY_DATA/fire" --manifest-sha256 8bca6ef9e1f089209aca7201e30d281d54444b6cc5b621c42f339394ef04fe88 \
  --original-player "$ORIGINAL_PLAYER" --original-pipe "$ORIGINAL_PIPE" \
  --original-trigs "$ORIGINAL_TRIGS" --original-id "$ORIGINAL_ID" \
  --original-process "$ORIGINAL_PROCESS" --original-object "$ORIGINAL_OBJECT" \
  --original-nfl "$VERIFIED_NFL" --original-selection "$VERIFIED_PLAYSEL" \
  --output tests/ball-trails/.artifacts/production-new
python3 -B tests/ball-trails/mutations.py \
  --bundle "$READONLY_DATA/fire" --manifest-sha256 8bca6ef9e1f089209aca7201e30d281d54444b6cc5b621c42f339394ef04fe88 \
  --output tests/ball-trails/.artifacts/controls-new
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- "$READONLY_DATA" --ball-trails-only
node --test tools/source-player/ball-trails.renderer.test.mjs
```

The older broad `Checks --require-portable` assumes a one-station cabinet fixture
and rejects the supplied four-human bundle before reaching effects. Its failure
is retained, not patched away. Use the existing `--free-ball-only` and the focused
`--fire-adapter-only` route for those related checks on this four-human data.

## Actual owned browser candidate

The unchanged private bundle has544 payload entries (545 files including its
manifest), with manifest SHA256
`ff2851555e6f8b01814b4a29dd20f8194761662debe8e6cb99db789dc5349f0d`.
The October18 candidate's build receipt is
`8676f984eb6895377262db917000a9426b5f8d705ccb2ceaad34cf87f9684065`.
Own8093 was built **before** its unique Compose project was force-recreated;
the HTTP receipt and both actual WASM assemblies were verified before Chrome.

A genuine four-human kickoff produced the original five-football chain.
Public Escape held source tick177; all five material alpha stages were present,
with223 actual colored/nonwhite ghost-only GPU pixels. One natural object queued
at176, entered177, published its five recorded source stages177..181 and deleted
at182. Real gameplay continued to317, epoch0, without reset or state injection.
The native createProgram journal contains only the existing scene/HUD/fire
programs, with actual linked shader sources and read-back material/matrix/
texture/cull/depth/blend state.

PNGs are unmodified gameplay images, not the diagnostic framebuffer:

| PNG | Source-tick bracket |
| --- | --- |
| `natural-nonfire-ghosts-public-pause.png` | [177,177], clearly public-paused; existing native preview pause HUD is visible |
| `after-original-ghost-deletion.png` | [190,212]; the tracked old object is gone, newer ghosts still emit |
| `continuing-real-gameplay.png` | [298,316]; continuing unpaused gameplay |

The failed first attempt is retained: extra per-frame GPU diagnostics sampled
render ticks177,178,180,182… and missed displayed stages. It is not relabelled a
pass. The accepted attempt uses read-only completed snapshots and the existing
actual C# **per-source-wake records**; completed-render stages are reported
separately (0,2,3). It does not equate57Hz source wakes with rendered frames,
slow/accelerate the simulation, call private C#, or force a scene. All five GPU
material stages coexist in the genuine paused chain.

To reproduce, first build, then start only the explicitly owned preview:

```sh
python3 -B tools/source-player/build.py --data "$READONLY_DATA"
SOURCE_PLAYER_PORT=8093 SOURCE_PLAYER_BIND_ADDRESS=127.0.0.1 \
  docker compose --env-file /dev/null -p nflblitz-source-ball-trails \
  -f tools/source-player/compose.yaml up -d --force-recreate
# Verify local and HTTP build-info.json hashes, then supply that exact receipt:
bash tools/source-player/with-browser-runtime.sh tools/source-player/ball-trails.browser.mjs \
  --url 'http://127.0.0.1:8093/?source-fire=portable' --receipt-sha256 "$VERIFIED_RECEIPT_SHA256" \
  --seconds 60 --output tools/source-player/artifacts/ball-trails-new
SOURCE_BALL_TRAIL_EVIDENCE=tools/source-player/artifacts/ball-trails-new/evidence.json \
  node --test tools/source-player/ball-trails.artifacts.test.mjs
```

No Editor/import/player run, native generated header, target FMA, WBUFFER,
Texus/WMS quantization, native pixels, full scheduler ABI, performance budget,
audio/DSP or whole-game parity is established. All sixteen parity rows remain
open. The independently owned terminal-event fix is not integrated here.

## Integrated C34 provenance/status appendix

Everything above is the unchanged frozen C33 trail record, including its
historical integration status and original receipt; it is not relabelled C34
evidence. The separate integrated worktree is rooted exactly at merged C34
`b163938fe4ba494bb69a116d35c000d5caeb3447`, tree
`78107e2b2a442bf0fab98694bef15f4420ddee5c`. Its reviewed production/test/renderer
semantics are unchanged; C34's terminal notification, tests and exact40 CI
guards are retained. Only permitted documentary provenance/status is added.

Fresh builder receipt
`936682d6b2f054308b95af5db759717c02fa5128a842697fd5666112723eaffa`
and metadata-annotated served receipt
`2716dc210f8fabccef4f10b75ecfeb4e2381ac09f353f87b698ab80d58398948`
identify the genuinely rebuilt binaries. Unchanged Core/Checks routes and full
fire18,800 pass, alongside C34's593/71 terminal regression and9 controls.
The integrated public browser records source stages177..181, deletion182,
paused PNG [177,177], continuing PNG [297,314], final315/epoch0, with no reset
or errors. The broad-preview single-station failure remains explicitly retained.
See `.squad/.scratch/ball-trails-integrated-proof/handoff.json` in that new
worktree for exact diffs, hashes, commands, failures and PNG provenance.
This is not integrated material approval, publication, native/performance
qualification or closure of any of the16 parity rows.
