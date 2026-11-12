# Source-owned kickoff wait and flash helpers

[Program.cs](Program.cs) exercises the real portable host/dispatcher/child with
synthetic primitive/effect bindings. The recorded baseline passes
**12 named cases / 908 assertions**, with clean compilation, managed exit 0,
empty stderr and stable input pins. No Game, Unity or native provider was started.

## Real opt-in and mandatory bindings

Call [`GameSimulation.BindSourceKickoffInstructionOwnedHelpers`][game-binding]
once on a fresh initialized source-mode simulation before first dispatch. Supply
the invocation and coherent admission state (`invocation.Effects` must be that
same object), explicit coordinator PID/maximum-player/parent A/B operands, live
child inputs/button services, child operands/effects and child return binding.
See the [child usage](../source-kickoff-instruction-child/README.md).

[`SourceKickoffInstructionOwnedHelpers`][bindings] additionally requires:

- Nonzero `Player1ButtonAMask`, `Player1ButtonBMask`, `Player1ButtonCMask` and
  `FlashWhiteMask`. These numeric bindings are not supplied by the port; parent
  A/B operands do not establish the wait's P1 A/B/C definitions.
- `ISourceKickoffCurrentSwitches`: genuine live current-switch-word sampling,
  not edge/new-button state or a control-index remapping.
- `ISourceKickoffPlayerObjects`: the original player-block object's identity and
  live flag reads/writes, not a control lookup or saved flag word.
- `ISourceKickoffInstructionFlashReturnBinding`, plus the separate child return
  binding: explicit `KeepLive` or `ExternallyRetire` policy. Source-function end
  is not evidence of native process retirement.

The method composes `WithOwnedHelpers` / `WithSourceChild` with existing admission.
The dispatcher builds factories using its **actual host and admission state**;
child/flash wakes stay deferred. New callers do not supply whole wait/flash
bodies, a host or an existence-query substitute.

## Owned ordering

The [active-player wait contract][wait-contract] cites `attract.c:2054–2067`;
the [flash contract][flash-contract] cites only `plyrinfo.c:4804–4822`, both at
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`.
The later active-wait contract supersedes the earlier flash report's
wait-not-located observation.

Wait entry sleeps 1 before sampling. Resumption increments ticks, then checks
live original-slot participation, the current word's player byte and explicit
P1 A/B/C masks, with minimum last. Timeout follows polling, including the final
tick. The child supplies minimum 40 and maximum `6 * live tsec`.

Flash captures the original player object once on its deferred wake. Every
cycle performs live OR-white/sleep8, live clear-white/sleep8, then the actual
host's ID22/full-mask existence query. Any live match, including foreign or
suspended processes, repeats. The first cycle is unconditional; unrelated live
flags survive, with no saved-word restoration, extra clear or implicit death.

UInt32 switch/flag words, original slots0–3, nonnegative Int32 wait operands and
positive 15-bit sleeps are portable constraints, not authenticated native ABI
or numeric symbol definitions.

## Actual new coverage

| Named case | Contract checked |
|---|---|
| `wait-first-sleep-zero-maximum` | Sleep before any zero-timeout sample. |
| `wait-held-current-minimum-last` | Held A/B/C; polling before minimum. |
| `wait-minimum-after-timeout-final-sample` | Final poll; timeout can precede minimum. |
| `wait-live-status-original-player-byte` | Live participation; original-slot byte. |
| `flash-deferred-first-full-cycle-delay8` | Deferred entry; two 8-tick phases before query. |
| `flash-captured-object-live-flags` | Captured identity; repeated live flag preservation. |
| `flash-any-id22-full-mask-suspended` | Any matching PID; foreign/suspended/full-mask behavior. |
| `flash-explicit-return-policy` | Source end versus declared lifecycle action. |
| `child-owned-wait-suffix-and-flash` | Owned wait, deferred flash and ordered child suffix. |
| `legacy-bound-path-compatibility` | Existing external helper factories still execute. |
| `mandatory-bindings-and-portable-domains` | Required bindings and declared domains. |
| `failed-continuations-poison-and-do-not-replay` | Fault propagation and non-replay. |

## Evidence and replay boundary

- [Minimal baseline inputs][inputs]: nine complete current source/test units,
  explicit Main, source/tool/reference pins and capture contract.
- [Baseline result][baseline] and [compiler][compile-process]/[managed][run-process]
  process metadata: one clean compile and one successful managed component run.
- [Current-source regressions][regression]: three clean compiles and two managed
  exit 0 results; child18/3,825 and coordinator16 passed. The 89-unit Core library,
  including the Game binding, compiled but was **never executed**.
- [Quality review][quality]: **APPROVE**, zero material findings, conditional
  component qualification. The older 29 compiled semantic controls concern older
  pins; they were not rerun or credited as new-helper coverage.

These are consumed single-use outputs, not copy/paste runner commands. Fresh
parent admission, current pins and an absent single-use output are required
before another execution; this documentation grants none. The parent did **not**
run the sealed historical all-phases `validate.py`; it may reject current helper
pins by design and is not a current passing runner. Recorded C#7.3/NetStandard2.1
compilation and .NET 10.0.0 component execution do not qualify Unity.

## Compatibility and remaining boundary

Rollback means retaining the existing unbound/manual, external-child,
external-helper-factory or whole-suffix path on a fresh simulation, not rebinding
an active one. No automatic opt-in was added. The frozen 9,470-byte original
instruction-body suffix and existing GUIDs remain unchanged.

The Game opt-in is authored/typechecked, not constructed or executed here.
Authentic switch/object/flag/mask, resource/font/format/sound and native-return
bindings, native SDK/ABI/scheduling, genuine Game/Unity/browser behavior,
assets/rights, all 16 and whole-game fidelity remain **OPEN**. Public source
access is not asset permission.

[game-binding]: ../../Assets/Scripts/Gameplay/GameSimulation.Instructions.cs
[bindings]: ../../Assets/Scripts/Contracts/SourceKickoffInstructionChildContracts.cs
[wait-contract]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-native-helpers-01/active-wait-source-02/native-active-player-wait-contract.json
[flash-contract]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-native-helpers-01/public-acquisition-01/native-helper-contracts.json
[inputs]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/baseline-inputs.json
[baseline]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/baseline-output-01/result.json
[compile-process]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/baseline-output-01/compile.process.json
[run-process]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/baseline-output-01/baseline.process.json
[regression]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/regression-output-01/result.json
[quality]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/quality-review.json
