#!/usr/bin/env python3
"""Single-use, parent-released current-source qualification; never a Game/Unity run."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import selectors
import signal
import subprocess
import sys
import time


WT = Path("/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f")
X = ".squad/.scratch/source-following-active-p1/metadata/source-kickoff-child-source-01"
Y = X + "/build-validation-01/refresh-01"
RELEASE = X + "/parent-build-execution-release-01.json"
OUTPUT = "tests/source-kickoff-instruction-child/validation-output-child-01"
DISPATCHER = "Assets/Scripts/Gameplay/SourceInstructionProcess.cs"
GAME = "Assets/Scripts/Gameplay/GameSimulation.Instructions.cs"
FROZEN_MARKER = b"        private sealed class InstructionProcess\n"
FROZEN_SHA = "a4b2e470a710ed4d8986ad71c1a54636b9ebc892edf8137e45776abd3af01d52"
FRAMEWORK = {"name": "Microsoft.NETCore.App", "version": "10.0.0", "rollForward": "Disable"}
SCOPES = ["child18", "prior16-current-source", "compiled-mutants29", "core89-library-only", "STATIC_NOT_GAME"]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def compact(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode("ascii")


def unique_object(pairs):
    value = {}
    for key, item in pairs:
        require(key not in value, "duplicate JSON key: " + key)
        value[key] = item
    return value


def document(raw):
    return json.loads(raw, object_pairs_hook=unique_object)


def local(name):
    path = Path(name)
    require(not path.is_absolute() and ".." not in path.parts and str(path) == name, "unsafe local path")
    full = WT / path
    require(full.resolve() == full, "symlink/escaping local path: " + name)
    return full


def read_row(row):
    path, size, pin = row
    full = Path(path) if Path(path).is_absolute() else local(path)
    require(type(size) is int and size > 0 and re.fullmatch("[0-9a-f]{64}", pin), "invalid pin row")
    raw = full.read_bytes()
    require(len(raw) == size and digest(raw) == pin, "pin drift: " + path)
    return raw


def pinned_document(name, pin):
    raw = local(name).read_bytes()
    require(len(raw) == pin["bytes"] and digest(raw) == pin["sha256"], "metadata pin drift: " + name)
    return document(raw)


def suffix(raw):
    require(raw.count(FROZEN_MARKER) == 1, "ambiguous frozen boundary")
    frozen = raw[raw.index(FROZEN_MARKER):]
    require(len(frozen) == 9470 and digest(frozen) == FROZEN_SHA, "frozen whole suffix drift")


def changed_copy(raw, recipe):
    text = raw.decode("utf-8", errors="strict")
    for edit in recipe["edits"]:
        require(edit["occurrences"] == 1 and edit["old"] != edit["new"], "invalid literal edit")
        require(text.count(edit["old"]) == 1, "ambiguous/missing actual-source edit: " + recipe["id"])
        text = text.replace(edit["old"], edit["new"], 1)
    changed = text.encode("utf-8")
    require(len(changed) == recipe["after"]["bytes"] and digest(changed) == recipe["after"]["sha256"],
            "copy-only control pin drift: " + recipe["id"])
    if recipe["path"] == DISPATCHER:
        suffix(changed)
    return changed


def static_evaluation(text, predicates):
    return {entry["id"]: all(text.count(context) == 1 for context in entry["complete_contexts"])
            for entry in predicates}


def source_paths(manifest, unit):
    if unit["target"] == "library":
        return manifest["core89_paths"]
    return manifest["component_production_paths"] + [unit["test_path"]]


def compiler_argv(plan, manifest, unit):
    base = OUTPUT + "/" + unit["id"]
    args = list(plan["compiler_prefix"]) + ["/target:" + unit["target"]]
    if unit["target"] == "exe":
        args += ["/main:" + unit["main"]]
    args += ["/out:" + base + "/" + unit["assembly"], "/reference:" + plan["reference_row"][0]]
    args += [base + "/source/" + name for name in source_paths(manifest, unit)]
    return args


def managed_argv(plan, unit):
    args = [plan["tool_rows"][1][0], OUTPUT + "/" + unit["id"] + "/" + unit["assembly"]]
    if "mutation" in unit:
        args += ["--case", unit["case"]]
    return args


def lines(raw):
    text = raw.decode("utf-8", errors="strict")
    require(text.endswith("\n") and "\r" not in text, "noncanonical/incomplete stdout")
    result = text[:-1].split("\n")
    require(all(result), "blank/unexpected stdout line")
    return result


def positive_baseline(raw, cases):
    actual = lines(raw)
    require(len(actual) == len(cases) + 1, "wrong positive baseline line count")
    counts = []
    for case, line in zip(cases, actual[:-1]):
        match = re.fullmatch(re.escape("PASS " + case + " assertions=") + r"([1-9][0-9]*)", line)
        require(match is not None, "wrong named/positive PASS: " + case)
        counts.append(int(match.group(1)))
    summary = "SUMMARY component_cases=" + str(len(cases)) + " assertions=" + str(sum(counts))
    summary += " failures=0 Game=0 Unity=0 native=0"
    require(actual[-1] == summary, "wrong positive aggregate SUMMARY")
    return {"observed_cases": len(counts), "observed_assertions": sum(counts),
            "assertions_by_case": dict(zip(cases, counts))}


def old_regression(raw, cases):
    expected = ["PASS " + case for case in cases]
    expected += ["SUMMARY component_cases=16 failures=0 Game=0 Unity=0 native=0"]
    require(lines(raw) == expected, "unchanged prior16 current-source regression failed")
    return {"observed_cases": 16, "assertion_count": "NOT_EXPOSED_BY_UNCHANGED_PRIOR_PROGRAM"}


def semantic_failure(raw, unit):
    actual = lines(raw)
    prefix = "FAIL " + unit["case"] + ": " + unit["failure_fragment"]
    require(len(actual) == 1 and (actual[0] == prefix or actual[0].startswith(prefix + ": ")),
            "wrong case/causal assertion, not an intended semantic kill: " + unit["id"])
    require("ERROR" not in actual[0] and "SUMMARY" not in actual[0] and "PASS " not in actual[0],
            "error/summary/success is not a semantic kill")


def inventory_guard(roster):
    require(roster["counts"] == {"Contracts": 14, "Gameplay": 43, "AssetImport": 27,
                                 "Runtime": 15, "Presentation": 38}, "wrong measured source counts")
    require(sum(roster["counts"].values()) == len(roster["source_rows"]) == 137, "wrong Unity metadata count")
    require(digest(compact(roster["source_rows"])) == roster["ordered_rows_sha256"], "wrong Unity roster pin")
    for folder, count in roster["counts"].items():
        directory = local("Assets/Scripts/" + folder)
        actual = sorted(path.name for path in directory.iterdir() if path.suffix == ".cs")
        expected = [Path(row[0]).name for row in roster["source_rows"]
                    if Path(row[0]).parent.as_posix() == "Assets/Scripts/" + folder]
        require(actual == expected and len(actual) == count, "current source membership drift: " + folder)
        require(not any(path.is_dir() for path in directory.iterdir()), "new nested production source directory")
    scripts = local("Assets/Scripts")
    require(not any(path.suffix == ".cs" for path in scripts.iterdir()), "new root production unit")
    require(sorted(path.name for path in scripts.iterdir() if path.is_dir()) ==
            ["AssetImport", "Contracts", "Editor", "Gameplay", "Presentation", "Runtime"], "source directory drift")
    for name in roster["assembly_metadata_locations"]:
        require(not any(path.suffix in (".asmdef", ".asmref") for path in local(name).iterdir()),
                "assembly metadata context changed: " + name)


def fresh_time(release):
    now = datetime.datetime.now(datetime.timezone.utc)
    issued = datetime.datetime.fromisoformat(release["issued_at_utc"])
    expires = datetime.datetime.fromisoformat(release["expires_at_utc"])
    require(issued.utcoffset() == datetime.timedelta(0) and expires.utcoffset() == datetime.timedelta(0),
            "release timestamps must be UTC")
    require(issued <= now <= expires and datetime.timedelta(0) < expires - issued <= datetime.timedelta(hours=1),
            "release is not fresh/live")
    require(isinstance(release["admission_id"], str) and bool(release["admission_id"].strip()), "missing admission id")


def preflight():
    require(sys.argv[1:] == ["--parent-release", RELEASE], "fresh exact parent release argument required")
    require(Path.cwd() == WT and Path(__file__).resolve() == WT / "tests/source-kickoff-instruction-child/validate.py",
            "wrong worktree/driver")
    require(sys.executable == "/usr/bin/python3.12" and sys.flags.isolated and sys.flags.no_site
            and sys.dont_write_bytecode, "use the exact pinned Python with -I -S -B")
    require(not os.path.lexists(WT / OUTPUT), "single-use output already consumed")
    release_raw = local(RELEASE).read_bytes()
    release = document(release_raw)
    require(release["schema"] == "source-kickoff-child-build-execution-release-1", "wrong execution release")
    fresh_time(release)
    require(release["worktree"] == str(WT) and release["output"] == OUTPUT and release["authorize"] == SCOPES,
            "wrong explicit execution scope/output")
    plan = pinned_document(Y + "/validation-proposal.json", release["proposal_pin"])
    manifest = pinned_document(Y + "/change-manifest.json", release["manifest_pin"])
    roster = pinned_document(Y + "/unity-facing-current-roster.json", release["unity_roster_pin"])
    pinned_document(Y + "/source-write-handoff.json", release["handoff_pin"])
    require(plan["schema"] == "source-kickoff-child-build-validation-proposal-1"
            and plan["status"] == "PREPARATION_ONLY_NOT_EXECUTED", "wrong prepared plan")
    require(release["manifest_pin"] == plan["manifest_pin"] and release["unity_roster_pin"] == plan["unity_roster_pin"],
            "proposal/manifest/roster admission differs")
    require(release["bounds"] == plan["bounds"] and release["tool_rows"] == plan["tool_rows"]
            and release["reference_row"] == plan["reference_row"] and release["framework"] == FRAMEWORK
            and plan["framework"] == FRAMEWORK, "tool/reference/framework/bounds not expressly admitted")
    require(release["process_plan_sha256"] == digest(compact(plan["units"])), "process plan not expressly admitted")
    require(release["parent_preflight_verified_all_pins"] is True
            and release["framework_availability_freshly_verified"] is True, "fresh parent preflight required")
    require(len(plan["mutations"]) == 29 and len({row["id"] for row in plan["mutations"]}) == 29,
            "wrong independent mutation roster")
    require(len(plan["cases"]) == 18 and len(set(plan["cases"])) == 18 and len(plan["prior_cases"]) == 16,
            "wrong frozen named inventories")
    units = plan["units"]
    require([unit["id"] for unit in units] ==
            ["baseline-child18", "regression-coordinator16"] + [row["id"] for row in plan["mutations"]] + ["core89"],
            "wrong process order")
    require(len(units) == 32 and sum(unit["target"] == "exe" for unit in units) == 31
            and units[-1]["target"] == "library", "wrong compiler/managed counts")
    require(all(unit.get("mutation") == unit["id"] for unit in units[2:-1])
            and all("mutation" not in unit for unit in (units[0], units[1], units[-1])),
            "every independent mutant must use its own exact selector")
    require(len(plan["static_predicates"]) == len(plan["static_controls"]) == 3
            and len({entry["id"] for entry in plan["static_predicates"]}) == 3
            and {entry["expected_guard"] for entry in plan["static_controls"]} ==
            {entry["id"] for entry in plan["static_predicates"]}, "wrong STATIC_NOT_GAME control inventory")
    require(plan["bounds"]["compiler_starts"] == 32 and plan["bounds"]["managed_starts"] == 31
            and plan["bounds"]["total_child_starts"] == 63, "wrong process bounds")
    require(plan["runtimeconfig"] == {"runtimeOptions": {
        "tfm": "net10.0", "rollForward": "Disable",
        "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}, "wrong exact runtimeconfig")
    metadata_rows = manifest["readonly_input_rows"] + manifest["authored_rows"]
    inputs = {row[0]: read_row(row) for row in metadata_rows}
    inputs.update({row[0]: read_row(row) for row in roster["source_rows"]})
    require(len(inputs) == len(metadata_rows) + len(roster["source_rows"]), "duplicate input rows")
    inventory_guard(roster)
    suffix(inputs[DISPATCHER])
    current_rows = {row[0]: row for row in roster["source_rows"]}
    component = manifest["component_production_paths"]
    require(len(component) == len(set(component)) == 8, "not the whole eight-unit component closure")
    require("Assets/Scripts/Gameplay/SourceAnyButtonWait.cs" in component, "missing real frozen-suffix dependency")
    require(all(name in current_rows for name in component), "component outside frozen production")
    require(not any("GameSimulation" in name for name in component), "no partial/fake Game in component")
    core = manifest["core89_paths"]
    require(len(core) == len(set(core)) == 89 and all(name in current_rows for name in core), "wrong full Core89")
    require(digest(compact([current_rows[name] for name in core])) == manifest["core89_ordered_rows_sha256"],
            "wrong current Core89 ordered pins")
    selected = [row[0] for folder in ("Contracts", "Gameplay", "AssetImport")
                for row in roster["source_rows"] if Path(row[0]).parent.as_posix() == "Assets/Scripts/" + folder]
    selected += manifest["canonical_presentation_runtime_paths"]
    require(core == selected, "Core selection/order no longer canonical")
    mutations = {row["id"]: row for row in plan["mutations"]}
    variants = {name: changed_copy(inputs[recipe["path"]], recipe) for name, recipe in mutations.items()}
    for unit in units:
        argv = compiler_argv(plan, manifest, unit)
        require(digest(compact(argv)) == unit["compiler_argv_sha256"], "exact compiler argv drift: " + unit["id"])
        if unit["target"] == "exe":
            require(digest(compact(managed_argv(plan, unit))) == unit["managed_argv_sha256"],
                    "exact managed argv drift: " + unit["id"])
        if "mutation" in unit:
            recipe = mutations[unit["mutation"]]
            require(unit["case"] == recipe["case"] and unit["case"] in plan["cases"]
                    and unit["failure_fragment"] == recipe["failure_fragment"], "wrong exact causal selector")
    require(compiler_argv(plan, manifest, units[0]) == plan["baseline_compiler_argv"]
            and compiler_argv(plan, manifest, units[-1]) == plan["core_compiler_argv"], "literal endpoint argv drift")
    require(plan["compiler_prefix"] == [plan["tool_rows"][1][0], plan["tool_rows"][2][0],
            "/nologo", "/noconfig", "/nostdlib+", "/langversion:7.3", "/warnaserror+", "/deterministic+"],
            "unsupported compiler/reference/flags")
    framework_pins = release["framework_pins"]
    require(framework_pins["framework"] == FRAMEWORK and framework_pins["complete_inventory_attested"] is True,
            "fresh complete genuine framework manifest required, not a discovery/fallback")
    root = Path(framework_pins["root"])
    require(root == Path("/home/long2know/.dotnet/shared/Microsoft.NETCore.App/10.0.0"),
            "wrong explicitly admitted framework directory")
    framework_rows = framework_pins["rows"]
    require(framework_rows and framework_rows == sorted(framework_rows)
            and len({row[0] for row in framework_rows}) == len(framework_rows), "invalid explicit framework roster")
    require(all(Path(row[0]).is_absolute() and Path(row[0]).parent == root for row in framework_rows),
            "framework files must be an exact nonrecursive parent-pinned inventory")
    require(digest(compact(framework_rows)) == framework_pins["ordered_rows_sha256"], "framework roster pin drift")
    binary_rows = plan["tool_rows"] + [plan["reference_row"]] + framework_rows
    require(len({row[0] for row in binary_rows}) == len(binary_rows), "duplicate binary pin rows")
    # No tool/reference/framework binary is opened until the complete fresh release is admitted.
    for row in binary_rows:
        read_row(row)
    return release_raw, plan, manifest, roster, inputs, variants, binary_rows


def main():
    started = time.monotonic()
    def alarm(signum, frame):
        raise TimeoutError("runner wall/postcheck bound exhausted")

    signal.signal(signal.SIGALRM, alarm)
    signal.setitimer(signal.ITIMER_REAL, 60)
    release_raw, plan, manifest, roster, inputs, variants, binary_rows = preflight()
    bounds = plan["bounds"]
    deadline = started + bounds["runner_wall_seconds"]
    require(time.monotonic() < deadline - bounds["postcheck_reserve_seconds"], "preflight exhausted wall budget")
    output = local(OUTPUT)
    output.mkdir()
    work = output / "process-work"
    work.mkdir()
    environment = dict(plan["clean_environment"])
    environment.update({key: str(work) for key in (
        "HOME", "TMPDIR", "TMP", "TEMP", "DOTNET_CLI_HOME", "DOTNET_BUNDLE_EXTRACT_BASE_DIR")})
    result = {"schema": "source-kickoff-child-current-qualification-result-1", "success": False,
              "qualification": "UNQUALIFIED", "whole100": "OPEN", "commands": [],
              "release_sha256": digest(release_raw), "observations": {}, "static": {},
              "copied_input_rows": [],
              "core_library_executed": False, "Game_Unity_native_provider_starts": 0,
              "ordinary_inventory_status": plan["ordinary_inventory"]["status"]}

    def write_json(path, value):
        with path.open("x", encoding="utf-8") as stream:
            stream.write(json.dumps(value, indent=2, ensure_ascii=True) + "\n")

    signal.setitimer(signal.ITIMER_REAL, max(0.001, deadline - time.monotonic() - bounds["postcheck_reserve_seconds"]))

    def command(argv, directory, phase, timeout, expected_exit):
        require(len(result["commands"]) < bounds["total_child_starts"], "child start bound exhausted")
        remaining = deadline - time.monotonic() - bounds["postcheck_reserve_seconds"]
        require(remaining > timeout + bounds["termination_and_drain_seconds"], "insufficient complete process budget")
        record = {"argv": argv, "phase": phase, "started": False, "expected_exit": expected_exit,
                  "exit": None, "complete": False, "timed_out": False, "overflow": False}
        result["commands"].append(record)
        process = None
        selector = selectors.DefaultSelector()
        streams = {}
        counts = {"stdout": 0, "stderr": 0}
        hashes = {key: hashlib.sha256() for key in counts}
        termination = None
        failure = None
        began = time.monotonic()

        def terminate(reason):
            nonlocal termination, failure
            if failure is None:
                failure = reason
            if process is not None and termination is None:
                termination = time.monotonic()
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass

        def cpu_bound():
            resource.setrlimit(resource.RLIMIT_CPU, (timeout, timeout))
            resource.setrlimit(resource.RLIMIT_CORE, (0, 0))

        try:
            for name in counts:
                streams[name] = (directory / (phase + "." + name + ".txt")).open("xb")
            process = subprocess.Popen(argv, cwd=WT, env=environment, stdin=subprocess.DEVNULL,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                       start_new_session=True, preexec_fn=cpu_bound)
            record.update(started=True, pid=process.pid)
            selector.register(process.stdout, selectors.EVENT_READ, "stdout")
            selector.register(process.stderr, selectors.EVENT_READ, "stderr")
            while selector.get_map():
                now = time.monotonic()
                if now - began >= timeout and termination is None:
                    record["timed_out"] = True
                    terminate("process wall timeout")
                if termination is not None:
                    if now - termination >= bounds["termination_and_drain_seconds"]:
                        raise TimeoutError("termination did not produce complete pipe EOFs")
                    if now - termination >= 1:
                        try:
                            os.killpg(process.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                for key, _ in selector.select(timeout=0.05):
                    block = os.read(key.fileobj.fileno(), 65536)
                    if not block:
                        selector.unregister(key.fileobj)
                        key.fileobj.close()
                        continue
                    name = key.data
                    streams[name].write(block)
                    counts[name] += len(block)
                    hashes[name].update(block)
                    if counts[name] > bounds["capture_bytes_per_stream"]:
                        record["overflow"] = True
                        terminate("capture cap exceeded; never a semantic kill")
            wait_bound = max(0.001, began + timeout - time.monotonic())
            if termination is not None:
                wait_bound = max(0.001, termination + bounds["termination_and_drain_seconds"] - time.monotonic())
            try:
                record["exit"] = process.wait(timeout=wait_bound)
            except subprocess.TimeoutExpired:
                record["timed_out"] = True
                terminate("process exit timeout after pipe EOF")
                raise TimeoutError("process exit timeout after pipe EOF")
            record["complete"] = True
        except BaseException:
            terminate("abnormal capture/runner interruption")
            raise
        finally:
            if process is not None and (not record["complete"] or process.poll() is None):
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                try:
                    remaining_cleanup = max(0.001, termination + bounds["termination_and_drain_seconds"]
                                            - time.monotonic())
                    record["exit"] = process.wait(timeout=remaining_cleanup)
                except subprocess.TimeoutExpired:
                    record["exit"] = None
            selector.close()
            if process is not None:
                for pipe in (process.stdout, process.stderr):
                    if pipe is not None and not pipe.closed:
                        pipe.close()
            for stream in streams.values():
                stream.close()
            record.update(seconds=round(time.monotonic() - began, 6), failure=failure,
                          capture={name: {"bytes": counts[name], "sha256": hashes[name].hexdigest()}
                                   for name in counts})
            write_json(directory / (phase + ".process.json"), record)
        require(record["complete"] and not record["timed_out"] and not record["overflow"]
                and failure is None and record["exit"] == expected_exit, "unclean/wrong process outcome")
        return tuple((directory / (phase + "." + name + ".txt")).read_bytes() for name in counts)

    try:
        write_json(output / "consumed-release.json", document(release_raw))
        predicates = plan["static_predicates"]
        base = static_evaluation(inputs[GAME].decode("utf-8"), predicates)
        require(len(base) == 3 and all(base.values()), "STATIC_NOT_GAME baseline predicate failed")
        result["static"] = {"classification": "STATIC_NOT_GAME", "baseline": base, "controls": []}
        for control in plan["static_controls"]:
            changed = changed_copy(inputs[GAME], control)
            evaluation = static_evaluation(changed.decode("utf-8"), predicates)
            require([name for name, passed in evaluation.items() if not passed] == [control["expected_guard"]],
                    "wrong STATIC_NOT_GAME causal predicate")
            directory = output / "static" / control["id"]
            directory.mkdir(parents=True)
            with (directory / "GameSimulation.Instructions.cs").open("xb") as stream:
                stream.write(changed)
            result["static"]["controls"].append({"id": control["id"], "evaluation": evaluation,
                                                  "classification": "STATIC_NOT_GAME"})
        for unit in plan["units"]:
            directory = output / unit["id"]
            directory.mkdir()
            for name in source_paths(manifest, unit):
                raw = inputs[name]
                if "mutation" in unit:
                    recipe = next(row for row in plan["mutations"] if row["id"] == unit["mutation"])
                    if name == recipe["path"]:
                        raw = variants[recipe["id"]]
                path = directory / "source" / name
                path.parent.mkdir(parents=True, exist_ok=True)
                with path.open("xb") as stream:
                    stream.write(raw)
                copy_row = [path.relative_to(WT).as_posix(), len(raw), digest(raw)]
                read_row(copy_row)
                result["copied_input_rows"].append(copy_row)
            timeout = bounds["core_compile_seconds"] if unit["target"] == "library" else bounds["component_compile_seconds"]
            stdout, stderr = command(compiler_argv(plan, manifest, unit), directory, "compile", timeout, 0)
            require(not stdout and not stderr, "compiler diagnostics are not clean success: " + unit["id"])
            assembly = directory / unit["assembly"]
            require(assembly.is_file() and assembly.stat().st_size > 0, "compiler emitted no assembly")
            result["observations"][unit["id"]] = {"assembly_bytes": assembly.stat().st_size,
                                                  "assembly_sha256": digest(assembly.read_bytes())}
            if unit["target"] == "library":
                continue
            write_json(assembly.with_suffix(".runtimeconfig.json"), plan["runtimeconfig"])
            config_raw = (json.dumps(plan["runtimeconfig"], indent=2, ensure_ascii=True) + "\n").encode("utf-8")
            config_row = [assembly.with_suffix(".runtimeconfig.json").relative_to(WT).as_posix(),
                          len(config_raw), digest(config_raw)]
            read_row(config_row)
            result["copied_input_rows"].append(config_row)
            expected_exit = 1 if "mutation" in unit else 0
            stdout, stderr = command(managed_argv(plan, unit), directory, "run", bounds["managed_seconds"], expected_exit)
            require(not stderr, "unexpected managed stderr")
            if unit["id"] == "baseline-child18":
                result["observations"][unit["id"]].update(positive_baseline(stdout, plan["cases"]))
            elif unit["id"] == "regression-coordinator16":
                result["observations"][unit["id"]].update(old_regression(stdout, plan["prior_cases"]))
            else:
                semantic_failure(stdout, unit)
                result["observations"][unit["id"]].update(case=unit["case"], intended_semantic_kill=True)
        records = result["commands"]
        require(len(records) == bounds["total_child_starts"] and all(row["started"] and row["complete"] for row in records),
                "incomplete successful start/exit inventory")
        require(sum(row["phase"] == "compile" and row["exit"] == 0 for row in records) == 32
                and sum(row["phase"] == "run" and row["exit"] == 0 for row in records) == 2
                and sum(row["phase"] == "run" and row["exit"] == 1 for row in records) == 29,
                "wrong compiler/managed exit counts")
        result["success"] = True
        result["qualification"] = "BOUNDED_CURRENT_COMPONENT_AND_CORE_ONLY"
    except Exception as error:
        result["failure"] = type(error).__name__ + ": " + str(error)
    finally:
        signal.setitimer(signal.ITIMER_REAL, bounds["postcheck_reserve_seconds"])
        try:
            for row in manifest["readonly_input_rows"] + manifest["authored_rows"] + roster["source_rows"] + binary_rows:
                read_row(row)
            for row in result["copied_input_rows"]:
                read_row(row)
            admitted = document(release_raw)
            for filename, field in (("validation-proposal.json", "proposal_pin"),
                                    ("change-manifest.json", "manifest_pin"),
                                    ("unity-facing-current-roster.json", "unity_roster_pin"),
                                    ("source-write-handoff.json", "handoff_pin")):
                pinned_document(Y + "/" + filename, admitted[field])
            require(local(RELEASE).read_bytes() == release_raw, "parent release changed during execution")
            inventory_guard(roster)
            suffix(local(DISPATCHER).read_bytes())
            result["postchecks"] = "ALL_SOURCE_METADATA_TOOL_REFERENCE_FRAMEWORK_PINS_UNCHANGED"
        except Exception as error:
            result["success"] = False
            result["qualification"] = "UNQUALIFIED"
            result["postcheck_failure"] = type(error).__name__ + ": " + str(error)
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
        result["seconds"] = round(time.monotonic() - started, 6)
        if result["seconds"] > bounds["runner_wall_seconds"]:
            result["success"] = False
            result["qualification"] = "UNQUALIFIED"
            result["wall_bound_exceeded"] = True
        write_json(output / "result.json", result)
        with (output / "validation.exit.txt").open("x", encoding="ascii") as stream:
            stream.write("0\n" if result["success"] else "1\n")
    return 0 if result["success"] else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("HELD/FAILED " + type(error).__name__ + ": " + str(error), file=sys.stderr)
        sys.exit(2)
