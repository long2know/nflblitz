# Public GameSimulation source-runtime integration boundary

This component compiles **the actual production model as a separate library**
and calls the real, public `GameSimulation` constructor from an external test
assembly. It does not use reflection, internal setters, extracted algorithms,
an alternate simulation, or private/original asset fixtures.

## Current result and exact limit

The existing `OriginalRelease57Hz` / `StrictSource` constructor does **not** admit
an explicitly synthetic roster through `ISourceTeamRoster`. Both targeted
configurations reach this actual production call chain:

```text
GameSimulation(source inputs) -> Restart -> InitializeSource
  -> PrepareSourceKickoff -> LineUpSourceActors -> UpdateSourceHandicaps
```

`GameSimulation.FireHandicap.cs:UpdateSourceHandicaps` first casts the supplied
roster to the **sealed** `SourceTeamRosterLibrary`. It throws if the cast fails,
before even the human-vs-human branch. Both tested layouts have humans on both
teams. Changing station layout or choosing the base source profile does not
remove that dependency.

The concrete library's constructor is private. Its public `Read` method requires
fixed, verified original provenance and a fixed twenty-two-input metadata catalog.
The test also executes that loader with an honestly synthetic provenance document,
its actual synthetic content hash, and `content_pin_verified: false`; the loader
rejects it at the exact original-provenance guard. No original authenticity
claims or original input metadata are fabricated.

Therefore exit **3**, `BLOCKED_PUBLIC_ADMISSION`, is an explicit dependency
result—not a passing source simulation, a mutation kill, or a fixture crash
quietly counted as coverage. A compiler/runtime/unexpected assertion error is
reported separately with exit 2/1 and retained diagnostics.

No source simulation object returns from these constructor attempts. The requested
source initialization ledger, snapshot immutability, public `Step` input
consumption, `Restart`, active-clock offense credit, result/captain/onside paths,
and all three matching mutation controls remain **UNRUN**.

There is a second, **statically inspected, not executed** dependency:
`SourceStep` always calls `RequireSourceSoundVmBinding`; binding requires the fixed
original sound-function payload. There is no muted base-profile alternative.
This is not evidence that all simulation verification always requires private
assets: it identifies the precise current public-source admission dependencies
under this task's synthetic/port-source-only authority.

## Fixtures and actual checks

`Program.cs` owns deterministic, explicitly synthetic public DTO fixtures:

- Thirty-one `SourceTeamRoster` objects with sixteen `SourceRosterProfile`
  records each, unique synthetic IDs, and explicit scales.
- Actual public `SourcePlaybook`, formation and route constructors for opening
  kickoff keys; no converted original playbook header.
- Actual `SourceCatchGeometry` with fifteen synthetic finite zero pivots.
- A synthetic linear quarter-direction table, with the port's public numeric
  49-way station mapping. Neither is claimed to be original asset evidence.
- Dimension-valid movement DTOs. Empty constant/selector maps are deliberately
  **not claimed to be a complete live M1 fixture**. The mandatory concrete-roster
  rejection occurs before any of those maps, poses, routes or clips are consumed.
- An animation interface that explicitly rejects asset requests, never inventing
  original clip metadata. No asset request is needed to reach the roster guard.
- Constant-zero injected random draws labelled `ExternalUnverified`, never an
  original PRNG claim.

Named observations distinguish:

1. Base TwoStation / both-human / first Home: exact strict-roster rejection.
2. Base FourStation / both-human / first Away: the same exact rejection.
3. Public animation-only source overload: exact mandatory movement-data rejection.
4. Existing Portable compatibility without M1 bootstrap: explicit rejection.
5. Legacy configuration on the source constructor: explicit rejection, no fallback.
6. Honest synthetic concrete-roster loader input: exact provenance rejection.

The code also contains public source construction/snapshot/skipped-host/Restart
assertions, guarded behind successful source construction. Their literal values
are declared before execution in the captured expectation manifest, with current
source rationale. They do not execute here and must not be counted as passed.
No expected clock totals are produced by clock helpers, observation replay or
recording outputs.

## Execution

Run only from the admitted worktree, in a clean controller environment whose
HOME, TMPDIR and CLI home are inside this component's new `.artifacts` directory:

```sh
env -i \
  HOME="$PWD/tests/source-runtime-integration/.artifacts/controller/home" \
  TMPDIR="$PWD/tests/source-runtime-integration/.artifacts/controller/tmp" \
  DOTNET_CLI_HOME="$PWD/tests/source-runtime-integration/.artifacts/controller/cli-home" \
  PATH=/usr/bin:/bin LANG=C.UTF-8 LC_ALL=C.UTF-8 TZ=UTC \
  /usr/bin/python3.12 -I -S -B tests/source-runtime-integration/run.py \
  --shell-id SOURCE-RUNTIME-UNIQUE-ACTUAL-TOOL-SHELL-ID
```

This is not a `dotnet build`/restore/SDK command. The controller uses only the
admitted pinned direct `dotnet exec csc.dll /noconfig @rsp` path:

- Actual seventy-source closure plus unchanged `SourceCatchGeometry.cs` and its
  required `SourceActorPoseSampler.cs` dependency;
  separate NS2.1 library, the exact 121 pinned references.
- External C#7.3 runner, warnings as errors, exact 167 net10 references.
- Microsoft.NETCore.App **10.0.0**, roll-forward disabled.
- Explicit isolated child environments, HOME/TMPDIR/CLI home in the attempt.
- At most twelve compiler invocations, ten managed launches and three correction
  cycles; 180/120-second compiler/runner timeouts.
- Reserved invocations count before launch, including failures; no refunds.

Each attempt has a fresh directory. Sources, test files, fixtures, expected fields,
tool/reference hashes, argv/environment and remaining budgets are persisted
**before** the corresponding launch. Response files, binaries, stdout/stderr,
exits, counters and post-run preservation checks remain available after failure.
An existing attempt is never overwritten. A correction, if necessary, produces
another numbered attempt with its own immutable handoff. The canonical metadata
handoff is a current report: before refreshing it, the controller archives its
previous complete bytes and records their hash in the new attempt. Failed
sources, command receipts, diagnostics and observations are never overwritten.

The twenty accepted component pins, complete model sources, original admission,
three small acceptance receipts and preparation/tool/reference manifests are
verified without changing them. No old qualification is rerun or requalified.
`metadata/source-runtime-integration/handoff.json` under the admitted base records
the evidence and exact limits for a future **independent Lead** material gate.
