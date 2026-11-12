# Source-owned kickoff tutorial child

[Program.cs](Program.cs) covers the authored child through the real portable
dispatcher/host, with explicit helper/effect bindings. The recorded current-source
regression passes **18 cases / 3,825 assertions**; this is component evidence,
not executed Game, Unity, browser or native-runtime qualification.

## Explicit integration

[`SourceKickoffInstructionCoordinator.WithSourceChild`][coordinator] selects the
authored child using a [`SourceKickoffInstructionChildBinding`][bindings].
For the authored wait and flash bodies, use
[`GameSimulation.BindSourceKickoffInstructionOwnedHelpers`][game-binding];
see the [owned-helper bindings and coverage](../source-kickoff-owned-helpers/README.md).
Bind once on a fresh initialized source-mode simulation before its first dispatch.
The invocation's `Effects` and admission `State` must be the same coherent owner.

The caller must explicitly supply the coordinator PID, maximum-player and parent
A/B operands, live input/button services, `SourceKickoffInstructionChildOperands`
(`DbgKoDefined`, `BarX1`, `BarX2`, `XComponentIndex`, `YComponentIndex`), child
effects and child return policy. Resource, font, color, text-format and sound
symbols require real bindings; no resource values or native retirement policy
are inferred. `DbgKoDefined` represents the selected compile-time alternative,
not a newly discovered runtime switch.

The existing child-binding constructor still accepts external wait/flash
factories. Default/unbound, manual, external-child and whole-suffix paths remain
available. To leave the new path disabled, retain those bindings on a fresh
simulation; admission is not a hot-rebind toggle.

## Child behavior and coverage

The [factual child contract][child-contract] cites
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`,
`plyrinfo.c:4516–4684` (body `4540–4684`). It records:

- Compile-choice return, coordinate reads before live participation/experience
  guards, and distinct native `die(0)` versus source-function return.
- Deferred flash creation with PID0 and arguments `(slot, 8, 22, 0)`, ordered
  plate/header setup, carrier precedence and fresh offense control reads.
- Coordinate/header rows, branch-specific text placement and per-position live
  low-resolution reads; these do **not** qualify all 16 source-version/slot cases.
- Wait invocation `(40, 6 * live tsec, original slot)`, then sound, full-mask
  string/plate-ID cleanup and explicit source-return handling.

The 18 cases additionally check shared allocation/lifetimes, any matching live
parent-wait PID, fail-closed non-replay, portable index/time constraints and
legacy compatibility without gameplay writes. Their helper factories test the
child's boundaries; the separate **12-case / 908-assertion** suite qualifies the
new owned wait/flash scenarios.

## Recorded evidence, not a reusable execution grant

- [Current-source preservation result][regression]: 18 child cases and 16 older
  coordinator cases passed; the current 89-unit Core library compiled cleanly
  and was **never executed**.
- [Earlier child baseline][prior-baseline]: historical 18/3,825 result on older
  production pins. The earlier 29 compiled semantic controls remain historical, not
  new-helper coverage.
- [Current Quality review][quality]: **APPROVE**, zero material findings,
  conditional component scope only.

The [minimal current helper input metadata][inputs] and captured outputs describe
consumed one-shot generations, not commands to rerun blindly. See the
[replay boundary](../source-kickoff-owned-helpers/README.md#evidence-and-replay-boundary).
The Game opt-in is authored/typechecked, not executed. Genuine primitive/effect
providers, native ABI/scheduling/return behavior, Unity/browser rendering,
resources/assets/rights, all 16 and whole-game fidelity remain **OPEN**.

[coordinator]: ../../Assets/Scripts/Contracts/SourceInstructionContracts.cs
[bindings]: ../../Assets/Scripts/Contracts/SourceKickoffInstructionChildContracts.cs
[game-binding]: ../../Assets/Scripts/Gameplay/GameSimulation.Instructions.cs
[child-contract]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-child-source-01/native-child-contract.json
[regression]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/regression-output-01/result.json
[prior-baseline]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-child-source-01/baseline-output-02/result.json
[quality]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/quality-review.json
[inputs]: ../../.squad/.scratch/source-following-active-p1/metadata/source-kickoff-owned-helpers-01/baseline-inputs.json
