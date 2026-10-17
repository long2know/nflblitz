# Original actor sound selection — separate from PCM and playback

This layer selects the calls made by the **two unchanged original routines**
`get_hit_snd` and `grnd_hit_snd` at the current **six verified VM sites**.
It neither edits nor invokes the initial decoder/reader/isolated player.
Original weighted numeric tables, constants, source declarations and generated
data stay **ignored**. `function-inputs.json` pins every public input blob;
`sound-functions.schema.json` describes the ignored audit manifest.

## Reproduce

No Unity installation or original assets are required for the default SDK tests:

```sh
python3 tests/audio/functions/run.py
```

This compiles actual `SourceRandom.cs`, actual source-match contracts/support
and the new pure code. Its guard declaration is extracted **unchanged** from
actual `SourcePlaybook.cs`, not replaced with a fake API. The default source
root is the current checkout; an explicit `--core-source-root` may select another
stable source checkout. This SDK-only invocation reports **33 synthetic checks,
zero original-C cases**; it is not the original oracle.

For a clean checkout, explicitly acquire **only the four original pins**, then
run the actual C oracle and mutations. All destinations below are relative to
the current checkout; no private cache, frozen snapshot or Editor path is needed:

```sh
python3 -m unittest discover -s tests/audio/functions -p test_fetch_inputs.py
python3 tools/audio/fetch_function_inputs.py --download \
  --destination tools/audio/.artifacts/source/nflblitz2000
python3 tools/audio/prepare_functions.py --name original
python3 tests/audio/functions/run.py \
  --original tools/audio/.artifacts/functions/original \
  --reproduce original-repeat --mutate
```

The original invocation must reproduce **15,912 C cases, 267,367 checks,
4,941 lazy-field checks and 14 compiled/rejected mutants**, not merely the SDK
proxy. Its ignored receipt is `tests/audio/functions/.artifacts/result.json`.
Use fresh producer/reproduction names. The helper reports the explicit destination,
canonical repository/commit/tree and each input's exact Git blob/SHA256/byte count.
It addresses the pinned commit directly and validates the frozen
`function-inputs.json` identity; it never follows a branch/ref or repairs bad bytes.

Instead of network acquisition, select one explicit read-only cache:

```sh
python3 tools/audio/fetch_function_inputs.py \
  --source-root /explicit/verified-original-cache \
  --destination tools/audio/.artifacts/source/nflblitz2000
```

Neither mode is automatic. Wrong repository/ref, wrong case, traversal, symlinks,
partial/invalid caches and inconsistent bytes fail without cache/network fallback
or overwriting originals. All four inputs are verified before an atomic directory
publication; an existing complete valid destination is reverified and left untouched.
The helper reuses the parity/playbook fetchers' canonical blob-verification pattern,
without their broader input/tree unions or implicit network/cache selection.
All inputs, acquisition receipts, generated original C and payloads remain ignored
under this checkout's `.artifacts` directories. A custom cache can be verified,
but the recipe's canonical destination is the producer/runner's shared default.

This specialized extractor verifies the exact
Git blobs before parsing; it does not run an upstream script or evaluate C
expressions. It extracts all seven weighted tables used by these two routines,
all seventeen callee call sites, branch assignments, mode/visibility/station
constants and actual six caller argument tuples. Duplicates are not deduplicated.
The bank note is extracted from source, while selection separately requires the
actual current `plyr_bnk_str` when the routine emits a call.

`--unity-editor-data /explicit/Editor/Data` optionally compiles against that
installation's actual netstandard2.1 references, C#7.3, editor/WebGL defines.
There is **no default Editor path**, Unity process, native build or browser launch.

## Synchronous API

Load `SourceAudioFunctionData.Read(payloadBytes, frozenPayloadSha256)` once.
At the actual `ANI_CODE` invocation, construct `SourceAudioFunctionState` from
the actual actor flags, **raw plyrmode**, player number and station; current
carrier and intended receiver; actual bank string; and `SourceAudioHitPartner`.
Nullable scalar fields mean **unknown**, not an original zero. A default partner
means **unknown presence**; `Absent` means an actually observed null pointer;
`Present(mode,station)` can carry independently unknown fields.

