# Original tutorial caller admission — connected NEW DRAFT

**Authored, not compiled or executed.** The actual production selection/catch
hooks, caller-aware dispatcher and deferred producer-entry write are implemented.
`Program.cs` and `validate.py` are new prepared regressions, not passing evidence.
`validation-output-implementation-01/` is absent/unused. Earlier component/Core
releases and the immutable predecessor handoff cannot be reused.

## What the new source observation actually establishes

Authority: `historicalsource/nflblitz2000`,
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`.
The immutable caller record is
`../../.squad/.scratch/source-following-active-p1/metadata/source-instruction-original-admission-01/native-caller-contract.json`.
The successor `implementation-01/native-producer-contract.json` records the
fresh extension and exact new producer observation.

- `nfl.c` LF410–420 initializes `idiot_boxes_shown` to **0 when `p_status` is
  nonzero, otherwise 2**. Experience is not this initialization predicate.
- `player.c` LF3659–3665 attempts the kickoff box process on a kickoff catch
  while the flag is zero. That callsite does not set the flag to 1.
- `nfl.c` LF871–876, LF904–906 and LF1015–1035 identify the real instructions
  caller: the **default play-type arm**, after `play_select()` returns and after
  fourth-down accounting, before player reinitialization. PAT and kickoff have
  separate switch arms.
- The caller first requires flag **1**, then tests slots 0–3 in order. A term
  requires `games_played` **strictly below 3** and that slot's `p_status` bit
  (1, 2, 4, 8). Experience precedes the bit read within each term; evaluation
  short-circuits. These are not inferred `ALL_ABCD_MASK` values.
- The immutable earlier entrypoint contract supplies the subsequent **-1 at
  instruction entry**, and **2 only after the final sleep returns**. Creation
  itself does not reserve the flag. This is not an unconditional once boolean
  or a new first-QB lineup wait.

The predecessor's three GETs remain consumed. A **separately authorized** fresh
extension GET authenticated `plyrinfo.c` again and emitted exactly LF4460–4490:
**producer entry unconditionally writes 1 at LF4473**, before the two child
creation attempts, conditional four-player children, suspension/resumption and
existence/sleep loop. There is no catch-side reservation. Only that entry write
is implemented locally; the required opaque continuation owns the entire suffix
from LF4475, including those observed operations and the unobserved remainder.
No full callback, child behavior, global interleaving or suffix return is proved.
No original/base64/excerpt was persisted.

## Actual connected production API

`GameSimulation.Source.cs:FinishSourcePlaySelection` is the real selection-return
surface. Both non-fire and fire scheduling call it inside an entered
`GameProcess` wake. `SourcePortableProcessHost.Visit` keeps `Caller` nonzero until
`CompleteSourceProcess`.

The extension released the dispatcher's admission surface. `AdmitFromProcess`
and the source-ordered `AdmitAtPlaySelectionReturn` reuse its real binding,
resource/fault, PID and host-serial validation without clearing `Caller`.
Manual `Admit` still requires idle context. Creation does not execute the child.

`GameSimulation.BindSourceInstructionAdmission(binding)` is explicit and must
precede the first source dispatch or manual admission. Constructors do not bind
it. The binding requires:

- the existing complete `SourceInstructionInvocation`, including its explicit
  input/effect, nonzero mask and process-ID providers;
- `ISourceInstructionAdmissionState` implemented by the **same object** as that
  invocation's effects, with coherent flag read/write, live `p_status` and
  per-slot experience. No station/configuration-to-status mapping is inferred;
- a non-null `ISourceKickoffInstructionContinuationFactory`, returning an actual
  cooperative suffix continuation. It must provide the observed operations from
  LF4475 and its independently admitted remainder; there is no no-op/default
  implementation or invented four-player setting, ID, input or timebase.

Binding initializes the flag to 0/2 from live participation. Restart creates a
fresh dispatcher and repeats initialization on the retained explicit binding;
selection does not reset/re-arm it. Automatic errors mark the actual Game's
existing failure field, requiring Restart. Dispatcher provider/resource faults
and reentry are fail-closed rather than successful progress.

The real selection hook captures the entering play type **before** play-type
mutation, and creates after fourth-down accounting, before lineup. The real
kickoff catch hooks creation with the executing actor process serial. Producer
entry on the normal dispatch path then writes 1 before constructing/advancing
the required external suffix. A suffix-reported return parks only that external
continuation: it does not synthesize flag2, resume-all or self-retirement.
The shared message predicate connects both instruction and producer to fire and
non-fire scheduling. Unbound helpers return immediately, preserving old flow.

## Prepared direct regressions

`Program.cs` uses the **actual production host, dispatcher, wait and instruction
process**, explicit synthetic inputs and recording boundary services. It never
constructs a fake/uninitialized/reflection Game or models a replacement policy.
Twelve named component cases cover:

| Case | Required real-production observation |
|---|---|
| Active caller | The real host retains `Caller == GameProcess` while the new API creates the instruction entry; the entry has not executed inline. The old manual API still requires idle context. |
| Flag short circuit | Flags 0, -1 and 2 cause no experience/status evaluation or creation; this is not a once-counter substitute. |
| Experience boundary | An active slot with 2 games is eligible; 3 is not. Inactive inexperience alone is insufficient. |
| Slot order/live status | All four exact bits, games-before-status and OR short circuit, plus distinct live status returns at each evaluated term. |
| Deferred producer | Catch creates a real pending producer, with no reservation; entry writes1 before the explicitly synthetic opaque suffix. Return is not fabricated native completion. |
| End-to-end component | Actual producer entry1 -> eligible selection creation -> instruction entry-1 -> four real pages/waits -> final delay ->2. |
| Initialization/reset | Explicit nonzero/zero `p_status` produces0/2 on a fresh dispatcher with the same flag owner, never an implicit admission. |
| Existing API restrictions | Exact post-player/camera bindings, shared pending resources, PID collisions and real host serial validation remain enforced. These are API compatibility cases, not newly discovered gameplay defects. |
| Provider faults | Failed live experience/effect reads cannot become admission/completion or replayed success. |
| Producer faults | Suffix failure remains faulted and protects later shared admission. |
| Reentry | A provider cannot recursively admit a hidden child during the guard's ordered reads. |
| Required coherent inputs | Distinct flag owners, a missing suffix or an invented default sleep are rejected. |

Seven separately compiled mutations target the real dispatcher: active caller,
flag predicate, strict experience boundary, participation bit, producer-entry
value, catch reservation, and initialization value. Each executes one explicit
named case and must fail its intended assertion, not compilation or a crash.

The runner separately authors **nine STATIC guards and five STATIC production
mutants**: lost selection hook, after-lineup hook, lost producer registration,
lost restart reset and lost no-binding guard. These inspect the actual Game
files and preserve the entering-type/source-order/caller wiring requirements;
they are **not Game execution**. The full actual Core87 library is proposed for
C#7.3 warnings-as-errors typechecking only and is never executed.

## Validation remains held

`implementation-01/validation-proposal.json` names exact current source/reference/
tool proposals, fresh output footprint and bounded argv. Only a **new parent
execution release** may start the prepared runner from the worktree:

```
/usr/bin/python3.12 -I -S -B tests/source-instructional-admission/validate.py --parent-authorized
```

The flag alone is not authorization. The runner requires the fresh
`implementation-01/parent-execution-release.json` with the exact proposal,
reference, genuine tool identities/runtime and bounds; refuses a consumed
footprint; and performs no discovery, project import, restore or installation.
No such release or execution result exists in this authoring handoff.

The whole kickoff callback/suffix and genuine Game/Unity/provider execution,
native scheduling/ABI, resources, Q1, assets/rights, all sixteen acceptance rows
and whole100 remain **OPEN**. This is a connected conditional production draft,
not full native callback or runtime fidelity acceptance.
