#!/usr/bin/env python3
"""Compare actual canonical281 composition with a separately labelled isolated cache witness."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import time
from threading import Lock

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
sys.path.insert(0, str(ROOT / "tests/parity"))
from contact_inputs import fixture_profile
from prepare_inputs import fixture_hashes
from kernel_sources import (canonical_hashes, production_paths, C2_ANIMATION_SHA, C2_M1_DATA_SHA,
                            PRESNAP_ANIMATION_SHA, PRESNAP_DATA_SHA, successor_hashes)
from compare_physics import compare
import prepare_public_replay as public_replay

CACHE_SOURCE = "Assets/Scripts/Gameplay/SourceMovementIntegration.cs"
CACHE_SOURCE_SHA = "17ea5f52c2d5eddbde015f83c9d550e8e96229df0698c32aee4bd04060c0283b"
U1_CACHE_SOURCE_SHA = "9d676f882d5e381f92fe456e0c4b11bdb9489b5917d8156b6a9b285de06d7165"
CALL = """                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true), root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
"""
OBSERVED_CALL = """                PlayerView qaActualCacheView = GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true);
                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    qaActualCacheView, root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
                global::CanonicalCacheCapture.Record(this, qaActualCacheView, root, frame, joints);
"""
HARNESS = (
    "tests/movement-callers/MovementCallerChecks.csproj", "tests/movement-callers/Directory.Build.props",
    "tests/movement-callers/Program.cs", "tests/movement-callers/Canonical.cs",
    "tests/movement-callers/PortablePublicPolicy.cs",
    "tests/movement-callers/Controlled.cs", "tests/movement-callers/InputAndStation.cs",
    "tests/movement-callers/SnapHikeChecks.cs",
    "tests/movement-callers/CompatibilityChecks.cs",
    "tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
)
CANONICAL_CACHE_MUTANTS = (
    ("refresh-after-contact", "production", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
     "SourceContacts();", "SourceContacts();\n            CacheSourcePlayerCameraJoints();",
     "canonical isolated observer records only the actual post-player/pre-contact call"),
    ("wrong-current-root-basis", "harness", "tests/movement-callers/Canonical.cs",
     "SourceVector3 referenceRoot = sample.Root;", "SourceVector3 referenceRoot = afterRoot;",
     "canonical ReferenceRows use exact PRE-contact call arguments"),
    ("drop-contact-cache-checks", "harness", "tests/movement-callers/Canonical.cs",
     "Check(SameBits(sample.Joints[joint], reference[joint]),",
     "if (displaced) continue;\n                        Check(SameBits(sample.Joints[joint], reference[joint]),",
     "canonical all14x15 comparisons include EVERY displaced actor"),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inject(original, expected_sha=CACHE_SOURCE_SHA):
    if expected_sha not in (CACHE_SOURCE_SHA, U1_CACHE_SOURCE_SHA) or sha(original) != expected_sha:
        raise ValueError("Canonical cache observation requires the exact frozen original production hash.")
    old, new = CALL.encode(), OBSERVED_CALL.encode()
    if original.count(old) != 1 or b"CanonicalCacheCapture" in original:
        raise ValueError("Canonical observer needs one unique real geometry call/count-guard site.")
    observed = original.replace(old, new)
    if observed.count(new) != 1 or observed.replace(new, old) != original:
        raise ValueError("Removing only the observer/local argument capture must restore the original bytes.")
    return observed


def inject_current(original):
    actual = sha(original)
    if actual not in (CACHE_SOURCE_SHA, U1_CACHE_SOURCE_SHA):
        raise ValueError("Current cache observer requires an explicitly frozen historical or U1 generation.")
    return inject(original, actual)


M1_COVERAGE_ASSERTION = "M1 full public R_SKIP gate requires after2299 same-instance two ordinals and two real15 poses"
SNAP_HIKE_CONTROLS = (
    ("early-live", "            SourcePlayerDriver.Snap(Model, SourceDraw);",
     "            Model.Phase = GamePhase.LiveBall;\n            Model.SourceMatchPhase = SourceMatchPhase.LiveBall;\n            SourcePlayerDriver.Snap(Model, SourceDraw);",
     "snap initiation remains PreSnap center-held receiver-1 until real HikeBall opcode"),
    ("stale-holder-frame", "            if (actor.SourceMode == SourceActorMode.Kicker)\n                frame = CurrentSourceKickerVariables(frame);",
     "            /* compiled control: stale preactor holder variables */",
     "actual later kicker consumes current holder variables in SAME HikeBall wake"),
    ("no-hiking-guard", "if (Model.Phase != GamePhase.PreSnap || (Model.SourceFlags & SourceGameFlags.HikingBall) != 0)",
     "if (Model.Phase != GamePhase.PreSnap)",
     "existing hiking flag prevents reentrant snap/interpolation reset or duplicate request"),
)


def snap_hike_main(argv):
    parser = argparse.ArgumentParser(description="Source-correct snap request/HikeBall real-factory timing and causal controls.")
    parser.add_argument("--snap-hike-only", action="store_true", required=True)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    output = args.output.absolute()
    if output.exists() or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch")):
        parser.error("Use a new ignored focused output, not a frozen receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {k: sha(v) for k, v in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
               MSBUILDDISABLENODEREUSE="1")
    result = {"schema": "source-snap-hike-focused-kernel/v1", "status": "failed",
              "controlled_fixtures_NOT_public_R_SKIP_witness": True,
              "production_before": pins, "inputs_before": inputs, "runs": {}, "commands": []}

    def execute(label, observed=False, mutation=None):
        copy = output / "copies" / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, raw in entries.items():
                if base == source and name == CACHE_SOURCE and observed:
                    raw = inject(raw)
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
        if mutation:
            identity, old, new, expected = mutation
            path = source / "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
            text = path.read_text()
            if text.count(old) != 1:
                raise RuntimeError("Snap-hike mutation must target one actual producer/consumer: " + identity)
            path.write_text(text.replace(old, new))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(source) + "/", "-p:BaseIntermediateOutputPath=" +
                  str(build / "obj") + "/", "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", *common]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            restore = ["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                       "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
            if restored.returncode:
                raise RuntimeError("Snap-hike restore failed after missing SDK assets.")
            compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        text = compiled.stdout + compiled.stderr
        (output / (label + "-compile.log")).write_text(text)
        if compiled.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Snap-hike causal control requires zero-diagnostic compile: " + label)
        destination = output / label
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   "snap-hike", str(args.fixture_manifest.absolute()), str(args.sound_functions.absolute()),
                   str(args.c2_m1_inputs.absolute()), str(destination), "observed" if observed else "plain"]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        receipt = json.loads((destination / "focused-result.json").read_bytes())
        result["runs"][label] = {"exit_code": process.returncode, "compile_zero_diagnostic": True, "receipt": receipt}
        if mutation:
            if process.returncode != 1 or receipt["status"] != "failed" or expected not in receipt.get("error", ""):
                raise RuntimeError("Snap-hike control did not fail its exact named assertion: " + identity)
            result["runs"][label]["named_causal_failure"] = expected
        elif (process.returncode != 0 or receipt["status"] != "passed" or receipt["snapAssertions"] != 421
              or receipt["referenceDelta"] != 4):
            raise RuntimeError("Exact source-operands/VM-derived snap-hike kernel inventory required: " + label)
        shutil.rmtree(copy)
        return receipt

    try:
        plain = execute("plain")
        observed = execute("observed", True)
        for a, b in zip(plain["cases"], observed["cases"]):
            if (a["fieldGoal"], a["requested"], a["released"], a["history"]) != \
                    (b["fieldGoal"], b["requested"], b["released"], b["history"]):
                raise RuntimeError("Actual cache observer changed snap/hike behavior.")
        for spec in SNAP_HIKE_CONTROLS:
            execute("control-" + spec[0], mutation=spec)
        execute("final-plain")
        result["status"] = "passed-focused-not-full-M1"
    except (RuntimeError, ValueError, OSError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
    finally:
        copies = output / "copies"
        if copies.exists():
            shutil.rmtree(copies)
        result["production_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        result["live_production_unchanged_during_checks"] = result["production_after"] == pins
        if not result["live_production_unchanged_during_checks"]:
            result["status"] = "inconclusive-generation-changed"
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(result["status"])
    return 0 if result["status"] == "passed-focused-not-full-M1" else 1


def require_m1_replay(code, summary, receipt, compatibility="strict", presnap_switch=False):
    expected = "Portable" if compatibility == "portable" else "StrictSource"
    if receipt.get("gameplayCompatibility", "StrictSource") != expected:
        raise RuntimeError("Explicit gameplay compatibility must match the actual factory receipt.")
    if receipt.get("failure") is not None:
        raise RuntimeError("Actual M1 source fault: stop without rescue, extra wake or next replay.")
    if (receipt.get("completedHosts") != 12000 or receipt.get("SourceTick") != 11400
            or receipt.get("profile") != ("original-teams-contact282-c2-m1-presnap-switch" if presnap_switch else
                                          "original-teams-contact281-c2-m1")
            or receipt.get("factory") != ("REAL SourceC2Composition.CreatePreSnapSwitchSimulation" if presnap_switch else
                                          "REAL SourceC2Composition.CreateM1Simulation")
            or receipt.get("sourceData") != (PRESNAP_DATA_SHA if presnap_switch else
                    "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a")
            or receipt.get("originalPublicVectorRetainedWithoutStateConditionedRegeneration") is not True):
        raise RuntimeError("Entire12000 explicit successor actual-factory input/source identity required.")
    if (code == 0 and summary.get("status") == "passed" and summary.get("public_R_SKIP_gate_qualified") is True
            and receipt.get("positive") and (not presnap_switch or receipt.get("preSnapSwitchWitnesses",0)>0)):
        return "positive"
    if (code == 1 and summary.get("status") == "failed" and
            M1_COVERAGE_ASSERTION in summary.get("failed_assertion", "") and receipt.get("positive") is None):
        return "unmet-public-R_SKIP"
    raise RuntimeError("M1 replay must pass its real R_SKIP gate or report that exact unmet criterion, never relabel a different failure.")


_M1_SIDE_SCHEMA = "source-M1-side-guard/v1"
_M1_SIDE_ISSUES = {
    "malformed_receipt", "malformed_fault", "missing_guard", "malformed_guard", "conflicting_guard",
    "conflicting_label", "conflicting_observed", "conflicting_profile", "conflicting_source_data",
    "conflicting_replay_hash", "missing_source_row", "oversized_source_row", "malformed_source_row",
    "conflicting_host", "conflicting_tick", "conflicting_raw_command", "malformed_raw_stations",
    "malformed_applied_stations", "missing_current_rules", "malformed_current_rules",
    "missing_published", "malformed_published", "conflicting_guard_applied", "malformed_projection",
    "conflicting_projection_hash", "conflicting_fault_identity",
}


def _m1_side_stations(command):
    frame = command["StationInputs"]
    if frame is None:
        return []
    stations = frame["Stations"]
    if not isinstance(stations, list) or len(stations) > 4:
        raise ValueError("station inventory")
    result, seen = [], set()
    for station in stations:
        identity, source = station["StationId"], station["Command"]
        if type(identity) is not int or identity not in range(4) or identity in seen:
            raise ValueError("station identity")
        seen.add(identity)
        specified, action, play = source["IsSpecified"], source["Action"], source["Play"]
        selected, kind, symbol = play["IsSpecified"], play["Playbook"], play["PlaySymbol"]
        if (type(specified) is not bool or type(action) is not int or action not in range(22)
                or type(selected) is not bool or type(kind) is not int or kind not in (0, 1)
                or (selected and (not isinstance(symbol, str) or
                    re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,127}", symbol) is None))
                or (not selected and symbol is not None)):
            raise ValueError("selection metadata")
        result.append({"station": identity, "specified": specified, "action": action,
                       "play_specified": selected, "playbook": kind if selected else None,
                       "symbol": symbol if selected else None})
    return result


def _m1_side_evidence(destination, label, receipt_bytes, expected):
    evidence = {
        "schema": _M1_SIDE_SCHEMA, "status": "missing-evidence", "run": label,
        "host": None, "before": None, "SourceTick": None,
        "receipt_sha256": sha(receipt_bytes), "source_row_sha256": None,
        "replay_sha256": None, "profile": None, "guard_error_json_sha256": None,
        "guard": None, "raw_stations": [], "applied_stations": [],
        "current_rules": None, "published": None, "issues": [],
    }
    issues = evidence["issues"]
    try:
        receipt = json.loads(receipt_bytes)
        failure = receipt["failure"]
        if not isinstance(failure, dict):
            raise ValueError("fault")
        for key in ("host", "before", "SourceTick"):
            value = failure[key]
            if type(value) is not int or not 0 <= value <= (1 << 63) - 1:
                raise ValueError("fault identity")
            evidence[key] = value
        error = failure["error"]
        if not isinstance(error, str):
            raise ValueError("fault error")
    except (ValueError, KeyError, TypeError):
        issues.append("malformed_receipt")
        return _finish_m1_side_evidence(evidence)
    evidence["guard_error_json_sha256"] = sha(json.dumps(error).encode("ascii"))
    prefix = "System.ArgumentException: The selected original playbook does not belong to the submitting source side."
    context = [line for line in error.splitlines() if line.startswith("SOURCE_SIDE_GUARD_V1 ")]
    match = re.fullmatch(
        r"SOURCE_SIDE_GUARD_V1 possession=(-?[0-9]+) phase=(-?[0-9]+) selectingSide=(-?[0-9]+) "
        r"playbook=(-?[0-9]+) selectingOffense=([01]) requestingTeam=(null|-?[0-9]+)",
        context[0]) if len(context) == 1 and len(context[0]) <= 256 and error.splitlines()[0:1] == [prefix] else None
    if match is None:
        issues.append("missing_guard" if not context else "malformed_guard")
    else:
        values = [int(value) if value != "null" else None for value in match.groups()]
        if any(value is not None and not -(1 << 31) <= value < (1 << 31) for value in values):
            issues.append("malformed_guard")
        else:
            possession, phase, side, kind, offense, requesting = values
            evidence["guard"] = {"possession": possession, "phase": phase, "selecting_side": side,
                                 "playbook": kind, "selecting_offense": bool(offense),
                                 "requesting_team": requesting}
            if (possession not in (0, 1) or phase not in range(5) or side not in (0, 1) or kind not in (0, 1)
                    or (requesting is not None and requesting != side)
                    or bool(offense) != (side == possession) or (kind == 0) == bool(offense)):
                issues.append("conflicting_guard")
    try:
        same_destination = destination.name == label and destination.resolve() == destination.absolute()
    except (OSError, RuntimeError):
        same_destination = False
    if not same_destination:
        issues.append("conflicting_label")
        return _finish_m1_side_evidence(evidence)
    for field, issue in (("observed", "conflicting_observed"), ("profile", "conflicting_profile"),
                         ("sourceData", "conflicting_source_data"), ("replayFileSha256", "conflicting_replay_hash")):
        if type(receipt.get(field)) is not type(expected[field]) or receipt.get(field) != expected[field]:
            issues.append(issue)
    if isinstance(receipt.get("replayFileSha256"), str) and re.fullmatch(r"[0-9a-f]{64}", receipt["replayFileSha256"]):
        evidence["replay_sha256"] = receipt["replayFileSha256"]
    if receipt.get("profile") in ("original-teams-contact281-c2-m1", "original-teams-contact282-c2-m1-presnap-switch"):
        evidence["profile"] = receipt["profile"]
    try:
        rules = failure["actual"]["SourceRules"]["OffenseSide"]
        if type(rules) is not int or rules not in (0, 1):
            raise ValueError("rules")
        evidence["current_rules"] = {"offense_side": rules}
    except (KeyError, TypeError, ValueError):
        issues.append("missing_current_rules")
    try:
        state = failure["actual"]["State"]
        tick, phase, possession = state["Tick"], state["Phase"], state["Possession"]
        if (type(tick) is not int or not 0 <= tick <= (1 << 63) - 1 or type(phase) is not int
                or phase not in range(5) or type(possession) is not int or possession not in (0, 1)):
            raise ValueError("published")
        evidence["published"] = {"tick": tick, "phase": phase, "possession": possession}
    except (KeyError, TypeError, ValueError):
        issues.append("missing_published")
    row_path = destination / "m1-public-replay.jsonl"
    try:
        if row_path.is_symlink() or not row_path.is_file() or destination.resolve() != destination.absolute():
            raise OSError("source row unavailable")
        with row_path.open("rb") as stream:
            end = stream.seek(0, os.SEEK_END)
            start = max(0, end - (2 * 1024 * 1024 + 1))
            stream.seek(start)
            tail = stream.read(2 * 1024 * 1024 + 1)
        if not tail.endswith(b"\n"):
            raise ValueError("incomplete source row")
        if start and b"\n" not in tail[:-1]:
            issues.append("oversized_source_row")
            return _finish_m1_side_evidence(evidence)
        row_bytes = tail[:-1].rsplit(b"\n", 1)[-1] + b"\n"
        if len(row_bytes) > 2 * 1024 * 1024:
            issues.append("oversized_source_row")
            return _finish_m1_side_evidence(evidence)
        evidence["source_row_sha256"] = sha(row_bytes)
        row = json.loads(row_bytes)
        if not isinstance(row, dict):
            raise ValueError("source row object")
        if type(row.get("host")) is not int or row["host"] != failure["host"]:
            issues.append("conflicting_host")
        if (type(row.get("beforeSourceTick")) is not int or row["beforeSourceTick"] != failure["before"]
                or type(row.get("SourceTick")) is not int or row["SourceTick"] != failure["SourceTick"]
                or type(receipt.get("SourceTick")) is not int or receipt["SourceTick"] != failure["SourceTick"]):
            issues.append("conflicting_tick")
        if row.get("command") != failure.get("command"):
            issues.append("conflicting_raw_command")
        try:
            evidence["raw_stations"] = _m1_side_stations(row["command"])
        except (KeyError, TypeError, ValueError):
            issues.append("malformed_raw_stations")
        try:
            evidence["applied_stations"] = _m1_side_stations(row["applied"])
        except (KeyError, TypeError, ValueError):
            issues.append("malformed_applied_stations")
        guard = evidence["guard"]
        if guard is not None and not any(
                station["specified"] and station["action"] == 1 and station["play_specified"]
                and station["playbook"] == guard["playbook"] for station in evidence["applied_stations"]):
            issues.append("conflicting_guard_applied")
    except (OSError, RuntimeError):
        issues.append("missing_source_row")
    except (ValueError, KeyError, TypeError):
        issues.append("malformed_source_row")
    return _finish_m1_side_evidence(evidence)


def _finish_m1_side_evidence(evidence):
    issues = evidence["issues"]
    evidence["status"] = ("conflicting-evidence" if any(issue.startswith("conflicting_") for issue in issues)
                          else "missing-evidence" if issues else "captured-context")
    evidence["projection_sha256"] = sha(json.dumps(evidence, sort_keys=True, separators=(",", ":")).encode())
    return evidence


def _m1_side_diagnostic_view(value, label, fault=None):
    invalid = {"schema": _M1_SIDE_SCHEMA, "status": "missing-evidence", "issues": ["malformed_projection"]}
    keys = {"schema", "status", "run", "host", "before", "SourceTick", "receipt_sha256",
            "source_row_sha256", "replay_sha256", "profile", "guard_error_json_sha256",
            "guard", "raw_stations", "applied_stations", "current_rules", "published", "issues",
            "projection_sha256"}
    if not isinstance(value, dict) or set(value) != keys or value["schema"] != _M1_SIDE_SCHEMA:
        return invalid
    if (value["status"] not in ("captured-context", "missing-evidence", "conflicting-evidence")
            or not isinstance(value["issues"], list) or len(value["issues"]) > 32
            or any(not isinstance(issue, str) or issue not in _M1_SIDE_ISSUES for issue in value["issues"])):
        return invalid
    def scalar(item):
        return (item is None or type(item) is bool or type(item) is int and -(1 << 63) <= item < (1 << 63)
                or isinstance(item, str) and re.fullmatch(r"[A-Za-z0-9_.:/-]{1,128}", item) is not None)
    for key in keys - {"guard", "raw_stations", "applied_stations", "current_rules", "published", "issues"}:
        if not scalar(value[key]):
            return invalid
    for key in ("receipt_sha256", "source_row_sha256", "replay_sha256",
                "guard_error_json_sha256", "projection_sha256"):
        if value[key] is not None and (not isinstance(value[key], str)
                                      or re.fullmatch(r"[0-9a-f]{64}", value[key]) is None):
            return invalid
    for key in ("host", "before", "SourceTick"):
        if value[key] is not None and (type(value[key]) is not int or value[key] < 0):
            return invalid
    groups = {"guard": {"possession", "phase", "selecting_side", "playbook", "selecting_offense", "requesting_team"},
              "current_rules": {"offense_side"}, "published": {"tick", "phase", "possession"}}
    for name, fields in groups.items():
        group = value[name]
        if group is not None and (not isinstance(group, dict) or set(group) != fields
                                  or not all(scalar(item) for item in group.values())):
            return invalid
    guard = value["guard"]
    if guard is not None and (any(type(guard[key]) is not int for key in
                                 ("possession", "phase", "selecting_side", "playbook"))
                              or type(guard["selecting_offense"]) is not bool
                              or (guard["requesting_team"] is not None
                                  and type(guard["requesting_team"]) is not int)):
        return invalid
    for name in ("current_rules", "published"):
        if value[name] is not None and any(type(part) is not int for part in value[name].values()):
            return invalid
    station_fields = {"station", "specified", "action", "play_specified", "playbook", "symbol"}
    for name in ("raw_stations", "applied_stations"):
        if (not isinstance(value[name], list) or len(value[name]) > 4
                or any(not isinstance(item, dict) or set(item) != station_fields
                       or not all(scalar(part) for part in item.values()) for item in value[name])):
            return invalid
        if any(type(item["station"]) is not int or item["station"] not in range(4)
               or type(item["specified"]) is not bool or type(item["play_specified"]) is not bool
               or type(item["action"]) is not int or item["action"] not in range(22)
               or (item["playbook"] is not None
                   and (type(item["playbook"]) is not int or item["playbook"] not in (0, 1)))
               for item in value[name]):
            return invalid
    if value["status"] == "captured-context" and (value["issues"] or any(value[key] is None for key in
            ("run", "host", "before", "SourceTick", "receipt_sha256", "source_row_sha256",
             "replay_sha256", "profile", "guard_error_json_sha256", "guard", "published", "current_rules"))):
        return invalid
    core = {key: part for key, part in value.items() if key != "projection_sha256"}
    if value["projection_sha256"] != sha(json.dumps(core, sort_keys=True, separators=(",", ":")).encode()):
        return dict(invalid, status="conflicting-evidence", issues=["conflicting_projection_hash"])
    if (value["run"] != label or fault is not None and (
            any(value[key] != fault.get(key) for key in ("host", "before", "SourceTick"))
            or value["guard_error_json_sha256"] != sha(json.dumps(fault.get("error")).encode("ascii")))):
        return dict(invalid, status="conflicting-evidence", issues=["conflicting_fault_identity"])
    return value


_M1_TRAJECTORY_SCHEMA = "source-M1-current-trajectory/v1"
_M1_TRAJECTORY_MAX_HOSTS = 12000
_M1_TRAJECTORY_MAX_LINE = 16384
_M1_TRAJECTORY_MAX_BYTES = 16777216
_M1_TRAJECTORY_DIRECTORY = "safe-current-trajectory"
_M1_TRAJECTORY_ROW_PREFIX = "SOURCE_CURRENT_TRAJECTORY_ROW_V1 "
_M1_TRAJECTORY_SEAL_PREFIX = "SOURCE_CURRENT_TRAJECTORY_SEAL_V1 "
_M1_TRAJECTORY_RESULT_STATUSES = {
    "failed", "failed-portable-source-fault", "inconclusive-generation-changed", "UNMET-public-R_SKIP",
    "passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",
    "passed-portable-single-public-gate-not-full-qualification",
    "passed-strict-single-public-gate-not-full-qualification",
}
_M1_TRAJECTORY_ISSUES = {
    "missing_workflow_binding", "missing_head_binding", "conflicting_label",
    "conflicting_inventory", "conflicting_input", "conflicting_receipt",
    "missing_receipt", "missing_generated_replay", "malformed_source_row",
    "malformed_input_row", "conflicting_host", "conflicting_tick",
    "conflicting_continuity", "conflicting_packet", "malformed_projection",
    "conflicting_terminal", "capture_bound", "capture_io", "malformed_binding",
    "missing_projection", "conflicting_projection_hash", "malformed_terminal_status",
}
_M1_TRAJECTORY_PROFILES = {
    "original-teams-contact281-c2-m1": (
        C2_M1_DATA_SHA, "REAL SourceC2Composition.CreateM1Simulation", public_replay.CORE_CURRENT_CASE),
    "original-teams-contact282-c2-m1-presnap-switch": (
        PRESNAP_DATA_SHA, "REAL SourceC2Composition.CreatePreSnapSwitchSimulation", public_replay.PRESNAP_CURRENT_CASE),
}
_M1_TRAJECTORY_STATES = {
    "prePublished": {"Tick", "Phase", "Possession"},
    "preModel": {"Phase", "Possession"}, "preRules": {"OffenseSide"},
    "postAttemptPublished": {"Tick", "Phase", "Possession"},
    "postAttemptModel": {"Phase", "Possession"}, "postAttemptRules": {"OffenseSide"},
}
_M1_TRAJECTORY_EVENT_FIELDS = {"Tick", "SourceTick", "Type", "Team", "StationId"}
_M1_TRAJECTORY_ROW_FIELDS = {
    "host", "inputOneBasedLine", "inputRowSha256", "generatedRowOneBasedLine",
    "generatedRowSha256", "beforeSourceTick", "afterSourceTick",
    "rawStationMenuIds", "appliedStationMenuIds", "postAttemptSourceEventIds",
    "stepSucceededOrFaulted", *_M1_TRAJECTORY_STATES,
}
_M1_TRAJECTORY_SOURCE_FIELDS = {
    "host", "beforeSourceTick", "SourceTick", "command", "applied", "Phase",
    "PlayNumber", "Match", "Ball", "actors", "SourceEvents", "C2Events", "sounds",
    "rawCursor", "fullActualStateEventCacheHash", "gameplayPrefixHash",
    "stepSucceededOrFaulted", *_M1_TRAJECTORY_STATES,
}
_M1_TRAJECTORY_HASH_FIELDS = {
    "controllerSha256", "recipeSha256", "publicJsonlSha256", "compressedInputSha256",
    "actualReceiptSha256", "generatedReplayJsonlSha256",
}
_M1_TRAJECTORY_ATTEMPT_FIELDS = {
    "workflowRunId", "jobId", "headCommit", "headTree", "label", "observed",
    "actualReceiptProfile", "actualReceiptSourceDataSha256", "actualReceiptFactory",
    "productionInventoryCountAndSha256", "harnessInventoryCountAndSha256",
    *_M1_TRAJECTORY_HASH_FIELDS,
}


def _m1_trajectory_require(condition, issue):
    if not condition:
        raise ValueError(issue)


def _m1_trajectory_integer(value, minimum=0, maximum=(1 << 63) - 1):
    return type(value) is int and minimum <= value <= maximum


def _m1_trajectory_hash(value, length=64):
    return isinstance(value, str) and re.fullmatch("[0-9a-f]{" + str(length) + "}", value) is not None


def _m1_trajectory_json(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
                      allow_nan=False).encode("ascii")


def _m1_trajectory_fields(value, fields):
    _m1_trajectory_require(type(value) is dict and set(value) == fields, "malformed_projection")


def _m1_trajectory_events(events):
    _m1_trajectory_require(type(events) is list and len(events) <= 128, "capture_bound")
    for event in events:
        _m1_trajectory_fields(event, _M1_TRAJECTORY_EVENT_FIELDS)
        _m1_trajectory_require(
            _m1_trajectory_integer(event["Tick"]) and _m1_trajectory_integer(event["SourceTick"])
            and _m1_trajectory_integer(event["Type"], 0, 19)
            and (event["Team"] is None or _m1_trajectory_integer(event["Team"], 0, 1))
            and _m1_trajectory_integer(event["StationId"], -1, 3), "malformed_projection")
    return events


def _m1_trajectory_row(row, host, previous=None):
    _m1_trajectory_fields(row, _M1_TRAJECTORY_ROW_FIELDS)
    _m1_trajectory_require(_m1_trajectory_integer(row["host"], 0, _M1_TRAJECTORY_MAX_HOSTS - 1)
                           and row["host"] == host, "conflicting_host")
    for field in ("inputOneBasedLine", "generatedRowOneBasedLine"):
        _m1_trajectory_require(type(row[field]) is int and row[field] == host + 1, "conflicting_host")
    for field in ("inputRowSha256", "generatedRowSha256"):
        _m1_trajectory_require(_m1_trajectory_hash(row[field]), "malformed_projection")
    before, after = row["beforeSourceTick"], row["afterSourceTick"]
    _m1_trajectory_require(_m1_trajectory_integer(before) and _m1_trajectory_integer(after)
                           and after - before in (0, 1), "conflicting_tick")
    for name, fields in _M1_TRAJECTORY_STATES.items():
        state = row[name]
        _m1_trajectory_fields(state, fields)
        for field in fields:
            maximum = (1 << 63) - 1 if field == "Tick" else 4 if field == "Phase" else 1
            _m1_trajectory_require(_m1_trajectory_integer(state[field], 0, maximum),
                                   "malformed_projection")
    for name in ("rawStationMenuIds", "appliedStationMenuIds"):
        stations = row[name]
        if stations is None:
            continue
        _m1_trajectory_require(type(stations) is list and len(stations) <= 4, "capture_bound")
        seen = set()
        for station in stations:
            _m1_trajectory_fields(station, {"station", "specified", "action", "playSpecified",
                                            "playbook", "symbol"})
            _m1_trajectory_require(
                _m1_trajectory_integer(station["station"], 0, 3) and station["station"] not in seen
                and type(station["specified"]) is bool and type(station["playSpecified"]) is bool
                and _m1_trajectory_integer(station["action"], 0, 21)
                and _m1_trajectory_integer(station["playbook"], 0, 1)
                and (station["specified"] or not station["playSpecified"])
                and (isinstance(station["symbol"], str) and re.fullmatch(
                    r"[A-Za-z_][A-Za-z0-9_]{0,127}", station["symbol"]) is not None
                    if station["playSpecified"] else station["symbol"] is None), "malformed_projection")
            seen.add(station["station"])
    _m1_trajectory_events(row["postAttemptSourceEventIds"])
    _m1_trajectory_require(row["stepSucceededOrFaulted"] in ("succeeded", "faulted"), "malformed_projection")
    if previous is None:
        _m1_trajectory_require(host == 0, "conflicting_host")
    else:
        _m1_trajectory_require(previous["host"] + 1 == host
                               and previous["stepSucceededOrFaulted"] == "succeeded", "conflicting_host")
        _m1_trajectory_require(before == previous["afterSourceTick"], "conflicting_tick")
        for pre, post in (("prePublished", "postAttemptPublished"), ("preModel", "postAttemptModel"),
                          ("preRules", "postAttemptRules")):
            _m1_trajectory_require(row[pre] == previous[post], "conflicting_continuity")
    return row


def _m1_trajectory_packet(command, host, generated=False):
    if generated:
        from export_portable_public import project_row
        command = project_row({"host": host, "command": command}, host)["command"]
    else:
        public_replay.public_row({"host": host, "command": command}, host)
    menus = None
    if command["StationInputs"] is not None:
        menus = []
        for station in command["StationInputs"]["Stations"]:
            source, play = station["Command"], station["Command"]["Play"]
            menus.append({"station": station["StationId"], "specified": source["IsSpecified"],
                          "action": source["Action"], "playSpecified": play["IsSpecified"],
                          "playbook": play["Playbook"], "symbol": play["PlaySymbol"]})
    # Equality uses the existing closed command schema and typed IEEE754 float32 axes, never their payload.
    normalized = dict(command, Move={key: struct.pack(">f", float(value)).hex()
                                     for key, value in command["Move"].items()})
    return sha(_m1_trajectory_json(normalized)), menus


def _m1_trajectory_project(source_bytes, input_bytes, host, previous):
    _m1_trajectory_require(source_bytes.endswith(b"\n") and input_bytes.endswith(b"\n"),
                           "malformed_source_row")
    source, original = public_replay.decode(source_bytes), public_replay.decode(input_bytes)
    _m1_trajectory_fields(source, _M1_TRAJECTORY_SOURCE_FIELDS)
    _m1_trajectory_require(type(source["host"]) is int and source["host"] == host, "conflicting_host")
    public_replay.public_row(original, host)
    input_packet, raw = _m1_trajectory_packet(original["command"], host)
    generated_packet, generated_raw = _m1_trajectory_packet(source["command"], host, True)
    _, applied = _m1_trajectory_packet(source["applied"], host, True)
    _m1_trajectory_require(input_packet == generated_packet and raw == generated_raw, "conflicting_packet")
    events = source["SourceEvents"]
    _m1_trajectory_require(type(events) is list and len(events) <= 128, "capture_bound")
    row = {
        "host": host, "inputOneBasedLine": host + 1, "inputRowSha256": sha(input_bytes),
        "generatedRowOneBasedLine": host + 1, "generatedRowSha256": sha(source_bytes),
        "beforeSourceTick": source["beforeSourceTick"], "afterSourceTick": source["SourceTick"],
        **{name: source[name] for name in _M1_TRAJECTORY_STATES},
        "rawStationMenuIds": raw, "appliedStationMenuIds": applied,
        "postAttemptSourceEventIds": [{key: event[key] for key in _M1_TRAJECTORY_EVENT_FIELDS} for event in events],
        "stepSucceededOrFaulted": source["stepSucceededOrFaulted"],
    }
    _m1_trajectory_require(type(source["Phase"]) is int
                           and source["Phase"] == row["postAttemptPublished"]["Phase"], "conflicting_tick")
    return _m1_trajectory_row(row, host, previous)


def _m1_new_trajectory_capture():
    return {
        "schema": _M1_TRAJECTORY_SCHEMA, "status": "incomplete", "issues": [],
        "attempt": {**{field: None for field in _M1_TRAJECTORY_ATTEMPT_FIELDS},
                    "label": "uninstrumented", "observed": False},
        "terminal": {"outcome": None, "host": None, "beforeSourceTick": None,
                     "afterSourceTick": None, "errorJsonSha256": None},
        "rows": 0, "bytes": 0, "sha256": sha(b""),
    }


def _m1_trajectory_issue(capture, issue):
    issue = issue if issue in _M1_TRAJECTORY_ISSUES else "malformed_source_row"
    if issue not in capture["issues"]:
        capture["issues"].append(issue)
    capture["status"] = "incomplete"


def _m1_trajectory_regular(path):
    _m1_trajectory_require(not path.is_symlink() and path.is_file()
                           and path.resolve() == path.absolute(), "capture_io")


def _m1_trajectory_binding(capture, label, expected, production, harness):
    attempt = capture["attempt"]
    _m1_trajectory_require(label == "uninstrumented" and expected["observed"] is False, "conflicting_label")
    for field, environment, pattern in (
            ("workflowRunId", "GITHUB_RUN_ID", r"[0-9]{1,20}"),
            ("jobId", "GITHUB_JOB", r"[A-Za-z0-9_-]{1,128}")):
        value = os.environ.get(environment)
        if isinstance(value, str) and re.fullmatch(pattern, value):
            attempt[field] = value
        else:
            _m1_trajectory_issue(capture, "missing_workflow_binding")
    try:
        head = subprocess.run(["git", "rev-parse", "HEAD", "HEAD^{tree}"], cwd=ROOT,
                              text=True, capture_output=True, timeout=15, check=True).stdout.splitlines()
        _m1_trajectory_require(len(head) == 2 and all(_m1_trajectory_hash(pin, 40) for pin in head),
                               "missing_head_binding")
        attempt["headCommit"], attempt["headTree"] = head
    except (OSError, ValueError, subprocess.SubprocessError):
        _m1_trajectory_issue(capture, "missing_head_binding")
    for name, pins, count in (("production", production, 78), ("harness", harness, 12)):
        _m1_trajectory_require(type(pins) is dict and len(pins) == count
                               and all(isinstance(path, str) and _m1_trajectory_hash(pin)
                                       for path, pin in pins.items()), "conflicting_inventory")
        if name == "harness":
            _m1_trajectory_require(set(pins) == set(HARNESS), "conflicting_inventory")
        attempt[name + "InventoryCountAndSha256"] = {"count": count, "sha256": sha(_m1_trajectory_json(pins))}
    profile = expected["profile"]
    _m1_trajectory_require(profile in _M1_TRAJECTORY_PROFILES, "conflicting_receipt")
    source_data, factory, case = _M1_TRAJECTORY_PROFILES[profile]
    _m1_trajectory_require(expected["sourceData"] == source_data and expected["factory"] == factory,
                           "conflicting_receipt")
    recipe_path = HERE / "fixtures" / (case + ".recipe.json")
    public_replay.load_recipe(recipe_path, case)
    recipe_bytes = recipe_path.read_bytes()
    recipe = public_replay.decode(recipe_bytes)
    compressed = recipe_path.with_name(case + ".jsonl.gz")
    _m1_trajectory_regular(compressed)
    _m1_trajectory_require(compressed.stat().st_size <= 2 * 1024 * 1024, "capture_bound")
    compressed_hash = sha(compressed.read_bytes())
    controller = harness["tests/movement-callers/PortablePublicPolicy.cs"]
    _m1_trajectory_require(controller == recipe["controller_sha256"]
                           and compressed_hash == recipe["gzip"]["sha256"]
                           and expected["replayFileSha256"] == recipe["jsonl"]["sha256"], "conflicting_input")
    attempt.update(controllerSha256=controller, recipeSha256=sha(recipe_bytes),
                   compressedInputSha256=compressed_hash)


def _capture_m1_trajectory(destination, label, input_path, receipt_bytes, expected, production, harness):
    capture = _m1_new_trajectory_capture()
    attempt, terminal = capture["attempt"], capture["terminal"]
    candidate = destination.parent / "work/current-trajectory.jsonl"
    previous = None
    try:
        _m1_trajectory_require(destination.name == label and destination.resolve() == destination.absolute(),
                               "conflicting_label")
        _m1_trajectory_binding(capture, label, expected, production, harness)
        _m1_trajectory_require(bool(receipt_bytes), "missing_receipt")
        receipt = public_replay.decode(receipt_bytes)
        _m1_trajectory_require(type(receipt) is dict and receipt["observed"] is False
                               and receipt["historical"] is False, "conflicting_receipt")
        for key in ("profile", "sourceData", "factory", "replayFileSha256"):
            _m1_trajectory_require(type(receipt[key]) is type(expected[key])
                                   and receipt[key] == expected[key], "conflicting_receipt")
        attempt.update(actualReceiptProfile=receipt["profile"],
                       actualReceiptSourceDataSha256=receipt["sourceData"],
                       actualReceiptFactory=receipt["factory"], actualReceiptSha256=sha(receipt_bytes))
        completed = receipt["completedHosts"]
        _m1_trajectory_require(_m1_trajectory_integer(completed, 1, _M1_TRAJECTORY_MAX_HOSTS)
                               and _m1_trajectory_integer(receipt["SourceTick"]), "conflicting_terminal")
        failure = receipt["failure"]
        _m1_trajectory_require(failure is None or type(failure) is dict, "conflicting_terminal")
        terminal.update(outcome="succeeded" if failure is None else "faulted",
                        host=completed - 1, afterSourceTick=receipt["SourceTick"])
        if failure is not None:
            _m1_trajectory_require(
                _m1_trajectory_integer(failure["host"], 0, _M1_TRAJECTORY_MAX_HOSTS - 1)
                and failure["host"] == completed - 1 and _m1_trajectory_integer(failure["before"])
                and type(failure["SourceTick"]) is int and failure["SourceTick"] == receipt["SourceTick"]
                and isinstance(failure["error"], str) and bool(failure["error"]), "conflicting_terminal")
            terminal.update(beforeSourceTick=failure["before"],
                            errorJsonSha256=sha(json.dumps(failure["error"]).encode("ascii")))
        generated = destination / "m1-public-replay.jsonl"
        _m1_trajectory_require(generated.is_file(), "missing_generated_replay")
        _m1_trajectory_regular(generated)
        _m1_trajectory_regular(input_path)
        candidate.parent.mkdir(parents=True, exist_ok=True)
        input_hash, generated_hash, projection_hash = hashlib.sha256(), hashlib.sha256(), hashlib.sha256()
        with input_path.open("rb") as inputs, generated.open("rb") as source, candidate.open("xb") as projected:
            try:
                for host in range(_M1_TRAJECTORY_MAX_HOSTS + 1):
                    source_bytes = source.readline(2 * 1024 * 1024 + 1)
                    generated_hash.update(source_bytes)
                    if not source_bytes:
                        break
                    input_bytes = inputs.readline(_M1_TRAJECTORY_MAX_LINE + 1)
                    input_hash.update(input_bytes)
                    _m1_trajectory_require(host < _M1_TRAJECTORY_MAX_HOSTS
                                           and len(source_bytes) <= 2 * 1024 * 1024
                                           and len(input_bytes) <= _M1_TRAJECTORY_MAX_LINE, "capture_bound")
                    row = _m1_trajectory_project(source_bytes, input_bytes, host, previous)
                    encoded = _m1_trajectory_json(row) + b"\n"
                    _m1_trajectory_require(len(encoded) + len(_M1_TRAJECTORY_ROW_PREFIX) <= _M1_TRAJECTORY_MAX_LINE
                                           and capture["bytes"] + len(encoded)
                                           + (capture["rows"] + 1) * len(_M1_TRAJECTORY_ROW_PREFIX)
                                           <= _M1_TRAJECTORY_MAX_BYTES - _M1_TRAJECTORY_MAX_LINE
                                           - len(_M1_TRAJECTORY_SEAL_PREFIX), "capture_bound")
                    projected.write(encoded)
                    projection_hash.update(encoded)
                    capture["rows"] += 1
                    capture["bytes"] += len(encoded)
                    previous = row
            except (ValueError, KeyError, TypeError, OverflowError, struct.error) as error:
                _m1_trajectory_issue(capture, str(error))
            for stream, digest in ((inputs, input_hash), (source, generated_hash)):
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
        capture["sha256"] = projection_hash.hexdigest()
        attempt.update(publicJsonlSha256=input_hash.hexdigest(), generatedReplayJsonlSha256=generated_hash.hexdigest())
        _m1_trajectory_require(attempt["publicJsonlSha256"] == expected["replayFileSha256"], "conflicting_input")
        _m1_trajectory_require(previous is not None and capture["rows"] == completed
                               and previous["afterSourceTick"] == receipt["SourceTick"]
                               and previous["stepSucceededOrFaulted"] == terminal["outcome"], "conflicting_terminal")
        terminal["beforeSourceTick"] = previous["beforeSourceTick"]
        if failure is None:
            _m1_trajectory_require(completed == 12000 and receipt["SourceTick"] == 11400, "conflicting_terminal")
        else:
            _m1_trajectory_require(previous["host"] == failure["host"]
                                   and previous["beforeSourceTick"] == failure["before"]
                                   and previous["postAttemptPublished"] == {
                                       key: failure["actual"]["State"][key] for key in ("Tick", "Phase", "Possession")}
                                   and previous["postAttemptRules"] == {
                                       "OffenseSide": failure["actual"]["SourceRules"]["OffenseSide"]},
                                   "conflicting_terminal")
        if not capture["issues"]:
            capture["status"] = "complete"
    except (OSError, RuntimeError, ValueError, KeyError, TypeError, OverflowError) as error:
        _m1_trajectory_issue(capture, str(error) if isinstance(error, ValueError) else "capture_io")
    return capture


def _m1_trajectory_capture_view(value):
    _m1_trajectory_fields(value, {"schema", "status", "issues", "attempt", "terminal", "rows", "bytes", "sha256"})
    _m1_trajectory_require(value["schema"] == _M1_TRAJECTORY_SCHEMA
                           and value["status"] in ("complete", "incomplete")
                           and type(value["issues"]) is list and len(value["issues"]) <= 32
                           and all(type(issue) is str and issue in _M1_TRAJECTORY_ISSUES for issue in value["issues"])
                           and _m1_trajectory_integer(value["rows"], 0, _M1_TRAJECTORY_MAX_HOSTS)
                           and _m1_trajectory_integer(value["bytes"], 0, _M1_TRAJECTORY_MAX_BYTES)
                           and _m1_trajectory_hash(value["sha256"]), "malformed_binding")
    attempt, terminal = value["attempt"], value["terminal"]
    _m1_trajectory_fields(attempt, _M1_TRAJECTORY_ATTEMPT_FIELDS)
    _m1_trajectory_require(attempt["label"] == "uninstrumented" and attempt["observed"] is False,
                           "conflicting_label")
    for key in _M1_TRAJECTORY_HASH_FIELDS | {"actualReceiptSourceDataSha256", "headCommit", "headTree"}:
        _m1_trajectory_require(attempt[key] is None or _m1_trajectory_hash(
            attempt[key], 40 if key in ("headCommit", "headTree") else 64), "malformed_binding")
    for key, pattern in (("workflowRunId", r"[0-9]{1,20}"), ("jobId", r"[A-Za-z0-9_-]{1,128}")):
        _m1_trajectory_require(attempt[key] is None or isinstance(attempt[key], str)
                               and re.fullmatch(pattern, attempt[key]), "malformed_binding")
    profile = attempt["actualReceiptProfile"]
    _m1_trajectory_require(profile is None or profile in _M1_TRAJECTORY_PROFILES, "malformed_binding")
    if profile is None:
        _m1_trajectory_require(attempt["actualReceiptFactory"] is None
                               and attempt["actualReceiptSourceDataSha256"] is None, "malformed_binding")
    else:
        source, factory, _ = _M1_TRAJECTORY_PROFILES[profile]
        _m1_trajectory_require(attempt["actualReceiptFactory"] == factory
                               and attempt["actualReceiptSourceDataSha256"] == source, "malformed_binding")
    for key, count in (("productionInventoryCountAndSha256", 78), ("harnessInventoryCountAndSha256", 12)):
        group = attempt[key]
        if group is not None:
            _m1_trajectory_fields(group, {"count", "sha256"})
            _m1_trajectory_require(type(group["count"]) is int and group["count"] == count
                                   and _m1_trajectory_hash(group["sha256"]), "malformed_binding")
    _m1_trajectory_fields(terminal, {"outcome", "host", "beforeSourceTick", "afterSourceTick", "errorJsonSha256"})
    _m1_trajectory_require(terminal["outcome"] in (None, "succeeded", "faulted"), "malformed_binding")
    for key in ("host", "beforeSourceTick", "afterSourceTick"):
        _m1_trajectory_require(terminal[key] is None or _m1_trajectory_integer(
            terminal[key], 0, _M1_TRAJECTORY_MAX_HOSTS - 1 if key == "host" else (1 << 63) - 1),
            "malformed_binding")
    _m1_trajectory_require(terminal["errorJsonSha256"] is None
                           or _m1_trajectory_hash(terminal["errorJsonSha256"]), "malformed_binding")
    if value["status"] == "complete":
        _m1_trajectory_require(not value["issues"] and value["rows"] > 0
                               and all(part is not None for part in attempt.values())
                               and terminal["outcome"] is not None
                               and all(terminal[key] is not None for key in ("host", "beforeSourceTick", "afterSourceTick"))
                               and (terminal["errorJsonSha256"] is not None) == (terminal["outcome"] == "faulted"),
                               "malformed_binding")
    return value


def _write_m1_trajectory(output, report, result):
    runs = result.get("runs")
    run = runs.get("uninstrumented") if isinstance(runs, dict) else None
    if not isinstance(run, dict) or "trajectory_capture" not in run:
        return
    try:
        capture = _m1_trajectory_capture_view(run["trajectory_capture"])
        capture = json.loads(json.dumps(capture))
    except (ValueError, KeyError, TypeError):
        capture = _m1_new_trajectory_capture()
        _m1_trajectory_issue(capture, "malformed_binding")
    safe = output / _M1_TRAJECTORY_DIRECTORY
    safe.mkdir(mode=0o700, exist_ok=True)
    digest, rows, size, previous = hashlib.sha256(), 0, 0, None
    candidate = output / "work/current-trajectory.jsonl"
    with (safe / "trajectory.jsonl").open("wb") as destination:
        try:
            _m1_trajectory_regular(candidate)
            with candidate.open("rb") as source:
                for host in range(_M1_TRAJECTORY_MAX_HOSTS + 1):
                    line = source.readline(_M1_TRAJECTORY_MAX_LINE + 1)
                    if not line:
                        break
                    _m1_trajectory_require(host < _M1_TRAJECTORY_MAX_HOSTS
                                           and len(line) + len(_M1_TRAJECTORY_ROW_PREFIX) <= _M1_TRAJECTORY_MAX_LINE
                                           and line.endswith(b"\n")
                                           and size + len(line) + (rows + 1) * len(_M1_TRAJECTORY_ROW_PREFIX)
                                           <= _M1_TRAJECTORY_MAX_BYTES - _M1_TRAJECTORY_MAX_LINE
                                           - len(_M1_TRAJECTORY_SEAL_PREFIX),
                                           "capture_bound")
                    row = _m1_trajectory_row(public_replay.decode(line), host, previous)
                    _m1_trajectory_require(line == _m1_trajectory_json(row) + b"\n", "malformed_projection")
                    destination.write(line)
                    digest.update(line)
                    rows += 1
                    size += len(line)
                    previous = row
        except (OSError, RuntimeError, ValueError, KeyError, TypeError) as error:
            _m1_trajectory_issue(capture, str(error) if isinstance(error, ValueError) else "missing_projection")
    if (rows, size, digest.hexdigest()) != (capture["rows"], capture["bytes"], capture["sha256"]):
        _m1_trajectory_issue(capture, "conflicting_projection_hash")
    if capture["attempt"]["actualReceiptSha256"] != run.get("replay_receipt_sha256"):
        _m1_trajectory_issue(capture, "conflicting_receipt")
    fault = run.get("fault_metadata")
    guard = None
    if isinstance(fault, dict):
        if (capture["terminal"]["host"] != fault.get("host")
                or capture["terminal"]["beforeSourceTick"] != fault.get("before")
                or capture["terminal"]["afterSourceTick"] != fault.get("SourceTick")
                or capture["terminal"]["errorJsonSha256"] != sha(json.dumps(fault.get("error")).encode("ascii"))):
            _m1_trajectory_issue(capture, "conflicting_terminal")
        if "source_side" in fault:
            guard = _m1_side_diagnostic_view(fault["source_side"], "uninstrumented", fault)
    status = result.get("status")
    if status not in _M1_TRAJECTORY_RESULT_STATUSES:
        status = None
        _m1_trajectory_issue(capture, "malformed_terminal_status")
    envelope = {
        "schema": _M1_TRAJECTORY_SCHEMA, "status": capture["status"], "issues": capture["issues"],
        "attempt": dict(capture["attempt"], terminalReportFileSha256=sha(report.read_bytes())),
        "trajectory": {"file": "trajectory.jsonl", "rows": rows, "bytes": size, "sha256": digest.hexdigest(),
                       "firstHost": 0 if rows else None, "lastHost": previous["host"] if previous else None},
        "terminal": dict(capture["terminal"], resultStatus=status, sourceSideGuard=guard),
        "bounds": {"maxHostRows": _M1_TRAJECTORY_MAX_HOSTS, "maxStationCount": 4,
                   "maxSymbolCharacters": 128, "maxSourceEventIds": 128,
                   "maxEncodedLineBytes": _M1_TRAJECTORY_MAX_LINE, "maxProjectionBytes": _M1_TRAJECTORY_MAX_BYTES},
        "packetNormalization": "closed-public-command/ieee754-float32-be-axes/v1",
        "originalObservationAuthentication": {"Core": "unsealed-provisional", "Defensive": "separate-original-seal-required"},
    }
    envelope["sealSha256"] = sha(_m1_trajectory_json(envelope))
    encoded = _m1_trajectory_json(envelope) + b"\n"
    _m1_trajectory_require(len(encoded) + len(_M1_TRAJECTORY_SEAL_PREFIX) <= _M1_TRAJECTORY_MAX_LINE
                           and size + rows * len(_M1_TRAJECTORY_ROW_PREFIX)
                           + len(encoded) + len(_M1_TRAJECTORY_SEAL_PREFIX) <= _M1_TRAJECTORY_MAX_BYTES, "capture_bound")
    (safe / "attempt.json").write_bytes(encoded)
    with (safe / "trajectory.jsonl").open("rb") as source:
        previous = None
        for host, line in enumerate(source):
            previous = _m1_trajectory_row(public_replay.decode(line), host, previous)
            print(_M1_TRAJECTORY_ROW_PREFIX + line.decode("ascii").rstrip("\n"), file=sys.stderr)
    print(_M1_TRAJECTORY_SEAL_PREFIX + encoded.decode("ascii").rstrip("\n"), file=sys.stderr)
    print("SOURCE_CURRENT_TRAJECTORY_V1 " + _m1_trajectory_json({
        "status": envelope["status"], "issues": envelope["issues"], "rows": rows,
        "trajectorySha256": digest.hexdigest(), "attemptSha256": sha(encoded),
        "terminalReportFileSha256": envelope["attempt"]["terminalReportFileSha256"],
    }).decode("ascii"), file=sys.stderr)


def read_m1_trajectory_log(lines, expected_attempt):
    """Read only this capture's prefixed scalar records from one exact raw job log."""
    required = {"workflowRunId", "jobId", "headCommit", "headTree", "actualReceiptProfile"}
    _m1_trajectory_require(type(expected_attempt) is dict and required <= set(expected_attempt)
                           and set(expected_attempt) <= _M1_TRAJECTORY_ATTEMPT_FIELDS, "malformed_binding")
    rows, envelope, digest, size, transport_bytes = [], None, hashlib.sha256(), 0, 0
    for line in lines:
        _m1_trajectory_require(isinstance(line, str), "malformed_projection")
        message = re.sub(r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z ", "",
                         line.removeprefix("\ufeff").rstrip("\r\n"), count=1)
        prefix = next((part for part in (_M1_TRAJECTORY_ROW_PREFIX, _M1_TRAJECTORY_SEAL_PREFIX)
                       if message.startswith(part)), None)
        if prefix is None:
            continue
        encoded = message[len(prefix):].encode("ascii") + b"\n"
        transport_bytes += len(encoded) + len(prefix)
        _m1_trajectory_require(len(encoded) + len(prefix) <= _M1_TRAJECTORY_MAX_LINE
                               and transport_bytes <= _M1_TRAJECTORY_MAX_BYTES, "capture_bound")
        value = public_replay.decode(encoded)
        _m1_trajectory_require(encoded == _m1_trajectory_json(value) + b"\n", "malformed_projection")
        _m1_trajectory_require(envelope is None, "conflicting_host")
        if prefix == _M1_TRAJECTORY_ROW_PREFIX:
            row = _m1_trajectory_row(value, len(rows), rows[-1] if rows else None)
            rows.append(row)
            digest.update(encoded)
            size += len(encoded)
        else:
            envelope = value
    _m1_trajectory_require(envelope is not None, "missing_projection")
    _m1_trajectory_fields(envelope, {
        "schema", "status", "issues", "attempt", "trajectory", "terminal", "bounds",
        "packetNormalization", "originalObservationAuthentication", "sealSha256"})
    attempt, trajectory, terminal = envelope["attempt"], envelope["trajectory"], envelope["terminal"]
    _m1_trajectory_fields(attempt, _M1_TRAJECTORY_ATTEMPT_FIELDS | {"terminalReportFileSha256"})
    _m1_trajectory_fields(trajectory, {"file", "rows", "bytes", "sha256", "firstHost", "lastHost"})
    _m1_trajectory_fields(terminal, {
        "outcome", "host", "beforeSourceTick", "afterSourceTick", "errorJsonSha256", "resultStatus", "sourceSideGuard"})
    capture = {
        "schema": envelope["schema"], "status": envelope["status"], "issues": envelope["issues"],
        "attempt": {key: attempt[key] for key in _M1_TRAJECTORY_ATTEMPT_FIELDS},
        "terminal": {key: terminal[key] for key in (
            "outcome", "host", "beforeSourceTick", "afterSourceTick", "errorJsonSha256")},
        "rows": trajectory["rows"], "bytes": trajectory["bytes"], "sha256": trajectory["sha256"],
    }
    _m1_trajectory_capture_view(capture)
    _m1_trajectory_require(
        trajectory == {"file": "trajectory.jsonl", "rows": len(rows), "bytes": size, "sha256": digest.hexdigest(),
                       "firstHost": 0 if rows else None, "lastHost": rows[-1]["host"] if rows else None},
        "conflicting_projection_hash")
    _m1_trajectory_require(_m1_trajectory_hash(attempt["terminalReportFileSha256"])
                           and all(type(attempt[key]) is type(value) and attempt[key] == value
                                   for key, value in expected_attempt.items()), "malformed_binding")
    _m1_trajectory_require(envelope["bounds"] == {
        "maxHostRows": _M1_TRAJECTORY_MAX_HOSTS, "maxStationCount": 4, "maxSymbolCharacters": 128,
        "maxSourceEventIds": 128, "maxEncodedLineBytes": _M1_TRAJECTORY_MAX_LINE,
        "maxProjectionBytes": _M1_TRAJECTORY_MAX_BYTES}
        and envelope["packetNormalization"] == "closed-public-command/ieee754-float32-be-axes/v1"
        and envelope["originalObservationAuthentication"] == {
            "Core": "unsealed-provisional", "Defensive": "separate-original-seal-required"}
        and (terminal["resultStatus"] is None or terminal["resultStatus"] in _M1_TRAJECTORY_RESULT_STATUSES),
        "malformed_binding")
    guard = terminal["sourceSideGuard"]
    if guard is not None:
        _m1_trajectory_require(type(guard) is dict, "malformed_binding")
        if set(guard) == {"schema", "status", "issues"}:
            _m1_trajectory_require(guard["schema"] == _M1_SIDE_SCHEMA
                                   and guard["status"] in ("missing-evidence", "conflicting-evidence")
                                   and type(guard["issues"]) is list and len(guard["issues"]) <= 32
                                   and all(type(issue) is str and issue in _M1_SIDE_ISSUES
                                           for issue in guard["issues"]), "malformed_binding")
        else:
            _m1_trajectory_require(_m1_side_diagnostic_view(guard, "uninstrumented") == guard, "malformed_binding")
            _m1_trajectory_require(guard["host"] == terminal["host"]
                                   and guard["before"] == terminal["beforeSourceTick"]
                                   and guard["SourceTick"] == terminal["afterSourceTick"]
                                   and guard["receipt_sha256"] == attempt["actualReceiptSha256"]
                                   and guard["guard_error_json_sha256"] == terminal["errorJsonSha256"],
                                   "conflicting_terminal")
            if guard["status"] == "captured-context":
                _m1_trajectory_require(bool(rows) and guard["source_row_sha256"] == rows[-1]["generatedRowSha256"]
                                       and guard["profile"] == attempt["actualReceiptProfile"]
                                       and guard["replay_sha256"] == attempt["publicJsonlSha256"],
                                       "conflicting_terminal")
    if envelope["status"] == "complete":
        _m1_trajectory_require(terminal["resultStatus"] is not None and rows[-1]["host"] == terminal["host"]
                               and rows[-1]["beforeSourceTick"] == terminal["beforeSourceTick"]
                               and rows[-1]["afterSourceTick"] == terminal["afterSourceTick"]
                               and rows[-1]["stepSucceededOrFaulted"] == terminal["outcome"],
                               "conflicting_terminal")
    core = {key: value for key, value in envelope.items() if key != "sealSha256"}
    _m1_trajectory_require(envelope["sealSha256"] == sha(_m1_trajectory_json(core)), "conflicting_projection_hash")
    return rows, envelope


