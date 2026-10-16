# Actual-source contract regressions

From the repository root, with the selected .NET SDK:

```sh
python3 tests/contracts/test_runner.py
python3 tests/contracts/run.py
```

The package-free runner compiles **actual** `Contracts/**/*.cs` and the actual
pure `SourceClock.cs`, `SourceMatchRules.cs`, and `SourceMatchFlow.cs`.
`model/PureContracts.csproj` targets **.NET Standard 2.1, C# 7.3**, with warnings
as errors; the executable targets .NET 10. No Unity assembly, shadow contract,
copied gameplay implementation, or geometry stand-in is provided.

## Assertion inventory

* **177 promoted DTO assertions**, including **65 exact named enum mappings**
  against the public pure Gameplay enums (also pinned to their numeric values).
  These preserve the finalized Lead harness: detached/read-only roster,
  configuration, input, quarter-score and station collections; unspecified
  defaults; original play/page/slot/weighted-pool identity; explicit captain
  teammate offset, not team; ordered four-station simultaneous held/pressed/
  released A/B/C inputs; legacy constructor compatibility; typed result/status/
  PAT/period/kicker distinctions; finite/ID/range guards; selected-ordinal to WAN
  authoring-index conversion; source action tags.
* **23 supplemental DTO guards**: ordinal metadata/map/current/next/blend
  validation and inclusive endpoint, remaining unspecified station/period
  identities, and immediate controller identity even at zero possession ticks.
* **122 promoted source-backed synthetic rules/flow assertions**: original
  packed/local clock expectations; result points/spot/turnover/no-op semantics;
  PAT decisions; opening/quick/onside kicks; expiry/current-play/PAT deferral;
  halftime and three-overtime boundaries; CPU helper draw requests/order and
  weighted indices; defensive-TD `BeginPlay` initialization and subsequent PAT.

The first two groups are **DTO boundary evidence**. In particular, separately
supplied immediate controller identity and credit eligibility at ticks 17/18 do
not prove actual catch ownership or `CONTROL_TIME` behavior. Compiled
`IPendingSourceInput` and `ISourceActorGeometry` signatures do **not** prove focus
clearing, imported wrist geometry, or any adapter implementation. The explicitly
synthetic clip supplies only metadata and throws if conversion samples/advances.

The 122 expectations preserve the pure match-rules contributor's harness tied to
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`
(`nfl.c` blob `15b49474fb55e8bac280b6515fcf7bf59cdf8664`,
`playsel.c` blob `f5e87b921a4038d03f55174bbd5425241939e798`,
`result.c` blob `eaf1201d0531a9d06d8ce0b1e9e75463062e2923`).
They assert recorded literal results, not a second C# implementation of those
expressions. They are **not** a new original-C differential corpus. Keep using
[`../source-oracles/README.md`](../source-oracles/README.md) and
[`../parity/README.md`](../parity/README.md) for the independently executed C
pipeline. No raw originals, original art/motion, generated C or oracle corpus is
published by this suite.

## Actual offending-field mutations

The same default command builds five isolated copies of the **actual source**:
roster-array alias, quarter-score-array alias, selected ordinal leaking into
authoring index, controller identity incorrectly delayed until credit, and
removed NaN guard. Each must build successfully and fail its **named field-level
assertion**. A compile error, exception/crash, missing summary, unrelated failure,
zero checks, wrong inventory, or surviving mutant cannot pass.

The live production tree is never mutated. Baseline and final baseline must
both pass; a SHA-256 **and inventory** guard covers every compiled production
source before/after and during mutation execution. Isolated source/build trees
are deleted in `finally`. Logs, commands, exact counts, mutated-source hashes and
receipts remain locally ignored at `tests/contracts/.artifacts/result.json`.

To run just the conventional assertions while authoring (without mutation/hash
guard evidence):

```sh
dotnet run --project tests/contracts/ContractChecks.csproj --configuration Release
```

These checks do not complete full match/AI/native/browser/art/motion parity.
