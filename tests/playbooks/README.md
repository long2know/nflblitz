# P1 original-source regressions

Permanent promotion of the frozen playbook/drone/random harness. Requirements:
Python 3.10+, GCC, and .NET SDK 10 with the .NET Standard 2.1 reference pack.
No Python packages, historical makefiles, Unity Editor, browser or service needed.

From a clean checkout:

```sh
# Optional network acquisition: only 14 pinned public blobs, decoded-byte hashes checked.
python3 tests/playbooks/fetch_inputs.py
python3 tests/playbooks/run.py
```

The fetcher uses `gh api` for metadata-pinned Git blob identities from
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`.
CI needs GitHub API network access and the usual `gh` authentication/rate budget.
It never clones/runs the historical game, fetches credentials, or overwrites a
wrong cache. `--cache path/to/canonical/archive` can be repeated to reuse read-only
verified inputs. Alternatively run entirely offline:

```sh
python3 tests/playbooks/run.py --source-root path/to/exact/pinned/archive
```

Canonical, case-sensitive paths and hashes are in `pins.json`. Missing, changed,
newline-normalized or escaping inputs fail; there is no implicit network or
scratch-path fallback. The runner is the recommended CI step, **after** the
explicit fetch step. Production/source-profile changes require deliberate pin
and matrix updates, not relaxed gates.

## What is proved

The runner snapshots all ten current actual Contracts sources (including
the existing actor/ball presentation and contact contracts plus
`SourceFireSpfxContracts.cs`, `SourceKickMeterContracts.cs` and
`SourceCameraContracts.cs`), the pure
SourceClock/MatchRules/MatchFlow dependencies, and the final three P1 helpers.
It compiles them as **C# 7.3 / netstandard2.1, warnings-as-errors**, then executes
a net10.0 comparison harness. A test-only friend-assembly attribute exposes the
internal fixture constructors and synthetic timer-state inputs; no production
logic or visibility is edited. Hot full-kernel/M1/presentation files are excluded.
Live production/producer inventory and hashes must remain unchanged throughout.
The model includes `Contracts/**/*.cs`; its dependency inventory guard requires
the complete named ten-file source set, without compiling presentation or the kernel.
Missing, extra or same-count renamed Contracts fail before compilation with the
offending missing/unexpected filenames. All ten actual files remain in the
snapshot/hash inventory and the model's recursive Contracts compile include;
no DTO substitute, omitted file or stale assembly is used.

* Execute the actual permanent producer and its eleven synthetic tests in an
  isolated byte-identical copy. Validate its actual metadata schema, pinned input
  provenance, catalog digest, full inventories and byte-for-byte reproducibility.
* Compile **53 selected original C functions** with normal 57 Hz /
  `USE_OLD_QB=FALSE` branches. The extractor removes includes and uses GCC's
  preprocessor; it does not rewrite decision expressions. Source/blob, selected
  function and translation-unit hashes, compiler flags and boundary metadata are
  recorded. Original functions/tables are fetched/extracted at runtime, never
  copied into these permanent fixtures.
* Compare 7,565 compiler-evaluated catalog records: all 88 plays, 29 formations,
  140 route programs, 13 CPU families (226 weighted entries **with duplicate
  ordinals**, plus six singletons), 257 quarter-sines, 49 directions and 34
  twelve-level handicap tables. Preserve formation scripts/roles/control masks,
  play references/flags, exact route words and CPU selection identity.
* Compare **21,141,803 assertions**: 20,000 numeric cases, 8,640 lineup inputs,
  1,200 receiver frames, 1,200 blocker frames, 300 full new-play initializations
  and **220,208 original-C decision ticks**. The frozen 219,776 ticks are retained;
  432 added switch-timing ticks cross side/stage/think-time/stick-only and the
  30/31-tick smoothing boundary. All 140 actual route programs run for 750 ticks
  on both sides. QB/DB/line/carrier, snap/captain/kicker stages and defined
  receiver-ball branches reuse the frozen harness matrices.
* Distinguish retained actor controls from newly written `CommandWritten` /
  `ButtonsWritten`, check switch timers/smoothing and ordered cross-defender
  effects, and compare RNG kind/bound/raw/value/global order plus caller-specified
  actor/process/tick/within-process order. Deterministic synthetic **input tapes**
  are passed explicitly to C; neither target nor host libc PRNG is substituted.
  Counts are exact gates: 19,779 wrapper calls, 192 retained stick commands,
  712 retained button commands and 493 cross-actor effects.
* Reject three real regressions in isolated copies: replace only ShortOffense
  ordinal 27 while retaining pool length, corrupt an actual route opcode to 999,
  and weaken the production `FindReceiver` five-yard `<=` exclusion to `<`.
  Build errors/crashes/unrelated assertions do not count as mutation kills.
  Re-run the complete unchanged baseline afterwards.

Fourteen small runner tests verify source/inventory/manifest/mutation/failure
gates; they are explicitly not gameplay evidence. There are no skips or
status-only success fallbacks. `.artifacts/result.json` contains the receipts,
counts, commands, timings and unchanged LiveSources hashes. Logs, fetched inputs
and converted catalogs remain ignored. Isolated copies/build/native artifacts
are removed after the run. The local ignore rules explicitly unignore both
permanent `.csproj` files from Unity's repository-wide generated-project rule;
runner tests verify they are publishable in the actual checkout.

## Explicit limits

“Native C” means **host-compiled functional oracle**, not Unity native, the
original MIPS binary or a full match. Actor trajectories, animation feedback,
service state and raw tapes are independent deterministic synthetic inputs to
unchanged original functions, not original physics/animation/scheduler traces.
The cached-geometry input/copy contract and explicit RNG-unavailable/negative/
zero-bound guards are synthetic seam tests, identified as such in the harness.

Target libc PRNG bodies, equal-key target `qsort` ordering, real scheduler/process
ordering, audible-side initialization and original undefined C branches remain
unverified. Equal-time blocker ordering is qualified, never treated as a parity
pass. Snap audibles are supplied explicitly. No original art/motion/assets,
full-game equivalence, P1 integration completion, or full-parity closure is
claimed by this necessary regression coverage.

## Cabinet saved and known-none custom selection

The separate bounded managed checks use an **existing approved pre-snap-switch563**
data root, without preparing assets or promoting it to the default-audibles profile:

```sh
tools/source-player/dotnet.sh build tests/playbooks/model/CustomSelectionProduction.csproj --no-restore -c Release
tools/source-player/dotnet.sh run --project tests/playbooks/CustomSelectionChecks.csproj --no-restore -c Release -- /path/to/approved563/data
```

Restore the selected project only if its assets file is missing. Production is
also compiled as C#7.3/netstandard2.1. The checks exercise the actual quantizer,
input buffer, game_proc, timer, shared RNG and source selection using authenticated
HUD/catalog/M1 inputs. A declared post-result entry and injected custom4 highlight
cover the otherwise unreachable timeout branch; known-none ordinary page cycling
skips custom4. These are not browser, native or whole-match acceptance.

Only deliberately new-player sessions may call
`game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(configuration), data.Hud)`
before the first wake, then construct
`new SourceCabinetPlaySelection(data.Hud, customPlaySelection: game)`.
The session must retain the same actual FourStation configuration and Portable
pre-snap-switch composition. Omitted/unavailable inventory is not empty. A missing manual custom
choice produces no selection or RNG; a valid forced known-none choice consumes
one `PlaySel__MakeChoice:2847` draw and resolves the actual current first-page
team/FG/punt/PAT slot without changing the displayed page or cursor. Custom
audibles do not acquire a timeout fallback.

For actually loaded records, bind
`new SourceCustomPlaySession(actualSavedRecordSession)` instead. Core converts
the source grid through `SourceCustomPlayCodec` into private per-Game plays and
routes. Typed `SourcePlaySelection.SavedOrigin` retains station/slot/session
identity without adding strings to the authenticated static catalog. Saved
captain page4, requested/first-existing forced selection, actual live receiver
routes, signed audibles and original saved-play/flip restoration are exercised.
Zero audible rows retain the real `select.c:960` defaults. The separate audible
factory authenticates its own catalog input; the base profile is not promoted.
The read-only `SourceCabinetPlaySelection.CustomPlay` supplies the actual
captain's grid for custom-card rendering, never a stock-play replacement.

Bindings are immutable and survive administrative reset. Changed records require
a fresh Game/session. `BindSourceRecordCommitBarrier` is optional and requires
the actual saved session and cabinet-flow bindings before wake1. At the original
full-game save guard, Core captures `SourceFinalRecordCommit` before final stats;
Pending/Failed sleeps the real game process, Committed/explicit Declined resumes
once, and reset cannot bypass the wait. Runtime owns IO, durable match IDs and
retry. Partial declined games retain their actual period count and never acquire
a fabricated four-period save. These are bounded source-process fixtures, not a
claim of browser persistence, natural full-match or native equivalence.
