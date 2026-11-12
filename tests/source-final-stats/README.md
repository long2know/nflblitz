# Source interception-count observations

This suite exercises the actual immutable public production types, not a
replacement statistics model. `SourceInterceptionsView` is co-located with the
existing match DTOs in `Assets/Scripts/Contracts/SourceMatchContracts.cs`.
The ten-contract, three-gameplay-file pure closure therefore remains **13**
production sources. Existing compilation lists and Unity script/meta ownership
need no changes.

## Observable contract

- Home/Away values are **interceptions made by that team**, not interceptions
  thrown against its offense.
- The view copies two scalar inputs into get-only properties. It retains no
  model, array, events, accumulator, classifier, or reset mechanism.
- The additive `SourceMatchView(interceptions, timeOfPossession, ...)` overload
  retains the supplied immutable view, rejects null `interceptions`, and
  delegates every old argument/default to the existing time-bearing overload.
- Both old overloads remain byte-identical. Their null `Interceptions` means
  **unobserved**, unlike a nonnull view containing observed zeroes.
- The sole actual source-mode `GameSnapshot` call constructs a fresh view from
  the existing `model.SourceInterceptions[Home/Away]` entries. It does not filter
  terminal phases. The legacy branch, existing producer, clock, terminal events,
  and replacement-state restart behavior are unchanged.

The fixed-revision source semantics and existing counter paths are documented in
the selected `contract.json`; no original source is fetched by this campaign.
The authorized implementation/execution limits are in the separate
`implementation-admission.json`. The admission supersedes the contract's
hypothetical standalone DTO file: no production `.cs` or `.meta` is added.
This is one counter projection, not a complete statistics or native-parity claim.
No overflow/clamping or original final-screen display policy is inferred.

## Literal component scenarios

`Program.cs` calls only public production DTO constructors. Its inline input
symbols are deliberately component literals, **not authenticated roster/play
assets or a naturally played game**. It checks:

1. Observed zeroes, sealed/get-only public API shape, and Home/Away split `2/5`.
2. Literal cumulative views `0/0`, `1/0`, `2/0`, `2/1`, retaining the earlier
   `1/0` instance without aliasing.
3. Actual new constructors with `LiveBall` and `MatchEnded`, both exposing
   `3/1`; every forwarded field has explicit nondefault inputs and named literal
   assertions. Equivalent old time-bearing constructors receive the same values
   and independently satisfy those literals.
4. Null interception/time parameter names, both old unobserved overloads, old
   time-reference identity, and defaults for all three constructor signatures.
5. Retained score/station/repair values after changing only caller-owned input
   arrays. No private/internal production state is accessed or mutated.

There are **13 named component cases**. The immutable pre-launch
`expectations.json` enumerates every case/field/literal and its assertion count.
Every runner assertion emits its case, field, expected value, actual value, and
comparison result; an exit status alone cannot qualify a baseline or control.
Public property metadata is inspected only to check the absence of setters.

Three isolated controls change complete copies of the actual contracts file:

| Control | Required first failing field |
| --- | --- |
| Home assignment becomes zero | `dto/team-separation.HomeInterceptionsMade`: `2`, observed `0` |
| Away assignment uses Home | `dto/team-separation.AwayInterceptionsMade`: `5`, observed `2` |
| New match assignment omitted | `match/new-overload-live.Interceptions`: supplied reference `True`, observed `False` |

Every control must first compile cleanly, then fail its declared field with the
same unchanged external runner and literal expectations. Compiler, loader,
timeout, admission, or unrelated assertion failures are not semantic kills.
The final clean baseline uses the identical clean production source identities.

## Admitted execution and evidence

Run only in the supplied worktree, with the actual outer tool shell ID recorded:

```sh
/usr/bin/python3.12 -I -S -B tests/source-final-stats/run.py \
  --outer-shell-id ACTUAL_TOOL_SHELL_ID
```

The outer process must also use an explicit clean environment with HOME/TMP/CLI
directories in a new public artifact scope. The harness gives every compiler and
managed child a recorded, explicit, attempt-local clean environment.

The runner directly invokes the pinned
`/home/long2know/.dotnet/dotnet exec` and SDK `10.0.100` Roslyn compiler DLL.
It performs no SDK discovery/query/build/restore, package installation, network
access, engine, browser, device, service, or port operations. Compiler response
files explicitly list actual sources and all **121 NS2.1 / 167 net10** pinned
references; compilation uses C# 7.3, `/noconfig`, determinism, and warnings as
errors. The external runner requires `Microsoft.NETCore.App 10.0.0` with
`rollForward: Disable`.

The planned campaign uses seven compiler invocations and five managed launches:
clean library and external runner, initial baseline, three actual-source
controls, final clean baseline, and **one compile-only 72-source compatibility
library**. The caps are 12 direct compiler invocations, five managed launches,
three harness corrections, 180 seconds per compile, and 60 seconds per managed
launch. A correction uses `--resume --correction REASON`; it appends evidence and
never refunds a reservation, overwrites an attempt, tunes expectations, or
changes the runner after a managed launch.

Before each child launch, a new numbered input manifest persists source,
test/literal fixture, tool, reference, input binary, response file, exact argv,
environment, timeout, counters, clock timestamp, and outer shell ID. Receipts
retain actual diagnostics, exits, and output identities, including failures.
Pre-edit source files/old constructor spans and the frozen23 catalog are archived
under `human-source-final-stats`. Static checks prove exactly two authorized
descendants and **21 unchanged** frozen paths, not “all23 unchanged.”

The 72-source compilation reads only the prior production path/identity list,
captures fresh identities for the two descendants, and requires all other 70
identities unchanged. It never executes that library, its historical runner,
or an existing test campaign.

## Coverage boundary

Constructor scenarios are **not actual `GameSnapshot` or gameplay execution**.
Static caller inspection and compatibility compilation are reported separately,
not as successful runtime projection or additional semantic kills.

This campaign creates **zero `GameSimulation` objects and executes zero steps**.
Actual interception production/classification, skipped wakes, terminal/idle
publication, retained real snapshots, public Restart, and legacy runtime
integration remain **UNRUN**, requiring legitimate inputs through the unchanged
roster/audio guards. A future snapshot-team-swap runtime control is also UNRUN.
Historical runtime/onside/captain/time receipts remain bound to their old inputs;
none is rerun or requalified here.

The resulting `metadata/source-final-stats/source-write-handoff.json` contains
exact changed/preserved pins, actual results and counts, command/attempt/binary
identities, failures, budgets, and these limitations. Independent Quality review
remains the sole later material gate; this harness does not approve its own work
or authorize publication.