def first_m1_trajectory_difference(current_rows, original_rows):
    """Compare already projected rows; this helper does not authenticate an original parent seal."""
    previous, original_previous, first = None, None, None
    current, original = iter(current_rows), iter(original_rows)
    host = 0
    while True:
        row = next(current, None)
        author = next(original, None)
        if row is None and author is None:
            return first
        _m1_trajectory_require(row is not None and author is not None, "conflicting_host")
        _m1_trajectory_row(row, host, previous)
        _m1_trajectory_fields(author, {"host", "beforeSourceTick", "SourceTick", "visible", "sourceEventIds"})
        _m1_trajectory_require(type(author["host"]) is int and author["host"] == host, "conflicting_host")
        visible = author["visible"]
        _m1_trajectory_fields(visible, {"Phase", "Possession"})
        _m1_trajectory_require(_m1_trajectory_integer(visible["Phase"], 0, 4)
                               and _m1_trajectory_integer(visible["Possession"], 0, 1), "malformed_projection")
        _m1_trajectory_events(author["sourceEventIds"])
        before, after = author["beforeSourceTick"], author["SourceTick"]
        _m1_trajectory_require(_m1_trajectory_integer(before) and _m1_trajectory_integer(after)
                               and after - before in (0, 1)
                               and (original_previous is None or before == original_previous["SourceTick"]),
                               "conflicting_tick")
        pairs = [("beforeSourceTick", row["beforeSourceTick"], before),
                 ("SourceTick", row["afterSourceTick"], after)]
        pairs += [("prePublished." + field, row["prePublished"][field], visible[field])
                  for field in ("Phase", "Possession")]
        for field, actual, expected in pairs:
            if first is None and actual != expected:
                first = {
                    "host": host, "field": field, "current": actual, "original": expected,
                    "predecessor": None if previous is None else {
                        "host": previous["host"],
                        "currentSourceEventIds": previous["postAttemptSourceEventIds"],
                        "originalSourceEventIds": original_previous["sourceEventIds"]},
                    "currentPostAttemptSourceEventIds": row["postAttemptSourceEventIds"],
                    "originalPostAttemptSourceEventIds": author["sourceEventIds"],
                    "originalAuthentication": "requires-separate-parent-seal; Core-remains-provisional",
                }
        previous, original_previous = row, author
        host += 1


