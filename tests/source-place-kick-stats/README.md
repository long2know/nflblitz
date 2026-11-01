# Source place-kick statistics — bounded component checks

This suite covers **field-goal attempts and made field goals**, together with the
canonical selected-play/catalog references their ending-input lifetime requires.
It does not reopen completed-pass, return, rush, sack or fire work.

## Source contract

Reference revision: `historicalsource/nflblitz2000` at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`. Original files were authenticated
in memory using GitHub size, Git blob SHA1 and raw SHA256; physical LF coordinates
retain the original CRLF identities. Original source/assets remain reference-only;
rights and whole-source completeness are not established.

| Original location | Supported effect |
| --- | --- |
| `include/GAME.H:453-456,499-504` | Separate integer counters and initial/current ownership. Neighboring declarations alone are not counter producers. |
| `playbook.c:4958-4984` | Field goal, fake field goal and free extra point are distinct objects even though their formation is shared. |
| `playsel.c:1629-1630,2631-2633,2787-2792` | Selection sentinel, actual canonical CPU pointer and actual human-selected pointer assignment. |
| `player.c:11359-11365,11489-11495` | Committed audibles replace the selected object. |
| `nfl.c:843-847,970-995` | Initial ownership, quick-kick role reversal and replacement kickoff selections. |
| `nfl.c:1603-1604` | Independent attempt increment **after the entire existing statistics chain**, only for the initial team's exact canonical selected field-goal object. |
| `nfl.c:1815-1818` | Classified field-goal result gives three points, restores initial ownership, selects kickoff and increments made field goals for that initial side. |

No `extra_points` or `block_fgs` producer was demonstrated in the acquired
relevant C files. This is not a whole-repository absence claim; neither counter
is invented here.

## Implementation boundary

`SourcePlayEndInput` retains its existing constructor/defaults/guard. An additive
overload saves the exact immutable `SelectedPlaybook` and `InitialOffensePlay`
references. The old constructor records unspecified references as null.

The direct ending-input constructor and the actual `SourceC2PendingEnd`
constructor capture both references. The existing pending-input path then
preserves them; later model selection/catalog/owner changes are not substitutes.
Kickoff identity comes from the actual selected-role object, not stale menu
descriptors. Canonical equality uses the captured catalog's stored object and
`ReferenceEquals`, never name/flags/type/formation/release/success equivalence.

The existing single `AccumulateSourcePlayEndStats` callback remains after fire
and before `LastResult`/next-state mutation. Its old chain and completion helper
are preserved; an independent attempt-then-made tail uses saved
`InitialOffenseSide` for both owners. Made goals depend on the classified result,
not on the attempt condition. Existing points/events/transitions are unchanged.

The native success counter follows native score/reset statements. Portable
`Resolve` computes those effects before the common commit, while model points
are written afterward. This suite qualifies ending **component effects**, not
literal native statement interleaving.

`SourcePlaceKickStatsView` is a sealed immutable four-count observation. Each
negative count has its own parameter guard; zero is valid. A seventh additive
`SourceMatchView` constructor forwards the exact previous35 parameters. All six
old constructor blocks/defaults remain unchanged, leaving the new group null.
Actual source snapshots copy four explicit model operands; non-source Match
remains null. Fresh model allocation supplies the existing match reset boundary;
public `Restart` is not run here.

## Frozen new evidence

The coordinator-owned contract and implementation/execution admissions are under
`.squad/.scratch/source-following-active-p1/metadata/source-place-kick-stats/`.
The contract is 65,322 bytes, SHA256
`3c21a47528ebb0aefcd60670be25abc3636464d859889ee8f801d0cefabfc212`.

The contract's literal tables, not production-derived expected arithmetic, define:

* **32 ordered cases / 603 named, unit-tagged literal fields** per clean run.
* **17** distinct actual captured-production mutations, each requiring a clean
  compile and exit2 at its exact named first mismatch/global prefix.
* **8** new Python unit tests, including **6 static** current-caller negatives.
* **20 compiler / 19 managed** launches total: clean,17 controls, one
  production-only NS2.1 compile, and a fresh final clean run.

Every semantic control runs the **same complete ordered Main** until the frozen
failure. There is no selected-case-only substitute, changed expected value,
technical-error kill, old result reuse, automatic retry or leftover budget.
Reservations, exact inputs/argv/environment, actual process starts/exits and
stdout/stderr are preserved in the new outcome's process-state root.

The new entrypoint is `SourcePlaceKickStatsChecks.Main`. The unchanged
`tests/source-offensive-stats/Program.cs` is compiled only to reuse its three
internal typed helpers; its Main/goldens/controllers are not invoked or copied.
One fixture initializes all14 genuine actors/28 layers once per managed process:
explicit synthetic one-frame/fifteen-joint data, strict unknown clip/frame/RNG
failure, zero RNG, null original hashes and false original provenance.

Only two plain synthetic play catalogs are constructed in the new harness.
Their revision/profile/source-blob strings explicitly identify
`SYNTHETIC_NON_ORIGINAL_PLACE_KICK_COMPONENT_VALUES`. Same-name objects from
different catalogs test real reference inequality. These operands do not
authenticate original data or bypass any production admission.

## Execution

Only the exact coordinator-admitted `M1/U1/D1/M2/M3` graph is permitted. The
driver refuses an unreserved/minimal-environment-mismatched launch.

```text
/usr/bin/python3.12 -I -S -B tests/source-place-kick-stats/test_runner.py -q
/usr/bin/python3.12 -I -S -B tests/source-place-kick-stats/run.py --admission .squad/.scratch/source-following-active-p1/metadata/source-place-kick-stats/execution-admission.json --continuation .squad/.scratch/source-following-active-p1/metadata/source-place-kick-stats/execution-continuation-03.json --controls
```

M1 freezes the full literal/mutation inventories and verifies seven exact
whole-file inverses, all six old constructors, the complete1461-byte completion
method, the old chain prefix and exact35-name delegation **before** compiler or
reference-body opening. The added caller inverse restores the immediately
preceding full source pin before all older four-site/completion/kickoff stages.
No old module CLI, source-list/project/props, package/build/restore/SDK query,
original retrieval, native/Editor/browser/device or external oracle is used.

Actual source closure is the explicit77-source production catalog plus the two
distinct harness paths, compiled with C#7.3/warnings-as-errors/deterministic mode
against the fixed167 net10 references. The fixed121 NS2.1 references are used for
one77-source production-library compilation only, never runtime.

## Qualification limits

Pending-constructor tests construct the real typed pending component, deliberately
change later model fields, then invoke an isolated saved-input component commit.
They do **not** execute the genuine queue/common/C2/Step/scheduler pipeline,
natural terminal-idle/exactly-once behavior, public Restart, original animation
or roster/audio admission, native ABI/statement order, Unity/browser/device or
whole-game parity. The immutable handoff precedes actual M3 completion and
enclosing-shell exit observations; future exits/hashes are never invented.

Handoff accounts for **44 current paths:11 authored descendants +33 unchanged
inherited paths**, not just the old38. Independent Quality review remains
required; this suite grants no self-approval, publication or whole-task completion.

## Preserved technical failure and explicit continuation

The first reserved M1 exited1 before preparation/literal freeze or compiler and
reference-body opening: its exact environment comparison rejected an extra
`SHLVL="1"`. The separately admitted one-shot diagnostic exited0 and found
**only** that extra key, with no missing keys or differing expected values.
Both versions, all receipts and the diagnostic remain unchanged.

`execution-continuation-01.json` explicitly permits the minimum correction:
`SHLVL="1"` is now a **required** environment dictionary entry and is explicitly
exported at the final Python launch boundary. The original strict equality and
exact underscore rule are byte-identical; no key is ignored or normalized.
Existing test08 additionally evaluates that actual equality predicate and rejects
missing/wrong SHLVL and additional keys. The suite still has8 tests/6 static
caller negatives and unchanged32/603 literals/17 semantic controls.

The original exact start UTC remains **null/unproved**. Valid observed failure
UTC is `2026-10-25T09:27:04.846421+00:00`; enclosing exit UTC is
`2026-10-25T09:27:04Z`. Local timestamps mislabeled with Z are preserved, not
rewritten or converted into an invented precise start. An additive clock
limitation record accompanies the new UTC-aware records.

The supplement grants a transparent prospective2400-second remaining-work window
from the replacement-M1 UTC reservation, not an erasure of prior time or counts.
Cumulative metadata is **5**: failedM1 + diagnostic + replacementM1 + M2 + M3.
The unit/driver and20 compiler/19 managed ceilings are unchanged; no further
diagnostic, retry or correction is authorized. New execution artifacts use
`continuation-02` namespaces, and every reservation binds both original and
supplemental authority. The unit launcher retains its full `-q` command and binds
the same supplement through its explicit reservation and driver authority checks.
No remaining-graph success is claimed until its actual observations and exits
exist.

### One gated M1 under the latest supplement

The first replacement M1 also exited1 at strict equality, before preparation or
tool/reference opening. Its actual differing key/value was not recorded and
remains **unknown**; it is not attributed to SHLVL.

`execution-continuation-02.json` authorizes one gated M1, not another standalone
probe/retry. Before any broad environment copy, a private-safe boundary record
contains actual UTC/PIDs/argv/cwd, expected/actual key names, missing/extras and
only admitted nonsecret value differences. Unknown names fail closed without
reading their values. The old equality/underscore block remains byte-identical.

Final Python launches use the newly pinned `/usr/bin/env -i` exec wrapper with
the complete explicit required vector, including `SHLVL=1`, immediately followed
by fixed Python and its exact arguments. There is no intervening Bash, inherited
export-sweep proof, environment dump or dynamic recorded-command evaluation.
Driver children remain `shell=false` with the exact dictionary.

Cumulative metadata is now **6**: original failedM1, successful diagnostic,
failed replacementM1, this gatedM1, M2 and M3. Both launcher correction admissions
are recorded separately from zero compiler/runtime correction campaigns.
Artifacts use `continuation-03`; both previous failures/versions remain immutable.
The existing prospective2400-second window is carried from
`2026-10-25T09:56:34.046876434Z`, without restart/extension. Original initial start
remains null/unproved. All nonmetadata ceilings, game literals, source guards
and qualification limits are unchanged; any new failure stops the graph.

### Explicit privacy-unit correction

The gated M1 passed. Its source/harness/guard/tool/reference/literal/mutation
capture remains immutable. The first U1 then ran all8 tests:7 passed, all6 static
caller negatives matched, and only test08's privacy-record expectation failed.
The formatter conservatively redacted invalid SHLVL2; the test incorrectly
expected that value to be persisted.

`execution-continuation-03.json` permits only that diagnostic expectation to
become `VALUE_NOT_PERSISTED/false`, preserving the bad input2, strict rejection
and privacy formatter. No C#, game literal or semantic control is changed.
One additional U1 startup appends an explicitly bound controller-input revision
under `preparation/continuation-04` and writes a new exclusive
`validation/continuation-04/unit-result.json`. The failed03 result remains
unchanged; the driver requires the new exact receipt, never whichever passes.

Cumulative limits remain6 metadata and now2 unit attempts, with the same8 tests/
6 static negatives per unit run. Driver1/compiler20/managed19 and the carried
09:56:34 prospective window are unchanged. No extra M1/diagnostic or automatic
retry is authorized. Subsequent success must come from actual new observations.
