# Remaining common play-end team statistics

## Current production match-statistics regression

The independent `MatchStatisticsChecks.csproj` references the real portable Core.
It preserves the historical campaign below without replaying or relaxing it:

```sh
bash tools/source-player/dotnet.sh build tests/source-remaining-play-end-stats/MatchStatisticsChecks.csproj -c Release --no-restore
bash tools/source-player/dotnet.sh tests/source-remaining-play-end-stats/bin/Release/net10.0/MatchStatisticsChecks.dll <existing-approved563-data-root>
```

Declared boundary fixtures use actual admitted actors/animations, the source
scheduler, C2 next-wake commit and public cabinet final/rematch continuation.
They are not natural full-match, native, Unity or browser evidence.

`SourceMatchView.PlayActivityStats` exposes actual Home/Away offensive plays,
attempted passes, human tackles, fumbles, lost fumbles, bobbles and deflections.
`ScoringStats` exposes original safeties and two-point attempts/conversions.
Both immutable groups are null for old constructor callers (unavailable, `--`);
source snapshots publish observed zeros. No UI/host counters were added.

Pinned `include/GAME.H:446-484` defines these original team fields.
`player.c:5353-5354` counts passes only at release when the **predicted ball
target's oriented integer FIELDX** is at/over source LOS, with no PAT exclusion.
Existing `fumble_ball:10860` counts drops; `_mode_wpn:2251-2290` and
`player_down:5951-5982` count losses only upon established opposing control.
An out-of-bounds fumble or same-side recovery is not a loss; a bounced lateral
can be a loss without a dropped-fumble increment. Bobbles are independent.
Existing `detect.c:1268-1269,1609-1610,1710-1711,1990-1991,2463-2464`
counts only non-drone retained-carrier progress stops, not CPU tackles, assists,
or a generic sack total.

`nfl.c:1736-1748,1792-1808` writes safeties and two-point counters after its
offense-side restoration. Safeties mirror `tdata.safetys` on that post-result
side, **not the side awarded points**; defensive PAT returns still use restored
initial offense for two-point counters. These accumulators run at the existing
once-only C2 result commit, never during pending-result or idle redraws.

## Historical bounded component campaign

This suite owns **one coherent result-dispatch outcome**: ordinary kick/punt
returns, quick returns, defensive sacks, and rushing, including the saved return
origin those return branches actually require. The separately accepted completed
pass method remains byte-identical and is reached through the new single common
dispatcher. One new mixed-case wrapper observation protects that integration; this
suite does not replay the completed-pass campaign.

## Source contract

The fixed reference is `historicalsource/nflblitz2000` revision
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`.
Physical LF coordinates are recorded without normalizing the original CRLF.
Only authenticated identities, coordinates and paraphrases are retained here,
not original source bodies.

* `nfl.c:1515-1601`: ordered ordinary-return, quick-return, pass, sack, rush chain.
* `nfl.c:1396-1443`: last-possession/classifier ownership and saved bounded
  `fpend` before next-state mutations.
* `result.c:158-203,277-318`: a fumble respot does not recompute that saved endpoint.
* `player.c:2121,3087,5739`: kickoff/punt release sets the saved catch yardline to
  `-1`; `3649-3665` captures recovery **FIELD yards**, in post-catch orientation.
  Generic later catch **WORLD-X** is a different value and must not replace it.

Return and rush gains are signed integers. Zero gains still increment their
counts. Sack yards subtract the signed original-LOS gain from the **opposite
classifier side**, without changing total yards. Sacks are not the earlier
consecutive-loss/fire counter. Preserve the original non-obvious exclusions:
sacks do not add a kicked-ball, incomplete, carrier-team or loss-only restriction.
Quick-return precedence survives forward-pass flags.

There is no rush-TD or return-TD counter in this chain. An ordinary return requires
`NewPossession`, not `Touchdown`; the quick branch admits both. Conversion is not
a quick-return touchdown. The independent field-goal-attempt increment at
`nfl.c:1603-1604`, following result-switch counters and preceding fire processing
are adjacent discoveries, **not implemented or audited by this outcome**.

## Observation and compatibility

`SourcePlayEndStatsView` groups the twelve Home/Away return-yard/count,
rushing-yard/count and sack-yard/count scalars. Yard observations are signed;
negative counts reject their exact parameter. `SourceMatchView.PlayEndStats`
is supplied by a new sixth additive overload. All five existing constructor
blocks and previous observations remain unchanged; old overloads leave the new
observation null.

The snapshot reads actual state arrays. The existing independently stored
`SourceTotalYards` receives returns and rushes as well as completed passes;
`CompletedPassStats.HomeTotalYards/AwayTotalYards` observes this shared total,
not a derived sum or a second total. Scalar copies stay immutable after later
commits, and reads do not update counters.

## Bounded execution, not a general test controller

The coordinator-owned `contract.json`, `implementation-admission.json`, and
`execution-admission.json` under this outcome's metadata directory are required
and hash-pinned. They authorize exactly M1/U1/D1/M2/M3, not automatic retries.
The driver requires an exclusive reservation before every launch and refuses an
unreserved invocation, changed input, exhausted budget or old campaign.

The new inventory freezes **132 named cases**, every scalar's literal value,
unit and order, and all thirty distinct actual-production mutation anchors
**before the first compiler**. Expected tuples are source-backed literals, never
computed from observed output or a cloned statistics/classifier algorithm.
Each semantic control compiles the full captured production closure, then must
exit `2` at its exact declared first semantic assertion with the identical clean
prefix. Compile, fixture, guard, loading, timeout or wrong-field failures are not
semantic successes.

Fresh admission permits:

* One new twelve-test stdlib invocation, including eight static wiring controls.
* One driver: clean, thirty actual-source controls, one production-only NS2.1
  compile, and a newly compiled final baseline.
* Exactly 33 compiler and 32 managed launches, no correction/retry campaign.
* Exact 77 production paths, two distinct harness paths, 167 named net10
  references, and 121 named NS2.1 compile-only references.
* Direct fixed Python/Roslyn tools, C# 7.3, deterministic warnings-as-errors;
  no project/source-list edits, SDK discovery, restore, packages or installation.

The existing fixture file is unchanged. Only its three internal helpers are
reused; `/main:SourceRemainingPlayEndStatsChecks` selects this new Main, never the
old `Program.Main`. One `ComponentState` initializes all fourteen genuine actors
and twenty-eight genuine typed animation layers once per managed process, using
the explicitly synthetic one-frame/fifteen-joint fixture. Unknown clip/frame/RNG
requests throw; RNG stays zero; original hashes/provenance remain absent/false.
No reflection, hidden actors, private/uninitialized construction, copied fixture
or weakened production admission is involved.

Five precise whole-file inverses preserve the accepted generation. The caller
guard first reverses only four new capture/reset/dispatch edits to its current
full-file pin, then only the old 92-byte completion insertion, then the unchanged
older kickoff-call restoration. Each required whole-file hash remains mandatory;
comments, duplicate calls, wrong locations, permissive stripping and hash rebases
are rejected.

## Evidence limits

These are actual resolver/state-producer/snapshot/DTO **component** observations
and static genuine-caller wiring evidence. The real catch/releases/common
`ApplySourcePlayEnd`, C2, `Step`, natural terminal/idle and public `Restart` are
not executed. Original asset/animation/roster/audio admission, native/Unity/browser
behavior, final screens, ABI/undefined overflow, natural reachability of mixed
inputs, adjacent producers, all16/whole-game completeness, rights and publication
remain unqualified. Independent Quality review is required; author execution is
not self-approval.