def _write_m1_fault_diagnostics(path, result):
    production = result.get("production_before")
    valid_production = (isinstance(production, dict) and bool(production) and all(
        isinstance(name, str) and bool(name) and isinstance(pin, str) and len(pin) == 64
        and all(c in "0123456789abcdef" for c in pin) for name, pin in production.items()))
    binding = {
        "schema": "source-M1-fault-diagnostic/v1",
        "report_sha256": sha(path.read_bytes()),
        "production_manifest_sha256": sha(json.dumps(
            production, sort_keys=True, separators=(",", ":")).encode()) if valid_production else None,
    }
    runs = result.get("runs")
    faults = [(label, run["fault_metadata"]) for label, run in runs.items()
              if isinstance(run, dict) and "fault_metadata" in run] if isinstance(runs, dict) else []

    for label, fault in (faults or [(None, None)])[:8]:
        diagnostic = dict(binding)
        missing = [] if valid_production else ["production_before"]
        if not isinstance(fault, dict):
            missing.append("fault_metadata")
            fault = {}
        for name in ("host", "before", "SourceTick"):
            value = fault.get(name)
            if type(value) is int and 0 <= value <= (1 << 63) - 1:
                diagnostic[name] = value
            else:
                missing.append(name)
        for name, value, limit in (("run", label, 128), ("error", fault.get("error"), 8192)):
            if not isinstance(value, str) or not value.strip():
                missing.append("run_label" if name == "run" else name)
                continue
            diagnostic[name + "_json_sha256"] = sha(json.dumps(value).encode("ascii"))
            diagnostic[name + "_chars"] = len(value)
            diagnostic[name + "_truncated"] = len(value) > limit
            if len(value) > limit:
                marker = "\n... [truncated] ...\n"
                first = (limit - len(marker)) * 3 // 4
                last = limit - len(marker) - first
                value = value[:first] + marker + value[-last:]
            diagnostic[name] = value
        diagnostic["status"] = "missing-evidence" if missing else "captured-fault"
        if missing:
            diagnostic["missing_fields"] = missing
        if "source_side" in fault:
            diagnostic["source_side"] = _m1_side_diagnostic_view(fault["source_side"], label, fault)
        print(json.dumps(diagnostic, sort_keys=True), file=sys.stderr)
    if len(faults) > 8:
        print(json.dumps(dict(binding, status="missing-evidence",
                              missing_fields=["additional_fault_metadata_runs"],
                              omitted_fault_runs=len(faults) - 8), sort_keys=True), file=sys.stderr)


