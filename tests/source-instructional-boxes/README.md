# Conditional source instructional-box program — NEW DRAFT

**Prepared, not compiled or run.** No old validation release is reusable. The
authoring outcome starts no compiler, managed harness, Game/Core, Unity, browser,
or SDK discovery. Whole parity remains **OPEN**.

## Production boundary

`GameSimulation.InvokeSourceInstructions(SourceInstructionInvocation)` admits an
explicit source program to the existing portable process host. It requires:

- a supplied, nonzero native `ALL_ABCD_MASK`, with no inferred station mapping;
- live callbacks for the opaque switch sample, `show_plysel_idiot_box`, mutable
  `tsec`, and both source-position `is_low_res` evaluations per page;
- supplied instruction, post-player, camera, and message process identifiers;
  post-player/camera must match the actual existing portable-host functions;
- a non-null `ISourceInstructionEffects` provider with valid retained plate,
  title/highlight text, and text-flasher returns. There is no default renderer,
  resource table, fake return, input provider, timing value, or forced trigger.

The normal `SourceMessageSlots` / `DispatchSourceMessage` boundary now dispatches
the operation in **both** existing non-fire and fire configurations. Constructors,
old input-recording schemas, and automatic game flow are unchanged. No automatic
initial-QB admission is claimed: the enclosing native caller switch and actual
runtime invocation have not been qualified. This is not an initial-lineup wait.

The existing `SourcePortableProcessHost`, `SourceProcessView`, and
`SourcePossessionProcessRecord` were moved unchanged into standalone source files
so the real scheduler/dispatcher can be tested without constructing a Game. These
new files must be included by source-list-based consumers; Unity's script
compilation needs no scene/asset change. Historical acceptance pins remain
immutable; the changed production files are new drafts, not accepted57.

## Source-derived contract

Authority: `historicalsource/nflblitz2000`,
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`; `Game.c` LF239–261 and
`plyrinfo.c` LF4909–5131. Pinned parent receipts are under the full nested
`metadata/source-initial-qb-lifecycle-01` path, not its invalid shadow.

The wait performs every initial `sleep(1)` without polling. Rejections each
perform a final `sleep(1)`, including the last rejection; timeout returns only
after that sleep returns. Acceptance tests the mask but returns the **entire
signed sample**, conditionally attempts the direct tick sound only on a zero
selection-box flag, and has no following helper sleep or release/repress rule.
The admitted arithmetic domain is nonnegative Int32 counts; overflowing/negative
`9*tsec` is rejected explicitly, not replaced with 100, 57, or seconds.

Instruction entry writes -1, sleeps 2, then attempts suspension and the three
ordered resumptions using the existing portable-host implementation. A shared
plate precedes rules, receivers, audibles, hints. Every page sleeps 2 before
`wait(10,9*tsec)`; the first page also attempts the plate flasher after that sleep.
Each returned wait precedes page sound, group deletion, and text-flasher kill.
Final plate deletion precedes `resume_all`, then **sleep(10) returns before
flag 2 and fallthrough**. Fallthrough parks the logical continuation; it does not
invent native process self-retirement, callback completion, or resource success.

Font/color/sound/texture/object enum and symbol identities are **not** inferred
numeric SDK identifiers. Text requests are `(page,line)` resource identities,
not copied native C or invented instructional copy. `Print` represents the
original `oprintf` call with the supplied original format and symbolic second
argument `HJUST_CENTER|VJUST_CENTER` (rules line 1 uses
`HJUST_LEFT|VJUST_CENTER`). The provider must preserve the original format/escape
evaluation rather than assume that second argument was an independent renderer
alignment parameter. Line 0 is the retained title; rules line 1 also writes
`pmsg[1]`. Exact native print-call locators:

| Page | Lines 0–6 in `plyrinfo.c` |
|---|---|
| Rules | 4943, 4951, 4954, 4957, 4960, 4963, 4966 |
| Receivers | 4997, 5005, 5007, 5010, 5012, 5015, 5017 |
| Audibles | 5044, 5052, 5054, 5057, 5059, 5062, 5064 |
| Hints | 5091, 5099, 5101, 5104, 5106, 5109, 5111 |

`SourceInstructionPosition` retains half-raster expressions and source offsets,
not resolved pixels. Plate coordinates require float half plus offset, native
integer truncation, then float storage; text coordinates retain the native
half-expression without adding that plate conversion. The consumer must resolve
the actual raster macros/types. No sprite metrics, physical sleep units, asset
availability, rights, or native Goose matching/interleaving are inferred.

## Prepared regressions

`Program.cs` compiles **real production** wait, process, dispatcher, host, and
DTOs, with explicit synthetic external inputs and recording effect services.
It never creates an uninitialized/reflection Game or executes Game/Core,
Capture, Resolve, Unity, or browser code. Eleven behavioral cases cover:

- initial delay, zero/one/multiple timeout polls and final rejected sleep;
- whole signed accepted samples, held first samples, conditional sound ordering;
- four pages, exact print slots/positions/colors/fonts, mutable per-page timing;
- real scheduler entry suspension/resumption and page-setup sleep order;
- cleanup/resume/final sleep before completion, no implicit self-kill;
- actual dispatcher timeouts, deterministic reset, sequential reentry and
  immutable snapshots, explicit admission/domain/provider failures.

`validate.py` separately labels **seven static wiring guards** (not Game
behavior). Three mutations disconnect real call sites/registry membership and
must fail those guards. Six independently compiled **production-source**
mutations must produce the expected named behavioral failure: delay polling,
masked return, skipped hints, early completion, missing final rejected sleep,
and inverted sound gate. A compiler failure or arbitrary nonzero exit does not
count as a killed mutant. Mutant copies are made only inside this test's named
validation-output after authorization; the working production sources and
historical fixtures are never mutated by the runner.

## Execution hold and smallest proposed validation

See the named `metadata/source-instruction-production-port-01/validation-proposal.json`.
Only a **new parent release** admitting the exact reference/runtime and source
pins may invoke the prepared runner from `/data/source/nflblitz`:

```
/usr/bin/python3.12 -I -S -B .squad/.scratch/source-next-default-audibles-45f/tests/source-instructional-boxes/validate.py --parent-authorized
```

The flag is an operator guard, not a grant of authorization. The runner performs
no build/restore/install or SDK query, and refuses a previously consumed
`validation-output`. Compiler/run argv, sources, reference, bounds, exits and
parent-visible result paths are explicit in the proposal. No execution result
is claimed in this draft.

## Bounded Unity Systems handoff (not implemented here)

1. Supply an admitted `ISourceInstructionInputs` from actual source values:
   opaque getter return contract, selection-box flag, live `tsec`, live low-res
   reads, actual nonzero mask, and declared process IDs. S2 provider participation
   remains held; do not infer inactive stations or hardware mappings.
2. Implement `ISourceInstructionEffects` at the existing presentation/runtime
   service boundary: resolve original text/format slots above, fonts/colors,
   raster expressions, `playresult`/texture/object symbols; retain real return
   handles and shared `pmsg` writes; consume the separate property writes,
   vertex generation, string deletion, flasher creation/kill, sounds and plate
   deletion. Callback execution/lifetime and resource failure behavior require
   their own evidence. No default/no-op provider qualifies.
3. Once a genuine native caller phase/flag producer is proved, bind **one**
   explicit `InvokeSourceInstructions` at that runtime boundary. Do not turn a
   documentary initial-QB comment into an executable lineup gate. Verify the
   actual launch/profile consumer with Game/Unity separately under new release.

This implementation is conditional local program behavior, not verified native
runtime integration, initial-QB admission, presentation fidelity, or whole100.
