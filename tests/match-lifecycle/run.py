#!/usr/bin/env python3
"""Declared production lifecycle/captain callers, not a public full match or native oracle."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import canonical_hashes, production_paths
from prepare_inputs import fixture_hashes

SOURCE = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
BRIDGE = "Assets/Scripts/Runtime/FixedStepBridge.cs"
HARNESSES = (
    "tests/match-lifecycle/Directory.Build.props",
    "tests/match-lifecycle/MatchLifecycleChecks.csproj",
    "tests/match-lifecycle/Program.cs",
    "tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
    "tests/match-lifecycle/run.py", "tests/match-lifecycle/test_runner.py",
    "tests/match-lifecycle/README.md",
    "tests/ci/isolated_mutations.py", "tests/parity/kernel_sources.py", "tests/parity/prepare_inputs.py",
    "tests/parity/contact_inputs.py", "tests/parity/fetch_inputs.py", "tests/source-oracles/pins.json",
)
EMIT = "                EmitSource(SourceEventType.MatchEnded);"
RETAINED_MUTANTS = (
    ("missing-source-notification", SOURCE, EMIT, "",
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("duplicate-source-notification", SOURCE, EMIT, EMIT + "\n" + EMIT,
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("wrong-source-notification", SOURCE, EMIT, EMIT.replace("MatchEnded", "PeriodChanged"),
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("missing-legacy-notification", SOURCE,
     "                Emit(GameEventType.GameEnded, TeamId.Home, -1, Model.HomeScore.CompareTo(Model.AwayScore),\n"
     "                    default(FieldVector));", "",
     "legacy GameEnded remains exactly once with its unchanged score-comparison payload"),
    ("invented-terminal-actor", SOURCE, EMIT, EMIT.replace("MatchEnded);", "MatchEnded, actor: 0);"),
     "typed terminal metadata has no invented winner value, actor, target, station or result"),
    ("premature-nonterminal-notification", SOURCE,
     "            if (decision.MatchEnded)\n",
     "            EmitSource(SourceEventType.MatchEnded);\n            if (decision.MatchEnded)\n",
     "unexpired, earlier-quarter, tied overtime and pending PAT boundaries publish no terminal notification"),
    ("stale-gameover-event-batch", "Assets/Scripts/Gameplay/GameSimulation.cs",
     "        public void Step(PlayerCommand command)\n        {\n",
     "        public void Step(PlayerCommand command)\n        {\n"
     "            if (Model.Phase == GamePhase.GameOver) { Model.Tick++; Publish(); return; }\n",
     "later GameOver host steps and queued Continue cannot replay either terminal event"),
    ("invented-terminal-RNG", SOURCE, EMIT,
     "                SourceDraw(100);\n" + EMIT,
     "typed terminal notification adds no RNG or source-game-over reward draws"),
    ("disconnected-source-bridge", BRIDGE,
     "if (handler != null) handler(sourceEvents[i]);",
     "if (handler != null && sourceEvents[i].Type != SourceEventType.MatchEnded) handler(sourceEvents[i]);",
     "actual FixedStepBridge delivers typed and legacy terminal notifications exactly once in a batched frame"),
)
CAPTAIN_CALL = "            if (SourceMatchFlow.ToggleCaptainAfterResult(resolution, endingType, Model.SourceFirstPossession))"
CAPTAIN_INTEGRATION = (CAPTAIN_CALL + "\n"
     "                Model.SourceCaptain = Model.SourceCaptain == SourceCaptainSlot.First\n"
     "                    ? SourceCaptainSlot.Second : SourceCaptainSlot.First;")
CAPTAIN_PROFILES = ("C1-Strict", "M1-Strict", "M1-Portable")
CAPTAIN_SCENARIOS = (
     "turnover-on-downs-to-first-pos", "established-possession-turnover-to-first-pos",
     "next-play-kickoff-no-toggle", "ending-play-kickoff-no-toggle",
     "offense-equals-initial-no-toggle", "resolved-offense-not-first-pos-no-toggle",
)
RETAINED_ASSERTIONS, CAPTAIN_ASSERTIONS, TOTAL_ASSERTIONS = 593, 1778, 2371
CAPTAIN_FIELD = "immediate-EndSourcePlay/commit/Match.Possession.Captain/exactly-once"
FIRST_CAPTAIN_CASE = "captain/C1-Strict/Home/First/" + CAPTAIN_SCENARIOS[0]
ENDING_CAPTAIN_CASE = "captain/C1-Strict/Home/First/" + CAPTAIN_SCENARIOS[3]
CAPTAIN_MUTANTS = (
     ("captain-omitted-commit-call", SOURCE, CAPTAIN_INTEGRATION, "",
      FIRST_CAPTAIN_CASE + "/" + CAPTAIN_FIELD),
     ("captain-wrong-ending-type", SOURCE, CAPTAIN_CALL,
      CAPTAIN_CALL.replace("resolution, endingType,", "resolution, resolution.State.PlayType,"),
      ENDING_CAPTAIN_CASE + "/" + CAPTAIN_FIELD),
     ("captain-wrong-first-possession", SOURCE, CAPTAIN_CALL,
      CAPTAIN_CALL.replace("endingType, Model.SourceFirstPossession", "endingType, 1 - Model.SourceFirstPossession"),
      FIRST_CAPTAIN_CASE + "/" + CAPTAIN_FIELD),
     ("captain-double-commit-toggle", SOURCE, CAPTAIN_INTEGRATION,
      CAPTAIN_INTEGRATION + "\n" + CAPTAIN_INTEGRATION,
      FIRST_CAPTAIN_CASE + "/" + CAPTAIN_FIELD),
)
MUTANTS = RETAINED_MUTANTS + CAPTAIN_MUTANTS
ORIGINAL_AUTHORITY = dict(
     repository="historicalsource/nflblitz2000", revision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
     path="nfl.c", bytes=107299, sha256="97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434",
     git_blob_sha1="15b49474fb55e8bac280b6515fcf7bf59cdf8664",
     physical_lf_lines=dict(saved_ending_type=1429, predicate_and_write=[1914, 1920], result_box=1928),
     authority_method="Lead's authenticated whole-game-code-next handoff; no original body copied into this harness",
)
CAPTAIN_COMMON_FIELDS = (
     "configuration/grouped-four-station", "startup/real-camera-and-60-host-57-source-cycle",
     "before/published-captain-and-possession", "before/declared-result-inputs",
     "commit/published-possession", "commit/result-classification", "commit/score-and-quarter-score",
     "commit/rules-spot-down", "commit/displayed-lines", "commit/flags-and-last-result",
     "commit/clock-fraction-and-stop", "commit/phase-ball-and-actors", "commit/exact-source-and-legacy-events",
     "commit/no-added-boundary-RNG", "commit/dispatch", "commit/compatibility",
     "later/Publish-cannot-toggle", "later/host-source-wakes-cannot-replay", "later/public-Continue",
     "later/final-publication-no-double-toggle",
)
CAPTAIN_QUEUE_FIELDS = (
     "queue/Match.Possession.Captain", "queue/uncommitted-result-rules-score-clock",
     "queue/skipped-host-cannot-commit", "queued-game_proc/commit/caller",
)
CAPTAIN_CONSUMER_FIELDS = ("consumer/actual-next-play-selection", "consumer/actual-captain-and-second-player-control")


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def required_cases():
    cases = set()
    for mode in ("StrictSource", "Portable"):
        cases.add(f"{mode}/nonterminal/q3/7-0/15360/Normal")
        cases.update(f"{mode}/nonterminal/q{q}/7-0/0/Normal" for q in range(3))
        cases.update(f"{mode}/nonterminal/q{q}/14-14/0/Normal" for q in range(3, 6))
        cases.update(f"{mode}/nonterminal/q{q}/7-0/0/ExtraPoint" for q in range(3, 7))
        for q in range(3, 7):
            cases.update((f"{mode}/terminal/q{q}/7-0/gateTrue", f"{mode}/terminal/q{q}/0-14/gateTrue"))
        cases.add(f"{mode}/terminal/q6/14-14/gateTrue")
        cases.update(f"{mode}/terminal/q{q}/14-14/gateFalse" for q in range(6))
        cases.add(f"{mode}/terminal/q3/7-0/gateFalse")
        cases.update((f"{mode}/actual-bridge/gateTrue", f"{mode}/actual-bridge/gateFalse",
                      f"{mode}/actual-period-producer-no-RNG"))
        cases.update(f"{mode}/actual-PAT/q{q}/{home}-{away}"
                     for q, home, away in ((3, 8, 6), (6, 7, 6), (3, 6, 8), (3, 7, 6)))
    cases.update("legacy60/terminal/" + str(sign) for sign in (-1, 0, 1))
    return cases


def ordered_captain_cases():
    return [f"captain/{profile}/{first}/{initial}/{scenario}" for profile in CAPTAIN_PROFILES
            for first in ("Home", "Away") for initial in ("First", "Second") for scenario in CAPTAIN_SCENARIOS]


def required_captain_cases():
    return set(ordered_captain_cases())


def captain_expectation(identity):
    if identity not in required_captain_cases():
        raise RuntimeError("Unknown captain case: " + str(identity))
    _, profile, first, initial, scenario = identity.split("/")
    other = "Away" if first == "Home" else "Home"
    # Declared result table and original nfl.c four guards; never query the production predicate.
    old, original, last, resolved, ending, next_type, toggle, kind, cause, down, los, target, flags, x = {
        CAPTAIN_SCENARIOS[0]: (other, other, other, first, "Normal", "Normal", True, "Down",
                               "CarrierTackledInBounds", 1, 50, 80, 0, 0),
        CAPTAIN_SCENARIOS[1]: (first, other, first, first, "Normal", "Normal", True, "NewPossession",
                               "CarrierTackledInBounds", 1, 50, 80, 8, 0),
        CAPTAIN_SCENARIOS[2]: (first, other, first, first, "Normal", "Kickoff", False, "Safety",
                               "CarrierDownInOwnEndZone", 2, 30, 60, 8, -400 if first == "Home" else 400),
        CAPTAIN_SCENARIOS[3]: (first, other, first, first, "Kickoff", "Normal", False, "NewPossession",
                               "CarrierTackledInBounds", 1, 50, 80, 8, 0),
        CAPTAIN_SCENARIOS[4]: (first, first, first, first, "Normal", "Normal", False, "Down",
                               "CarrierTackledInBounds", 3, 50, 60, 0, 0),
        CAPTAIN_SCENARIOS[5]: (other, first, other, other, "Normal", "Normal", False, "NewPossession",
                               "CarrierTackledInBounds", 1, 50, 80, 8, 0),
    }[scenario]
    if toggle != (next_type != "Kickoff" and ending != "Kickoff" and resolved != original and resolved == first):
        raise RuntimeError("Declared captain table conflicts with original four-condition authority.")
    expected = dict(old_offense=old, initial_offense=original, last_possession=last, resolved_offense=resolved,
                    saved_ending_type=ending, next_type=next_type, predicate=toggle,
                    captain=("Second" if initial == "First" else "First") if toggle else initial,
                    result=kind, cause=cause, down=down, los=los, first_down=target, flags=flags, raw_play_end_x=x)
    configuration = dict(layout="FourStation", first_possession=first, initial_captain=initial,
                         compatibility="Portable" if profile == "M1-Portable" else "StrictSource",
                         stations=[dict(id=i, team="Home" if i < 2 else "Away", initial_player_id=-1) for i in range(4)])
    caller = "immediate-EndSourcePlay" if profile == "C1-Strict" else "queued-game_proc"
    fields = set(CAPTAIN_COMMON_FIELDS) | {caller + "/commit/Match.Possession.Captain/exactly-once"}
    fields.update(CAPTAIN_QUEUE_FIELDS if profile != "C1-Strict" else ("immediate-EndSourcePlay/commit/caller",))
    if toggle:
        fields.update(CAPTAIN_CONSUMER_FIELDS)
    return expected, configuration, caller, fields


def unique_cases(value, allowed):
    return (isinstance(value, list) and all(isinstance(item, str) for item in value)
            and len(value) == len(set(value)) and set(value).issubset(allowed))


def require_captain_records(rows, identities):
    if (not isinstance(rows, list) or len(rows) != len(identities)
            or any(not isinstance(row, dict) for row in rows)
            or [row.get("id") for row in rows] != identities):
        raise RuntimeError("Captain records must match their exact case partition, once each.")
    for row in rows:
        identity = row["id"]
        expected, configuration, caller, fields = captain_expectation(identity)

        def demand(condition, field):
            if not condition:
                raise RuntimeError(identity + ": missing/wrong captain record field " + field)

        actual = row.get("actual")
        demand(row.get("profile") == identity.split("/")[1] and row.get("caller") == caller
               and row.get("declared_boundary_fixture") is True and row.get("expected") == expected
               and row.get("configuration") == configuration, "declared-original-table/configuration")
        demand(row.get("assertions") == len(fields) and isinstance(row.get("invariants"), dict)
               and set(row["invariants"]) == fields and all(value is True for value in row["invariants"].values()),
               "exact-assertion-fields")
        demand(isinstance(actual, dict), "actual")
        observations = ("startup", "before", "commit", "later_publish", "continue", "later")
        for name in observations:
            demand(isinstance(actual.get(name), dict), name)
        startup, before, commit = (actual[name] for name in ("startup", "before", "commit"))
        initial, first = configuration["initial_captain"], configuration["first_possession"]
        for name in observations:
            observation = actual[name]
            demand(all(type(observation.get(key)) is int for key in
                       ("host_tick", "source_tick", "end_epoch", "home_score", "away_score", "down", "los",
                        "first_down", "flags", "clock_packed", "clock_local", "clock_play_time", "clock_next_wake_tick",
                        "dispatch_ordinal", "game_proc_last_wake_tick", "repair_count", "rng_trace_count", "rng_draws")),
                   name + "/integer-identity-and-invariants")
            demand(observation.get("compatibility") == configuration["compatibility"]
                   and observation.get("first_possession") == first, name + "/configured-profile-and-first-pos")
            demand(type(observation.get("ball_x")) in (int, float) and math.isfinite(observation["ball_x"]),
                   name + "/finite-original-ball-position")
        demand(startup["host_tick"] == 60 and startup["source_tick"] == 57 and startup.get("captain") == initial,
               "startup/actual-cycle")
        demand(before["host_tick"] == 60 and before["source_tick"] == 57 and before.get("captain") == initial
               and before.get("offense") == expected["old_offense"] and before.get("initial_offense") == expected["initial_offense"]
               and before.get("last_possession") == expected["last_possession"] and before.get("play_type") == expected["saved_ending_type"]
               and before.get("phase") == "LiveBall" and before.get("match_phase") == "LiveBall"
               and before["flags"] == expected["flags"]
               and before["clock_packed"] == 0x3c07 and before.get("clock_active") is True
               and before["home_score"] == 7 and before["away_score"] == 3, "before")
        queued = caller == "queued-game_proc"
        epoch = before["end_epoch"] + (1 if queued else 0)
        demand(commit.get("captain") == expected["captain"] and commit.get("offense") == expected["resolved_offense"]
               and commit.get("initial_offense") == expected["initial_offense"] and commit.get("last_possession") == expected["last_possession"]
               and commit.get("play_type") == expected["next_type"] and commit.get("phase") == "DeadBall"
               and commit.get("match_phase") == "PlayResult" and commit.get("result_pending") is False
               and commit["end_epoch"] == epoch and commit["clock_packed"] == 0x3c00 and commit["clock_local"] == 0x3c07
               and commit.get("clock_active") is False and commit["clock_play_time"] == 0
               and commit["clock_next_wake_tick"] == commit["source_tick"] + 1, "commit/published-captain-possession-clock")
        signed_ball_x = commit["ball_x"] if expected["resolved_offense"] == "Home" else -commit["ball_x"]
        post_wake_flags = expected["flags"] | (
            0x4000 if queued and expected["next_type"] != "Kickoff" and signed_ball_x >= 0 else 0)
        demand(all(commit[key] == expected[key] for key in ("down", "los", "first_down"))
               and commit["flags"] == post_wake_flags
               and commit["repair_count"] == before["repair_count"]
               and isinstance(before.get("process_serials"), list) and commit.get("process_serials") == before["process_serials"]
               and commit["game_proc_last_wake_tick"] == commit["source_tick"], "commit/rules-dispatch-compatibility")
        home_points = 2 if expected["result"] == "Safety" and expected["resolved_offense"] == "Away" else 0
        away_points = 2 if expected["result"] == "Safety" and expected["resolved_offense"] == "Home" else 0
        demand(commit["home_score"] == 7 + home_points and commit["away_score"] == 3 + away_points, "commit/score")
        demand(actual.get("result") == dict(kind=expected["result"], ending_type=expected["saved_ending_type"],
               cause=expected["cause"], status="Classified", home_points=home_points, away_points=away_points,
               turnover_on_downs=identity.endswith("/" + CAPTAIN_SCENARIOS[0]), first_down=False), "commit/result")
        legacy = ["TurnoverOnDowns", "PossessionChanged"] if identity.endswith("/" + CAPTAIN_SCENARIOS[0]) else (
            ["Safety"] if expected["result"] == "Safety" else [])
        demand(actual.get("source_events") == ["PlayResolved"] and actual.get("legacy_events") == legacy
               and actual.get("commit_boundary_rng_calls") == 0 and type(actual.get("commit_boundary_rng_calls")) is int
               and isinstance(actual.get("displayed_lines"), list) and len(actual["displayed_lines"]) == 2,
               "commit/events-RNG-lines")
        player = (7 if expected["old_offense"] == "Away" else 0) + 3
        demand(actual.get("source_event_context") == [dict(
            type="PlayResolved", team=expected["old_offense"], actor=player, target=-1, station=-1, value=0,
            play_kind=expected["saved_ending_type"], host_tick=commit["host_tick"], source_tick=commit["source_tick"])],
            "commit/unchanged-typed-actor-context")
        legacy_team = expected["resolved_offense"] if identity.endswith("/" + CAPTAIN_SCENARIOS[0]) else (
            "Away" if expected["old_offense"] == "Home" else "Home")
        demand(actual.get("legacy_event_context") == [dict(
            type=kind, team=legacy_team, player=player, actor=player, target=-1,
            value=2 if kind == "Safety" else 0, host_tick=commit["host_tick"], source_tick=commit["source_tick"])
            for kind in legacy], "commit/unchanged-legacy-payload")
        if queued:
            queue, skipped = actual.get("queue"), actual.get("skipped_host")
            demand(isinstance(queue, dict) and isinstance(skipped, dict), "queue/skipped-host")
            for name, observation, offset in (("queue", queue, 0), ("skipped_host", skipped, 1)):
                demand(observation.get("captain") == initial and observation.get("result_pending") is True
                       and observation.get("host_tick") == before["host_tick"] + offset
                       and observation.get("source_tick") == before["source_tick"] and observation.get("end_epoch") == epoch,
                       name + "/no-early-toggle")
            demand(commit["host_tick"] == before["host_tick"] + 2 and commit["source_tick"] == before["source_tick"] + 1,
                   "queued-game_proc/actual-commit-ticks")
            events = actual.get("commit_c2_events")
            demand(isinstance(events, list) and len(events) == 1 and isinstance(events[0], dict), "commit/C2-record")
            demand(all(events[0].get(key) == value for key, value in dict(source_tick=commit["source_tick"], epoch=epoch,
                       process="game_proc", source_path="nfl.c", function="game_proc", line=1381).items())
                   and type(events[0].get("process_order")) is int and events[0]["process_order"] >= 0, "commit/C2-caller")
        else:
            demand(actual.get("queue") is None and actual.get("skipped_host") is None and actual.get("commit_c2_events") == []
                   and commit["host_tick"] == before["host_tick"] and commit["source_tick"] == before["source_tick"]
                   and commit["rng_trace_count"] == before["rng_trace_count"] and commit["rng_draws"] == before["rng_draws"],
                   "immediate-EndSourcePlay/no-invented-queue")
        demand(actual["later_publish"].get("captain") == expected["captain"]
               and actual["later_publish"]["host_tick"] == commit["host_tick"]
               and actual["later_publish"]["source_tick"] == commit["source_tick"], "later/Publish")
        wakes = actual.get("later_host_wakes")
        demand(isinstance(wakes, list) and len(wakes) == 4 and all(isinstance(wake, dict) for wake in wakes), "later/wakes")
        previous_source = commit["source_tick"]
        for offset, wake in enumerate(wakes, 1):
            demand(wake.get("host_tick") == commit["host_tick"] + offset and type(wake.get("source_tick")) is int
                   and wake["source_tick"] - previous_source in (0, 1) and wake.get("captain") == expected["captain"]
                   and wake.get("phase") == "DeadBall" and wake.get("end_epoch") == epoch
                   and wake.get("result_pending") is False, "later/actual-host-source-wake")
            previous_source = wake["source_tick"]
        demand(previous_source > commit["source_tick"] and actual["continue"]["host_tick"] == wakes[-1]["host_tick"] + 2
               and actual["continue"].get("captain") == expected["captain"]
               and actual["continue"].get("phase") == ("PreSnap" if expected["next_type"] == "Kickoff" else "PlaySelect"),
               "later/public-Continue")
        consumer = actual.get("consumer")
        if expected["predicate"]:
            demand(isinstance(consumer, dict) and consumer.get("path") ==
                   "public station selection -> FinishSourcePlaySelection -> LineUpSourceActors -> ConfigureSourceStations"
                   and isinstance(consumer.get("state"), dict)
                   and consumer["state"].get("captain") == expected["captain"] and consumer["state"].get("phase") == "PreSnap",
                   "consumer/actual-next-play-path")
            stations = consumer.get("stations")
            demand(isinstance(stations, list) and len(stations) == 4 and all(isinstance(station, dict) for station in stations)
                   and [station.get("station") for station in stations] == [0, 1, 2, 3], "consumer/grouped-stations")
            demand(all(type(station.get("actor")) is int and 0 <= station["actor"] < 14
                       and station["actor"] // 7 == station["station"] // 2 and type(station.get("control")) is int
                       and station["control"] & (8 if station["station"] % 2 == (0 if expected["captain"] == "First" else 1) else 16)
                       for station in stations) and len({station["actor"] for station in stations}) == 4,
                   "consumer/captain-and-second-player-roles")
        else:
            demand(consumer is None, "consumer/not-claimed-for-guard-fixture")
        demand(actual["later"].get("captain") == expected["captain"] and actual["later"].get("end_epoch") == epoch
               and actual["later"].get("result_pending") is False, "later/exactly-once")


def require_summary(code, summary, expected_failure=None):
    if (not isinstance(summary, dict) or summary.get("schema") != "source-match-ended-caller-checks/v1"
            or summary.get("whole_game_or_native_qualified") is not False
            or summary.get("declared_boundary_cases_not_public_full_match") is not True):
        raise RuntimeError("Actual declared M1/bridge scope and summary are required.")
    cases = summary.get("cases")
    retained, captain = summary.get("retained"), summary.get("captain_transition")
    if not isinstance(retained, dict) or not isinstance(captain, dict):
        raise RuntimeError("Separate retained593/71 and captain1778/72 partitions are required.")
    retained_cases, captain_cases = retained.get("cases"), captain.get("cases")
    if (not unique_cases(cases, required_cases() | required_captain_cases())
            or not unique_cases(retained_cases, required_cases()) or not unique_cases(captain_cases, required_captain_cases())
            or cases != retained_cases + captain_cases):
        raise RuntimeError("Declared lifecycle case identities cannot be missing, duplicated or substituted.")
    if (captain.get("schema") != "source-post-result-captain-checks/v1"
            or captain.get("contract") != "source-post-result-captain-transition/v1"
            or captain.get("expected_assertions") != CAPTAIN_ASSERTIONS or captain.get("qualifying_cases") != 24
            or captain.get("guard_negative_cases") != 48 or captain.get("whole_game_or_native_qualified") is not False
            or type(retained.get("assertions")) is not int or type(captain.get("assertions")) is not int
            or min(retained["assertions"], captain["assertions"]) < 0
            or summary.get("assertions") != retained["assertions"] + captain["assertions"]):
        raise RuntimeError("Fixed assertion totals, original authority scope and exact additive partitions are required.")
    if expected_failure is not None:
        if (code != 1 or summary.get("status") != "failed" or summary.get("assertions", 0) < 1
                or summary.get("failed_assertion") != expected_failure):
            raise RuntimeError("A compiled lifecycle control must reject its exact causal assertion.")
        if expected_failure in {item[4] for item in CAPTAIN_MUTANTS}:
            failing_case = ENDING_CAPTAIN_CASE if expected_failure.startswith(ENDING_CAPTAIN_CASE + "/") else FIRST_CAPTAIN_CASE
            prefix = ordered_captain_cases()[:4 if failing_case == ENDING_CAPTAIN_CASE else 1]
            expected_assertions = 77 if failing_case == ENDING_CAPTAIN_CASE else 7
            rows = captain.get("records")
            if (retained["assertions"] != RETAINED_ASSERTIONS or set(retained_cases) != required_cases()
                    or captain_cases != prefix or captain["assertions"] != expected_assertions
                    or not isinstance(rows, list) or len(rows) != len(prefix) or not isinstance(rows[-1], dict)):
                raise RuntimeError("Captain controls require their frozen retained/full and additive/failure-prefix partitions.")
            require_captain_records(rows[:-1], prefix[:-1])
            failed = rows[-1]
            expected, configuration, caller, _ = captain_expectation(failing_case)
            fields = set(CAPTAIN_COMMON_FIELDS[:4]) | {"immediate-EndSourcePlay/commit/caller", CAPTAIN_FIELD}
            invariants, actual = failed.get("invariants"), failed.get("actual")
            if (failed.get("id") != failing_case or failed.get("expected") != expected
                    or failed.get("configuration") != configuration or failed.get("caller") != caller
                    or failed.get("assertions") != 6 or not isinstance(invariants, dict) or set(invariants) != fields
                    or invariants.get(CAPTAIN_FIELD) is not False
                    or any(value is not True for key, value in invariants.items() if key != CAPTAIN_FIELD)
                    or not isinstance(actual, dict) or not isinstance(actual.get("before"), dict)
                    or not isinstance(actual.get("commit"), dict)
                    or actual["before"].get("captain") != configuration["initial_captain"]
                    or actual["commit"].get("captain") != ("Second" if expected["captain"] == "First" else "First")):
                raise RuntimeError("Captain control must fail its exact published field in the actual immediate caller.")
        elif captain["assertions"] != 0 or captain_cases or captain.get("records") != []:
            raise RuntimeError("Retained controls cannot be relabelled as an additive captain failure.")
    elif (code or summary.get("status") != "passed" or summary.get("assertions") != TOTAL_ASSERTIONS
          or retained["assertions"] != RETAINED_ASSERTIONS or set(retained_cases) != required_cases()
          or captain["assertions"] != CAPTAIN_ASSERTIONS or captain_cases != ordered_captain_cases()
          or len(cases) != 143):
        observed = {key: summary.get(key) for key in ("status", "assertions", "failed_assertion", "error")}
        observed.update(exit_code=code, cases=len(cases))
        raise RuntimeError("Exact retained593/71 plus captain1778/72 partitions (2371 assertions,143 cases) are required. "
                           "Observed: " + json.dumps(observed, sort_keys=True))
    else:
        require_captain_records(captain.get("records"), captain_cases)


def require_compiled_identity(identity, assembly):
    if not isinstance(identity, dict):
        raise RuntimeError("Actual newly compiled production identity is required.")
    try:
        mvid = str(uuid.UUID(identity.get("mvid", "")))
    except (ValueError, TypeError, AttributeError) as error:
        raise RuntimeError("A generated production module MVID is required.") from error
    if (mvid in ("00000000-0000-0000-0000-000000000000", "119605f0-50aa-4181-9f2b-1b723b493270",
                 "7a6092bc-d697-4e54-ac18-089acbd93d25")
            or identity.get("same_assembly_as_harness") is not True or identity.get("module") != "MatchLifecycleChecks.dll"
            or not isinstance(identity.get("assembly"), str) or not identity["assembly"].startswith("MatchLifecycleChecks, ")
            or identity.get("location") != str(assembly)):
        raise RuntimeError("Copied actual production must execute in this new module, never an old/guarded Core or proxy.")


def require_controls(controls, runs):
    expected = {item[0]: item[4] for item in MUTANTS}
    if (not isinstance(controls, list) or len(controls) != 13 or any(not isinstance(item, dict) for item in controls)
            or [item.get("id") for item in controls] != list(expected)):
        raise RuntimeError("Exactly nine retained plus four added distinct production controls are required.")
    retained = {item[0] for item in RETAINED_MUTANTS}
    for item in controls:
        identity = item["id"]
        run = runs.get(identity, {})
        if (item.get("named_failure") != expected[identity] or item.get("compiled_zero_diagnostics") is not True
                or item.get("partition") != ("retained" if identity in retained else "captain")
                or run.get("compiled_zero_diagnostics") is not True or run.get("exit_code") != 1):
            raise RuntimeError("Clean compile and exact named runtime field/caller failure required: " + identity)
        require_summary(run["exit_code"], run.get("summary"), expected[identity])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--project-assets", type=Path,
                        help="Explicit already-prepared/admitted project.assets.json; never restores or discovers one.")
    args = parser.parse_args()
    out = args.output.absolute()
    if (out.exists() or ".." in out.parts or not out.is_relative_to(ROOT / ".squad/.scratch")
            or any(path.is_symlink() for path in (out, *out.parents))):
        parser.error("Choose a NEW ignored output directory under this worktree's .squad/.scratch.")
    if os.environ.get("BLITZ_CI_MUTATION_WORKERS", "1") != "1":
        parser.error("This bounded lifecycle/captain graph requires exactly one mutation worker.")
    assets = args.project_assets.absolute() if args.project_assets is not None else None
    if assets is not None and (not assets.is_file() or not assets.is_relative_to(ROOT / ".squad/.scratch")
                               or ".." in assets.parts or any(path.is_symlink() for path in (assets, *assets.parents))):
        parser.error("Project assets must be an explicit admitted regular file in this worktree's ignored scratch.")
    out.mkdir(parents=True)
    work = out / "work"
    work.mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
               MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0",
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
    paths = production_paths() + [ROOT / BRIDGE]
    sources = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in paths}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESSES}
    before = {name: digest(raw) for name, raw in sources.items()}
    inputs = {}
    build_inputs = {} if assets is None else {str(assets): digest(assets.read_bytes())}
    result = dict(schema="source-match-ended-caller-run/v1", status="failed", runs={}, commands=[],
                  command_results=[], controls=[],
                  declared_boundary_cases_not_public_full_match=True, whole_game_or_native_qualified=False,
                  production_before=before, harness_before={name: digest(raw) for name, raw in harness.items()},
                  runner_sha256=digest(Path(__file__).read_bytes()), inputs_before=inputs, build_inputs_before=build_inputs,
                  captain_transition=dict(
                      schema="source-post-result-captain-run/v1", contract="source-post-result-captain-transition/v1",
                      original_authority=ORIGINAL_AUTHORITY, production_source=SOURCE,
                      production_sha256=before[SOURCE], harness_sha256={name: digest(raw) for name, raw in harness.items()},
                      source_snapshot_sha256=digest(json.dumps(before, sort_keys=True, separators=(",", ":")).encode("utf-8")),
                      declared_case_ids=dict(retained=sorted(required_cases()), added=ordered_captain_cases(),
                                             union=sorted(required_cases() | required_captain_cases())),
                      fixed_partitions=dict(retained_cases=71, added_cases=72, union_cases=143,
                                            retained_assertions=RETAINED_ASSERTIONS, added_assertions=CAPTAIN_ASSERTIONS,
                                            total_assertions=TOTAL_ASSERTIONS, qualifying_cases=24, guard_negative_cases=48,
                                            retained_controls=9, added_controls=4, total_controls=13),
                      declared_control_ids=dict(retained=[item[0] for item in RETAINED_MUTANTS],
                                                added=[item[0] for item in CAPTAIN_MUTANTS]),
                      baseline="not-started", final_baseline="not-started",
                      planned_direct_processes=dict(compile=15 if args.controls else 2, candidate=15 if args.controls else 2),
                      input_interface=dict(contact272_manifest=str(args.fixture_manifest.absolute()),
                                           sound_functions=str(args.sound_functions.absolute()),
                                           m1_root=str(args.c2_m1_inputs.absolute())),
                      inputs_sha256=inputs, build_inputs_sha256=build_inputs,
                      compiled_identity_method="Reflection inside each newly compiled production executable, paired with DLL/closure hashes; no standalone reader or old Core",
                      whole_game_or_native_qualified=False),
                  complete_compiled_control_closure_executed=False)

    def command(label, argv, kind):
        result["commands"].append(argv)
        record = dict(label=label, argv=argv, kind=kind, status="launching", process_started=False, timeout_seconds=240)
        result["command_results"].append(record)
        scratch = out / "runtime" / label
        scratch.mkdir(parents=True, exist_ok=True)
        execute_env = dict(env, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
        try:
            process = subprocess.run(argv, env=execute_env, cwd=ROOT, capture_output=True, text=True, timeout=240)
        except subprocess.TimeoutExpired:
            record.update(process_started=True, status="timeout-not-a-control-kill")
            raise
        except OSError:
            record["status"] = "start-error-not-a-control-kill"
            raise
        record.update(process_started=True, exit_code=process.returncode, status="exited")
        (out / (label + ".log")).write_text(process.stdout + process.stderr)
        return process

    def execute(label, mutation=None):
        run = dict(status="preparing-source-copy", compiled_zero_diagnostics=False)
        result["runs"][label] = run
        if label in ("baseline", "final-baseline"):
            result["captain_transition"][label.replace("-", "_")] = "started"
        copy = out / "copies" / label
        for base, entries in ((copy / "source", sources), (copy / "harness", harness)):
            for name, raw in entries.items():
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
        if mutation is not None:
            identity, filename, old, new, expected = mutation
            path = copy / "source" / filename
            raw = path.read_bytes()
            if raw.count(old.encode("utf-8")) != 1:
                raise RuntimeError("Lifecycle control requires one exact production anchor: " + identity)
            path.write_bytes(raw.replace(old.encode("utf-8"), new.encode("utf-8")))
        compiled_sources = {name: digest((copy / "source" / name).read_bytes()) for name in sources}
        changed = {name for name in compiled_sources if compiled_sources[name] != before[name]}
        if changed != ({mutation[1]} if mutation is not None else set()):
            raise RuntimeError("Only the named real production mutation may differ: " + label)
        run["compiled_production_closure"] = compiled_sources
        run["production_snapshot_sha256"] = digest(json.dumps(compiled_sources, sort_keys=True, separators=(",", ":")).encode("utf-8"))
        run["compiled_harness"] = {name: digest((copy / "harness" / name).read_bytes()) for name in harness}
        project = copy / "harness/tests/match-lifecycle/MatchLifecycleChecks.csproj"
        build = out / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(copy / "source") + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        if assets is not None:
            common.append("-p:ProjectAssetsFile=" + str(assets))
        argv = ["dotnet", "build", str(project), "--no-restore", "-c", "Release", "-v", "quiet",
                "-m:1", "-nr:false", *common]
        run["status"] = "compiling"
        process = command(label + "-compile", argv, "compile")
        run["compile_exit_code"] = process.returncode
        if process.returncode and "NETSDK1004" in process.stdout + process.stderr:
            raise RuntimeError("Missing prepared project assets; STOP for separate resource admission, never implicit restore: " + label)
        if process.returncode or "0 Warning(s)" not in process.stdout or "0 Error(s)" not in process.stdout:
            raise RuntimeError("Compile errors/warnings cannot qualify a lifecycle control: " + label)
        run.update(compiled_zero_diagnostics=True, status="running-candidate")
        assembly = build / "bin/Release/net10.0/MatchLifecycleChecks.dll"
        run["assembly_sha256"] = digest(assembly.read_bytes())
        process = command(label, ["dotnet", str(assembly), str(args.fixture_manifest.absolute()),
            str(args.sound_functions.absolute()), str(args.c2_m1_inputs.absolute())], "candidate")
        summaries = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[0] if len(summaries) == 1 else None
        run.update(exit_code=process.returncode, summary=summary)
        require_compiled_identity(summary.get("compiled_production_identity") if isinstance(summary, dict) else None, assembly)
        run["compiled_production_identity"] = summary["compiled_production_identity"]
        require_summary(process.returncode, summary, mutation[4] if mutation else None)
        run["assembly_sha256_after"] = digest(assembly.read_bytes())
        if ({name: digest((copy / "source" / name).read_bytes()) for name in sources} != compiled_sources
                or {name: digest((copy / "harness" / name).read_bytes()) for name in harness} != run["compiled_harness"]
                or run["assembly_sha256_after"] != run["assembly_sha256"]):
            raise RuntimeError("Compiled production/harness copy changed during execution: " + label)
        run["status"] = "killed-by-exact-named-runtime-assertion" if mutation else "passed"
        control = dict(id=label, named_failure=summary["failed_assertion"], compiled_zero_diagnostics=True,
                       partition="captain" if mutation in CAPTAIN_MUTANTS else "retained") if mutation else None
        if control is not None:
            result["controls"].append(control)
        print("PASS " + label, flush=True)
        return control

    try:
        inputs.update(fixture_hashes(args.fixture_manifest, args.sound_functions))
        inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        execute("baseline")
        result["captain_transition"]["baseline"] = "passed"
        if args.controls:
            run_mutations(MUTANTS, lambda mutation: execute(mutation[0], mutation))
            require_controls(result["controls"], result["runs"])
        execute("final-baseline")
        result["captain_transition"]["final_baseline"] = "passed"
        (out / "captain-cases.json").write_text(json.dumps(dict(
            schema="source-post-result-captain-cases/v1", observation_run="final-baseline",
            declared_boundary_cases_not_public_full_match=True, whole_game_or_native_qualified=False,
            original_authority=ORIGINAL_AUTHORITY,
            source_snapshot_sha256=result["runs"]["final-baseline"]["production_snapshot_sha256"],
            compiled_production_identity=result["runs"]["final-baseline"]["compiled_production_identity"],
            qualification="Actual observations only; acceptance requires the paired passing unchanged-generation result.json",
            rows=result["runs"]["final-baseline"]["summary"]["captain_transition"]["records"]), indent=2) + "\n")
        result["captain_transition"]["case_artifact"] = dict(path="captain-cases.json",
            sha256=digest((out / "captain-cases.json").read_bytes()), rows=72)
        result["complete_compiled_control_closure_executed"] = args.controls
        result["status"] = "passed-declared-real-M1-terminal-and-bridge" if args.controls else "passed-component-only-controls-not-run"
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        for label in ("baseline", "final-baseline"):
            key = label.replace("-", "_")
            if result["captain_transition"][key] == "started":
                result["captain_transition"][key] = "failed-or-incomplete"
        print("MATCH LIFECYCLE FAILED: " + str(error), file=sys.stderr)
    finally:
        result["production_after"] = {p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in paths}
        result["inputs_after"] = {name: digest(Path(name).read_bytes()) for name in inputs}
        result["build_inputs_after"] = {name: digest(Path(name).read_bytes()) for name in build_inputs}
        result["harness_after"] = {name: digest((ROOT / name).read_bytes()) for name in HARNESSES}
        result["captain_transition"]["actual_control_ids"] = dict(
            retained=[item["id"] for item in result["controls"] if item["partition"] == "retained"],
            added=[item["id"] for item in result["controls"] if item["partition"] == "captain"],
            union=[item["id"] for item in result["controls"]])
        result["captain_transition"]["actual_command_records"] = result["command_results"]
        result["captain_transition"]["actual_runs"] = {
            label: {key: run[key] for key in ("status", "exit_code", "compiled_zero_diagnostics", "assembly_sha256",
                                            "compiled_production_identity", "production_snapshot_sha256") if key in run}
            for label, run in result["runs"].items()}
        result["captain_transition"]["started_direct_processes"] = {
            kind: sum(record["process_started"] and record["kind"] == kind for record in result["command_results"])
            for kind in ("compile", "candidate")}
        result["captain_transition"]["process_accounting_scope"] = (
            "Direct dotnet children only. Python guard/wrapper and MSBuild/Roslyn/SDK descendants need separately admitted external accounting; no retry/refund or inherited57-process authority.")
        if (result["production_after"] != before or result["inputs_after"] != inputs
                or result["build_inputs_after"] != build_inputs
                or result["harness_after"] != result["harness_before"]
                or digest(Path(__file__).read_bytes()) != result["runner_sha256"]):
            result["status"] = "inconclusive-generation-changed"
        (out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"].startswith("passed-") else 1


if __name__ == "__main__":
    raise SystemExit(main())