def _write_m1_result(output, result):
    path = output / "result.json"
    path.write_text(json.dumps(result, indent=2) + "\n")
    status = result["status"]
    if status in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",
                  "passed-portable-single-public-gate-not-full-qualification",
                  "passed-strict-single-public-gate-not-full-qualification"):
        print("M1", status)
        print("M1 report:", path)
    else:
        print("M1 REGRESSION FAILED:", status, file=sys.stderr)
        if status == "UNMET-public-R_SKIP":
            print("Unmet required assertion:", M1_COVERAGE_ASSERTION, file=sys.stderr)
        print("M1 report:", path, file=sys.stderr)
        if status == "failed-portable-source-fault":
            _write_m1_fault_diagnostics(path, result)
    try:
        _write_m1_trajectory(output, path, result)
    except (OSError, RuntimeError, ValueError, KeyError, TypeError):
        print('SOURCE_CURRENT_TRAJECTORY_V1 {"status":"incomplete","issues":["capture_io"]}', file=sys.stderr)


def post_vm_output(output, presnap_switch=False):
    prefix = "presnap-post-VM-" if presnap_switch else "m1-post-VM-"
    return HERE / ".artifacts" / (prefix + sha(str(Path(output).absolute()).encode())[:16])


def author_presnap_main(argv):
    """One explicitly frozen authoring window; no search, retries or fallback factory."""
    parser = argparse.ArgumentParser(description=author_presnap_main.__doc__)
    parser.add_argument("--author-presnap-switch",action="store_true",required=True)
    for name in ("source-root","fixture-manifest","sound-functions","c2-presnap-switch-inputs","policy","output"):
        parser.add_argument("--"+name,type=Path,required=True)
    args=parser.parse_args(argv)
    output=args.output.absolute()
    if output.exists() or output.resolve()!=output or not output.is_relative_to(ROOT/".squad/.scratch"):
        parser.error("Choose a new ignored authoring output; no prior capture may be overwritten.")
    marker=args.policy.absolute().parent/"only-public-window-started.json"
    if marker.exists():
        parser.error("The single create-new authoring marker already exists; never execute another gameplay window.")
    fixture_profile(args.fixture_manifest,"contact272")
    inputs=fixture_hashes(args.fixture_manifest,args.sound_functions)
    inputs.update(successor_hashes(args.c2_presnap_switch_inputs))
    policy=json.loads(args.policy.read_bytes())
    production={p.relative_to(ROOT).as_posix():p.read_bytes() for p in production_paths()}
    harness={name:(ROOT/name).read_bytes() for name in HARNESS}
    for name,raw in {**production,**harness}.items():
        if policy["frozenCodeSha256"].get(name)!=sha(raw):
            raise ValueError("Frozen authoring compile closure changed: "+name)
    if any(policy["frozenInputSha256"].get(name)!=pin for name,pin in inputs.items()):
        raise ValueError("Frozen authoring input-generation closure changed.")
    output.mkdir(parents=True)
    copy=output/"compile"
    for folder,files in (("source",production),("harness",harness)):
        for name,raw in files.items():
            target=copy/folder/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw)
    work=output/"work";work.mkdir();(work/"empty-feed").mkdir()
    env=dict(os.environ,TMPDIR=str(work),TMP=str(work),TEMP=str(work),DOTNET_CLI_TELEMETRY_OPTOUT="1",
             DOTNET_NOLOGO="1",MSBUILDDISABLENODEREUSE="1",PYTHONDONTWRITEBYTECODE="1")
    result=dict(schema="defensive-presnap-switch-one-authoring-window/v1",status="failed-before-window",
                maximumInitialWindows=1,commands=[],policySha256=sha(args.policy.read_bytes()))
    def execute(command,label,timeout=240):
        start=time.monotonic()
        process=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=timeout)
        text=process.stdout+process.stderr;(output/(label+".log")).write_text(text)
        result["commands"].append(dict(command=command,exit=process.returncode,seconds=round(time.monotonic()-start,3),
            log=label+".log",diagnostics=[line for line in text.splitlines()
                if " error CS" in line or " warning CS" in line or "error NETSDK" in line]))
        return process.returncode,text
    code=1
    try:
        project=copy/"harness/tests/movement-callers/MovementCallerChecks.csproj"
        build=output/"build"
        common=["-p:BlitzSourceRoot="+str(copy/"source")+"/","-p:BaseIntermediateOutputPath="+str(build/"obj")+"/",
                "-p:BaseOutputPath="+str(build/"bin")+"/","-p:UseSharedCompilation=false"]
        command=["dotnet","build",str(project),"--no-restore","-c","Release","-v:q","--nologo",*common]
        status,text=execute(command,"compile")
        if status and "NETSDK1004" in text:
            status,_=execute(["dotnet","restore",str(project),"--source",str(work/"empty-feed"),"-v:q",
                              "-p:NuGetAudit=false",*common],"restore-after-missing-assets")
            if status:raise RuntimeError("Missing-assets restore failed before authoring.")
            status,text=execute(command,"compile-restored")
        if status or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Authoring requires a fresh zero-diagnostic actual-source compile.")
        command=["dotnet",str(build/"bin/Release/net10.0/MovementCallerChecks.dll"),
                 str(args.fixture_manifest.absolute()),str(output/"capture"),"presnap-public-policy",
                 str(args.sound_functions.absolute()),str(args.c2_presnap_switch_inputs.absolute()),
                 str(args.policy.absolute()),"--source-compatibility","portable"]
        code,text=execute(command,"one-public-window",900)
        result["status"]="captured-full-qualification-required" if code==0 else "STOP-source-fault-or-unmet-one-window"
        receipt=output/"capture/public-window-result.json"
        if receipt.exists():
            result["receipt"]=json.loads(receipt.read_bytes())
            result["receipt_sha256"]=sha(receipt.read_bytes())
        result["marker_exists"]=marker.is_file()
        if not result["marker_exists"]:raise RuntimeError("No create-new one-window marker was persisted.")
    except (OSError,ValueError,RuntimeError,subprocess.SubprocessError) as error:
        result["error"]=str(error);code=1
    finally:
        result["production_unchanged"]={n:sha((ROOT/n).read_bytes()) for n in production}=={n:sha(b) for n,b in production.items()}
        result["harness_unchanged"]=all((ROOT/n).read_bytes()==b for n,b in harness.items())
        if not result["production_unchanged"] or not result["harness_unchanged"]:code=1
        shutil.rmtree(copy)
        (output/"authoring-result.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(dict(status=result["status"],exit=code,report=str(output/"authoring-result.json"))))
    return code


def m1_main(argv):
    parser = argparse.ArgumentParser(description="Full actual-factory M1 successor fixed-vector gate, never old scoped acceptance.")
    for name in ("fixture-manifest", "sound-functions", "source-root", "replay-commands", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    generations = parser.add_mutually_exclusive_group(required=True)
    generations.add_argument("--c2-m1-inputs",type=Path)
    generations.add_argument("--c2-presnap-switch-inputs",type=Path)
    parser.add_argument("--input-profile", choices=("contact272",), default="contact272")
    parser.add_argument("--source-compatibility", action="append", metavar="{strict,portable}")
    parser.add_argument("--single-public-attempt", action="store_true",
                        help="Run one unchanged vector only; never claim the complete compiled-control closure.")
    args = parser.parse_args(argv)
    presnap_switch = args.c2_presnap_switch_inputs is not None
    profile_inputs = args.c2_presnap_switch_inputs if presnap_switch else args.c2_m1_inputs
    animation_sha = PRESNAP_ANIMATION_SHA if presnap_switch else C2_ANIMATION_SHA
    modes = args.source_compatibility or ["strict"]
    if len(modes) != 1:
        parser.error("argument --source-compatibility: duplicate argument; specify only once")
    if modes[0] not in ("strict", "portable"):
        parser.error("argument --source-compatibility: invalid choice: " + repr(modes[0])
                     + " (choose from 'strict', 'portable')")
    args.source_compatibility = modes[0]
    output = args.output.absolute()
    if output.exists() or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch")):
        parser.error("Choose a NEW ignored M1 output, not an existing/frozen receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(successor_hashes(profile_inputs) if presnap_switch else canonical_hashes(profile_inputs, "contact281-c2-m1"))
    inputs[str(args.replay_commands.absolute())] = sha(args.replay_commands.read_bytes())
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {name: sha(data) for name, data in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    observed_source = inject_current(production[CACHE_SOURCE])
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
               DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_EnableDiagnostics="0",
               DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
               DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"))
    controls_spec = importlib.util.spec_from_file_location("existing_M1_immutable_controls", HERE / "run.py")
    controls = importlib.util.module_from_spec(controls_spec)
    controls_spec.loader.exec_module(controls)
    result = {"schema": "source-C2-M1-complete-actual-factory-replay/v1", "status": "failed",
              "whole_M1_or_game_or_native_accepted": False, "commands": [], "runs": {},
              "inputs_before": inputs, "production_before": pins,
              "harness_sha256": {k: sha(v) for k, v in harness.items()},
              "original8_controls_retained": True, "original3_cache_controls_retained": True,
              "no_new_policy_seed_vector_clock_pointer_or_live_source_changes": True}
    result["gameplay_compatibility"] = args.source_compatibility
    result["no_new_policy_seed_vector_clock_pointer_or_live_source_changes"] = args.source_compatibility == "strict"
    result["public_controller_policy_seed_vector_clock_unchanged"] = True
    result["portable_is_deliberate_correction_not_original_hardware"] = args.source_compatibility == "portable"
    result["complete_compiled_control_closure_executed"] = False
    counter_lock = Lock()

    def execute(label, observed=False, mutation=None, section=None):
        copy = output / "copies" / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, data in entries.items():
                if base == source and observed and name == CACHE_SOURCE:
                    data = observed_source
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
        if mutation:
            identity, area, relative, old, new, expected = mutation
            target = (source if area == "production" else tests) / relative
            text = target.read_text()
            if text.count(old) != 1:
                raise RuntimeError("M1 mutation anchor must remain unique: " + identity)
            target.write_text(text.replace(old, new))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = output / "build" / label
        scratch = build / "tmp"
        scratch.mkdir(parents=True)
        execute_env = dict(env, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
        common = ["-p:BlitzSourceRoot=" + str(source) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", *common]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=240)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            restore = ["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                       "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=240)
            if restored.returncode:
                raise RuntimeError("M1 isolated restore failed after missing SDK assets.")
            compiled = subprocess.run(command, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=240)
        text = compiled.stdout + compiled.stderr
        (output / (label + "-compile.log")).write_text(text)
        if compiled.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("M1 causal controls require zero-diagnostic compilation: " + label)
        destination = output / label
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(destination),
                   section or (("presnap-complete-observed" if observed else "presnap-complete") if presnap_switch
                               else "m1-complete-observed" if observed else "m1-complete"),
                   str(args.sound_functions.absolute())]
        command += ["--c2-presnap-switch-inputs" if presnap_switch else "--c2-m1-inputs",
                    str(profile_inputs.absolute())] if section else [
            str(profile_inputs.absolute()), str(args.replay_commands.absolute())]
        command += ["--source-compatibility", args.source_compatibility]
        result["commands"].append(command)
        if section is None:
            with counter_lock:
                result["public_vector_executions"] = result.get("public_vector_executions", 0) + 1
        process = subprocess.run(command, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=900)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = rows[-1] if len(rows) == 1 else None
        run = {"exit_code": process.returncode, "summary": summary, "compiled": True, "zero_diagnostics": True}
        result["runs"][label] = run
        if mutation:
            if (process.returncode != 1 or summary is None or summary.get("status") != "failed"
                    or expected not in summary.get("failed_assertion", "") or summary.get("assertions", 0) <= 0):
                raise RuntimeError("M1 compiled control must reject its own named causal assertion: " + identity)
            run["named_causal_failure"] = summary["failed_assertion"]
        elif section:
            if section == "controlled":
                controls.require_summary(process.returncode, summary, "controlled", animation_sha)
            elif (process.returncode != 0 or summary.get("status") != "passed"
                  or summary.get("actual_Core_post_VM_physics_cases") != 664):
                raise RuntimeError("Retained M1 caller/physics inventory failed.")
        else:
            receipt_path = destination / "m1-replay-result.json"
            receipt_bytes = receipt_path.read_bytes() if receipt_path.is_file() else b""
            expected_receipt = {
                "observed": observed,
                "profile": ("original-teams-contact282-c2-m1-presnap-switch" if presnap_switch
                            else "original-teams-contact281-c2-m1"),
                "sourceData": PRESNAP_DATA_SHA if presnap_switch else C2_M1_DATA_SHA,
                "factory": ("REAL SourceC2Composition.CreatePreSnapSwitchSimulation" if presnap_switch
                            else "REAL SourceC2Composition.CreateM1Simulation"),
                "replayFileSha256": inputs[str(args.replay_commands.absolute())],
            }
            if label == "uninstrumented":
                run["trajectory_capture"] = _capture_m1_trajectory(
                    destination, label, args.replay_commands.absolute(), receipt_bytes, expected_receipt,
                    pins, result["harness_sha256"])
            receipt = json.loads(receipt_bytes)
            run["replay_receipt_sha256"] = sha(receipt_bytes)
            run["receipt_metadata"] = {key: receipt.get(key) for key in (
                "completedHosts", "SourceTick", "gameplayCompatibility", "compatibilityRepairCount",
                "compatibilityRepairs", "rawHandicap", "effectiveHandicap", "rSkipFrames", "positive")}
            if receipt.get("failure"):
                failure = receipt["failure"]
                run["fault_metadata"] = {key: failure.get(key) for key in ("host", "before", "SourceTick", "error")}
                run["fault_metadata"]["source_side"] = _m1_side_evidence(
                    destination, label, receipt_bytes, expected_receipt)
                if args.source_compatibility == "portable":
                    result["status"] = "failed-portable-source-fault"
            run["classification"] = require_m1_replay(process.returncode, summary, receipt, args.source_compatibility, presnap_switch)
            run["receipt"] = receipt
        shutil.rmtree(copy)
        return run

    try:
        plain = execute("uninstrumented")
        if args.single_public_attempt:
            result["status"] = ("passed-portable-single-public-gate-not-full-qualification"
                if args.source_compatibility == "portable" else "passed-strict-single-public-gate-not-full-qualification"
                ) if plain["classification"] == "positive" else "UNMET-public-R_SKIP"
            result["M1_public_caller_gate_qualified"] = plain["classification"] == "positive"
            result["public_vector_executions"] = 1
        else:
            observed = execute("isolated-observer", True)
            for name in ("m1-public-replay.jsonl", "m1-all-raw-draws.json", "m1-R_SKIP-real-poses.json"):
                a, b = output / "uninstrumented" / name, output / "isolated-observer" / name
                if a.read_bytes() != b.read_bytes():
                    raise RuntimeError("Passive observer changed actual recorded state/input/pose/RNG: " + name)
            result["uninstrumented_and_actual_observer_state_RNG_and_poses_byte_identical"] = True
            run_mutations(CANONICAL_CACHE_MUTANTS,
                          lambda spec: execute("cache-control-" + spec[0], True, spec))
            execute("controlled-baseline", section="controlled")
            def caller_control(spec):
                identity, filename, old, new, expected = spec
                return execute("caller-control-" + identity, mutation=(identity, "production",
                    "Assets/Scripts/Gameplay/" + filename, old, new, expected), section="controlled")
            run_mutations(controls.MUTANTS, caller_control)
            execute("controlled-final", section="controlled")
            execute("existing-caller-physics", section="campaigns")
            native_output = post_vm_output(output, presnap_switch)
            result["original_C_post_VM"] = compare(args.source_root,
                output / "existing-caller-physics/actual-caller-physics.tsv", native_output)
            final = execute("final-uninstrumented")
            if (output / "final-uninstrumented/m1-public-replay.jsonl").read_bytes() != \
                    (output / "uninstrumented/m1-public-replay.jsonl").read_bytes():
                raise RuntimeError("Final actual-factory source baseline changed after causal controls.")
            result["all3_cache_and8_caller_controls_compiled_and_causal"] = True
            result["complete_compiled_control_closure_executed"] = True
            result["status"] = ("passed-portable-M1-public-caller-gate" if args.source_compatibility == "portable"
                else "passed-full-M1-public-caller-gate") if plain["classification"] == "positive" else "UNMET-public-R_SKIP"
            result["whole_M1_or_game_or_native_accepted"] = False
            result["M1_public_caller_gate_qualified"] = plain["classification"] == "positive"
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        copies = output / "copies"
        if copies.exists():
            shutil.rmtree(copies)
        result["production_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        result["live_production_unchanged"] = result["production_after"] == pins
        if not result["live_production_unchanged"]:
            result["status"] = "inconclusive-generation-changed"
        _write_m1_result(output, result)
    return 0 if result["status"] in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",
        "passed-portable-single-public-gate-not-full-qualification", "passed-strict-single-public-gate-not-full-qualification") else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--sound-functions", type=Path, required=True)
    parser.add_argument("--c2-inputs", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, required=True, help="Pinned original player.c for all664 post-VM cases.")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if (not output.is_relative_to(ROOT) or output.exists()
            or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch"))):
        parser.error("Use a new explicitly ignored local output, never an existing owner receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_inputs))
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {name: sha(data) for name, data in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    observed_source = inject_current(production[CACHE_SOURCE])
    workspace = output / "isolated"
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
                       DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
                       DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"))
    result = {"schema": "source-canonical281-cache-observation-run/v1", "status": "failed",
              "production_sha256_before": pins,
              "harness_sha256": {name: sha(data) for name, data in harness.items()},
              "inputs_sha256_before": inputs, "commands": [], "runs": {},
              "instrumentation": {
                  "original_source": CACHE_SOURCE, "original_sha256": CACHE_SOURCE_SHA,
                  "observed_sha256": sha(observed_source), "unique_call_site": True,
                  "removing_only_insertion_restores_original_bytes": True,
                  "additional_geometry_calls": 0, "replacement_geometry": False,
                  "model_RNG_order_return_or_live_assembly_mutation": False},
              "full_M1_or_game_or_native_acceptance": False}
    controls_spec = importlib.util.spec_from_file_location("canonical_M1_existing_controls", HERE / "run.py")
    controls = importlib.util.module_from_spec(controls_spec)
    controls_spec.loader.exec_module(controls)

    def execute(label, instrumented, mutation=None, section=None):
        copy = workspace / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, content in entries.items():
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)
        if instrumented:
            (source / CACHE_SOURCE).write_bytes(observed_source)
        if mutation is not None:
            identity, kind, relative, old, new, expected = mutation
            target = (source if kind == "production" else tests) / relative
            original = target.read_bytes()
            if original.count(old.encode()) != 1:
                raise RuntimeError("Canonical cache control requires one actual unique anchor: " + identity)
            target.write_bytes(original.replace(old.encode(), new.encode()))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = copy / "build"
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", "-p:BlitzSourceRoot=" + str(source) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        (output / (label + "-compile.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            feed = work / "empty-feed"
            feed.mkdir(exist_ok=True)
            restore = ["dotnet", "restore", str(project), "--source", str(feed), "--verbosity", "quiet",
                       "-p:NuGetAudit=false", "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/"]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-restore-after-missing-assets.log")).write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("Existing SDK local restore failed after missing assets: " + label)
            compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-compile-after-restore.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode:
            raise RuntimeError("Canonical harness must compile; a compilation failure is NOT a production failure: " + label)
        mode = section or ("canonical281-observed" if instrumented else "canonical281")
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(output / label), mode,
                   str(args.sound_functions.absolute())]
        command += [str(args.c2_inputs.absolute())] if section is None else ["--c2-inputs", str(args.c2_inputs.absolute())]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=600)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        actual = rows[-1] if len(rows) == 1 else None
        result["runs"][label] = {"exit_code": process.returncode, "summary": actual,
                                 "instrumented": instrumented}
        if mutation is not None:
            if (process.returncode != 1 or actual is None or actual.get("status") != "failed"
                    or actual.get("assertions", 0) <= 0 or expected not in actual.get("failed_assertion", "")):
                raise RuntimeError("Compiled canonical cache control did not reject its named causal field: " + identity)
            result["runs"][label].update(compiled=True, named_causal_rejection=True,
                                         expected_assertion=expected, mutation_source=relative,
                                         mutated_sha256=sha(target.read_bytes()))
            return actual
        failure = output / label / "first-canonical-software-failure.json"
        if section is not None:
            failure = output / label / "first-current-source-failure.json"
        if failure.exists():
            result["first_software_failure"] = {"path": str(failure), "sha256": sha(failure.read_bytes())}
            raise RuntimeError("Canonical real-composition stream stopped at a genuine software boundary: " + str(failure))
        if section is not None:
            if section == "controlled":
                from kernel_sources import C2_ANIMATION_SHA
                controls.require_summary(process.returncode, actual, "controlled", C2_ANIMATION_SHA)
            elif (process.returncode != 0 or actual is None or actual.get("status") != "passed"
                  or actual.get("actual_Core_post_VM_physics_cases") != 664
                  or actual.get("actual_Hurdle_VM_scripts") != 16
                  or actual.get("actual_MaybeDive_windows_per_hurdle") != 7
                  or actual.get("complete_publication_qualification") is not False):
                raise RuntimeError("Every existing canonical M1 caller/gesture/physics case group must pass: " + section)
            return actual
        if (process.returncode or actual is None or actual.get("status") != "canonical-entry-window-only"
                or actual.get("full_M1_qualified") is not False):
            raise RuntimeError("Canonical entry compilation/checks did not produce qualified scoped evidence: " + label)
        receipt = json.loads((output / label / "canonical-entry.json").read_bytes())
        if (receipt["completedHosts"] != 12000 or receipt["observed"] != instrumented
                or receipt["factory"] != "REAL SourceC2Composition" or receipt["fullM1OrGameOrNativeAccepted"] is not False):
            raise RuntimeError("Canonical entry/factory/window inventory differs: " + label)
        result["runs"][label]["receipt_sha256"] = sha((output / label / "canonical-entry.json").read_bytes())
        return receipt

    exit_code = 1
    try:
        untouched = execute("uninstrumented", False)
        observed = execute("isolated-observer", True)
        first = (output / "uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        second = (output / "isolated-observer/canonical-observable-stream.jsonl").read_bytes()
        if first != second or untouched["allSharedRawDraws"] != observed["allSharedRawDraws"]:
            raise RuntimeError("Isolated witness changed actual state/events/cache/RNG; it cannot qualify untouched production.")
        result["byte_identical_full_observable_stream_and_all_raw_draws"] = True
        result["compared_host_records"] = len(first.splitlines())
        result["stream_sha256"] = sha(first)
        for spec in CANONICAL_CACHE_MUTANTS:
            execute("control-" + spec[0], True, spec)
        final = execute("final-uninstrumented", False)
        final_stream = (output / "final-uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        if final_stream != first or final["allSharedRawDraws"] != untouched["allSharedRawDraws"]:
            raise RuntimeError("Final untouched real-composition baseline differs after isolated cache controls.")
        result["canonical_compiled_cache_controls"] = len(CANONICAL_CACHE_MUTANTS)
        result["historical_source474_three_controls_preserved_separately"] = True
        result["final_uninstrumented_baseline_identical"] = True
        execute("controlled-baseline", False, section="controlled")
        campaign = execute("existing-M1-campaigns", False, section="campaigns")
        result["original_C_post_VM"] = compare(args.source_root,
            output / "existing-M1-campaigns/actual-caller-physics.tsv", output / "original-C-post-VM")
        for identity, filename, old, new, expected in controls.MUTANTS:
            spec = (identity, "production", "Assets/Scripts/Gameplay/" + filename, old, new, expected)
            execute("M1-control-" + identity, False, spec, section="controlled")
        execute("controlled-final", False, section="controlled")
        final_campaign = execute("final-existing-M1-campaigns", False, section="campaigns")
        if final_campaign.get("assertions") != campaign.get("assertions"):
            raise RuntimeError("Canonical caller campaign assertions changed after isolated original8 controls.")
        result["original8_M1_controls_compiled_and_causal"] = 8
        result["all_existing_M1_case_groups_executed"] = True
        result["old36948_full_M1_gate_not_relabeled_as_canonical"] = True
        result["canonical_R_SKIP_coverage"] = untouched["rSkipFrames"]
        if untouched["rSkipFrames"] == 0:
            result["stronger_full_M1_coverage_blocked"] = "Unchanged unfiltered canonical12000 window does not execute the prior R_SKIP advancement assertions; no vector/filter/assertion change authorized."
        result["status"] = "passed-scoped-entry-and-cache-only"
        exit_code = 0
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        result["own_copies_removed"] = not workspace.exists()
        result["production_sha256_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        after_inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after_inputs.update(canonical_hashes(args.c2_inputs))
        result["inputs_sha256_after"] = after_inputs
        result["production_and_inputs_unchanged"] = pins == result["production_sha256_after"] and inputs == after_inputs
        if not result["production_and_inputs_unchanged"]:
            result["status"] = "inconclusive-generation-changed"
            exit_code = 1
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(result["status"])
    return exit_code


if __name__ == "__main__":
    raise SystemExit(snap_hike_main(sys.argv[1:]) if "--snap-hike-only" in sys.argv else
                     m1_main(sys.argv[1:]) if "--c2-m1-inputs" in sys.argv else main())