Then call:

```csharp
SourceAudioFunctionResult result = SourceAudioFunctions.Select(
    data, actualCue, actualState, sameCoreRandom, actualRandomContext);
```

This must execute **inside the actor/VM process before the next source opcode
or actor**, not through post-Step `SourcePlayback.SourceCallback`. Pass the
existing `ISourceRandom` instance and `SourceRandomContext` with its actual
process/order/profile and current `FirstCallIndex`. The implementation uses
the existing `SourceRandomCursor`, never a new seed, camera RNG, or algorithm.
Update Core's process draw accounting by `result.RandomCalls`. Cue total-opcode
order and RNG process order are distinct values and are both retained.

The result contains immutable actual call envelopes: original cue/context,
original callee source site/blob, subcall order, actual bank, playlist ID,
full volume/pan/priority word, cumulative random calls at emission, and actual
`SourceRandomDraw` trace/provenance. No DSP parameter is mapped to Unity gain.
After selection, these already-resolved envelopes can be queued for delivery.

Inputs are requested only when the original C branch reads them. Offscreen
get-hit returns before mode/partner/ball/bank/RNG reads. Carrier status
short-circuits intended receiver. A human ground-hit actor short-circuits
partner presence/station; ground-hit never reads visibility or carrier state.
A lineman's extra sound does **not** replace the later hit/fly calls.
Needed unknown state fails visibly. `SourceAudioMissingInputException` retains
the exact partial calls/draws already performed; do not retry against an
already-advanced stream. An actual null lineman hit pointer fails explicitly
instead of inventing a partner mode. `PlayerDownCallback` is never sound.

## Proof and remaining dependencies

The opt-in oracle compiles **unchanged pinned C function bodies**, original
extracted declarations/macros, and recorded raw draws. It compares ordered
sound calls, raw/reduced draws, bounds/counts, callee lines, parameter words and
unchanged source state/flags. The same stream is tested before/inside/after
selection. Unknown-field deletion tests exercise short-circuit requirements.
Source mutants must **compile and fail named contract assertions**, not merely
fail compilation.

This is source-routine equivalence given an explicit raw stream, not proof of
the target `/video/lib/libc.a` PRNG implementation or whole-game ordering.
Core's existing unavailable/unverified RNG provenance remains truthful.
The accountable Unity owner must install the synchronous VM hook; this task
does not edit Core, Contracts, Runtime, Team/UI, resources or metadata.

DSP3 playback remains separate. The pinned Goose host driver sends a bank-
relocated call, volume/pan word and priority word; that is not gain/pan/voice
arbitration semantics. Its public source also lists OS/compression/program/data
firmware image sets: the binaries are **not asserted missing**. Required next
evidence is a verified match between the game's target board/loader/version
and the relevant complete firmware images or authoritative DSP implementation,
then independent execution/measurement of the three-word call protocol,
playlist controls, allocation/preemption, gain/pan/mixing and SPFX. The OS95
isolated PCM decoder is not this evidence, and its isolated Unity preview must
not be installed as the game consumer. No audible/native/browser parity is claimed.

## SAFN1 binary schema

Little-endian integers; UInt16-length bounded ASCII strings; exact EOF required.
Header: `SAFN`, version byte 1 and three zero bytes; four raw 20-byte original
blob IDs in sounds/header/player-header/VM order; bank string; Int32 visibility
mask, line mode, drone station; human and drone triplets of UInt32 volume,
primary and secondary priority.

Then UInt16 table count: string symbol, UInt32 original line, UInt16 value count,
ordered Int32 IDs. UInt16 site count: role/function strings, UInt32 line, table
string, Int32 argument index, Int32 volume mode, UInt32 volume/pan, Int32 priority
mode, UInt32 priority. Table references use argument index -1; fixed arguments
use index 0..2. Volume mode 0=fixed,1=branch; priority mode 0=fixed,1=primary,
2=secondary. Finally UInt16 binding count: path/script/function strings, UInt32
line and three signed Int32 original arguments. The reader verifies limits,
hash, version, role completeness/uniqueness, references and strict framing.
