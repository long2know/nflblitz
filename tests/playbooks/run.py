#!/usr/bin/env python3
"""Reproduce the permanent P1 producer/original-C/runtime/mutation regressions."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import uuid

sys.dont_write_bytecode = True
import oracle
from support import (
    SUITE, ROOT, ARTIFACTS, PINS, GateError, blob_sha1, sha256, verify_inputs,
    require_recipe, snapshot, require_unchanged, validate_schema,
)


MUTATIONS = (
    ("weighted-ordinal", "catalog", "compiled original catalog", "P ShortOffense 52 27 play_switch.Index"),
    ("route-opcode", "catalog", "converted catalog load", "pr_goto_endzone"),
    ("five-yard-exclusion", "source", "explicit source guards/RNG provenance", "strict five-yard exclusion exactly at YD5"),
)
DRONE_ANCHOR = "receiver.ThrowDistance <= UnitsPerYard * 5f"
DRONE_REPLACEMENT = "receiver.ThrowDistance < UnitsPerYard * 5f"


def require_baseline(code, summary):
    if (code != 0 or not isinstance(summary, dict)
            or summary.get("schema") != "source-playbook-checks/v1"
            or summary.get("status") != "passed" or summary.get("passed") is not True
            or summary.get("scope") != "all"
            or summary.get("full_gameplay_parity_claimed") is not False
            or any(type(summary.get(key)) is not int or summary[key] != value for key, value in PINS["expected"].items())
            or summary.get("observed_entry_opcodes") != [1, 2, 3, 4, 5, 6, 7, 8, 9, 13, 14, 17]
            or summary.get("observed_scripts") != ["c_jmpint_anim", "c_no_vel1_anim", "juke_c_anim", "spin_anim"]):
        raise GateError("Incomplete/unsupported P1 baseline: require the complete original catalog/numeric/route/AI/initialization/write/RNG matrix.")


def require_mutation(code, summary, mutation):
    name, _, context, offending = mutation
    if (code != 1 or not isinstance(summary, dict) or summary.get("schema") != "source-playbook-checks/v1"
            or summary.get("status") != "failed" or summary.get("passed") is not False
            or summary.get("context") != context or summary.get("scope") != "quick"
            or summary.get("full_gameplay_parity_claimed") is not False or type(summary.get("assertions")) is not int):
        raise GateError("A crash/build error/survivor is not a behavioral mutation rejection: " + name)
    if name == "route-opcode":
        if (summary.get("failure_kind") != "error" or summary.get("exception_type") != "System.FormatException"
                or offending not in summary.get("error", "") or "unknown opcode 999" not in summary.get("error", "")
                or summary["assertions"] != 0):
            raise GateError("Route mutant did not fail its exact source-identity/opcode loader guard.")
    elif (summary.get("failure_kind") != "assertion" or summary.get("exception_type") != "Checks+AssertionFailure"
          or offending not in (summary.get("failed_assertion") or "") or summary["assertions"] <= 0):
        raise GateError("Mutant did not fail its actual offending-field assertion: " + name)


def require_manifest(manifest, catalog, recipe, contents, schema):
    validate_schema(manifest, schema)
    expected_counts = dict(recipe["expected_counts"])
    expected_counts["pool_entries"] = {family: item["count"] for family, item in recipe["weighted_pools"].items()}
    expected_counts["pool_entries"].update({family: 1 for family in recipe["singleton_pools"]})
    if (manifest["source_revision"] != PINS["revision"] or manifest["source_tree"] != PINS["tree"]
            or manifest["profile"] != PINS["profile"] or manifest["source_inputs"] != recipe["inputs"]
            or manifest["recipe_sha256"] != sha256(contents["tools/playbooks/recipe.json"])
            or manifest["converter_sha256"] != sha256(contents["tools/playbooks/convert.py"])
            or manifest["catalog"]["sha256"] != sha256(catalog)
            or sha256(catalog) != PINS["catalog_sha256"] or manifest["counts"] != expected_counts):
        raise GateError("Actual producer manifest/catalog identity, schema, source pins or full inventory mismatch.")


def mutate_catalog(data, name):
    rows = data.decode("utf-8").splitlines()
    if name == "weighted-ordinal":
        matches = [i for i, row in enumerate(rows) if row.startswith("pool\tShortOffense\t")]
        if len(matches) != 1:
            raise GateError("Weighted mutant requires exactly one real ShortOffense record.")
        index = matches[0]
        fields = rows[index].split("\t")
        entries = fields[5].split(",")
        if len(entries) != 52 or entries[27:29] != ["play_switch", "play_switch"]:
            raise GateError("Weighted mutant requires the actual duplicate ordinals 27/28.")
        entries[27] = "play_da_bomb"
        fields[5] = ",".join(entries)
    elif name == "route-opcode":
        matches = [i for i, row in enumerate(rows) if row.startswith("route\tpr_goto_endzone\t")]
        if len(matches) != 1:
            raise GateError("Route mutant requires exactly one real pr_goto_endzone record.")
        index = matches[0]
        fields = rows[index].split("\t")
        words = fields[4].split(",")
        if words[0] == "999":
            raise GateError("Route mutant must change an actual original opcode.")
        words[0] = "999"
        fields[4] = ",".join(words)
    else:
        raise GateError("Unsupported catalog mutation: " + name)
    rows[index] = "\t".join(fields)
    return ("\n".join(rows) + "\n").encode("utf-8")


def require_unit_tests(code, output, count):
    found = re.findall(r"^Ran (\d+) tests? in ", output, flags=re.M)
    if (code != 0 or found != [str(count)] or not re.search(r"^OK\s*$", output, flags=re.M)
            or re.search(r"\bskipped\b", output, flags=re.I)):
        raise GateError("Unit-test inventory/status mismatch; no missing tests or skips are accepted.")


def main(argv=None):
    global ARTIFACTS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ARTIFACTS / "original",
                        help="Exact canonical-path pinned public source input root; no network fallback.")
    parser.add_argument("--output", type=Path, help="New ignored child output; never overwrite historical P1 receipts.")
    args = parser.parse_args(argv)
    if args.output is not None:
        selected = args.output.absolute()
        if not selected.is_relative_to(SUITE / ".artifacts") or selected.exists():
            parser.error("--output requires a new tests/playbooks/.artifacts child.")
        ARTIFACTS = selected
    source_root = args.source_root.resolve()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    report = ARTIFACTS / "result.json"
    report.unlink(missing_ok=True)
    workspace = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    workspace.mkdir()
    cache = workspace / "runtime-cache"
    cache.mkdir()
    environment = dict(os.environ, TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                       DOTNET_CLI_HOME=str(workspace / "dotnet-home"), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
    evidence = {
        "schema": "source-playbook-regressions/v1", "status": "failed", "commands": [], "mutations": [],
        "source_root": str(source_root), "source_pins": PINS,
        "profile": "Actual complete nine Contracts + SourceClock/MatchRules/MatchFlow + final three P1 helpers only; C#7.3/netstandard2.1 warnings-as-errors, net10.0 runner.",
        "boundary": PINS["boundary"], "full_gameplay_parity_claimed": False,
        "unity_editor_native_build_browser_or_hosting_executed": False,
        "production_hashes_unchanged": False, "original_input_hashes_unchanged": False,
    }
    started = time.monotonic()
    before = original_before = None
    exit_code = 1

    def execute(label, command, timeout=180):
        start = time.monotonic()
        process = subprocess.Popen(command, cwd=ROOT, env=environment, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, text=True, start_new_session=True)
        try:
            stdout, stderr = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                stdout, stderr = process.communicate(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                stdout, stderr = process.communicate()
            (ARTIFACTS / (label + ".log")).write_text(stdout + stderr, encoding="utf-8")
            raise GateError("Timed out (not a mutation kill): " + label)
        evidence["commands"].append({
            "label": label, "command": [str(part) for part in command], "exit_code": process.returncode,
            "seconds": round(time.monotonic() - start, 3), "log": label + ".log",
        })
        (ARTIFACTS / (label + ".log")).write_text(stdout + stderr, encoding="utf-8")
        return process.returncode, stdout, stderr

    def copy_sources(destination, contents):
        for name, data in contents.items():
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)

    def compile_checks(label, root):
        project_root = workspace / label / "tests/playbooks"
        for name in ("Checks.cs", "PlaybookChecks.csproj", "model/PurePlaybooks.csproj", "model/FriendAccess.cs"):
            path = project_root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(SUITE / name, path)
        project = project_root / "PlaybookChecks.csproj"
        property_arg = "-p:BlitzSourceRoot=" + str(root) + "/"
        command = ["dotnet", "build", str(project), "--configuration", "Release", "--verbosity", "quiet",
                   "--no-restore", property_arg]
        code, stdout, stderr = execute(label + "-build-no-restore", command)
        if code and "NETSDK1004" in stdout + stderr:
            code, _, _ = execute(label + "-restore", ["dotnet", "restore", str(project), "--verbosity", "quiet", property_arg])
            if code:
                raise GateError("Missing-reference restore failed, not a behavioral rejection: " + label)
            code, _, _ = execute(label + "-build", command)
        if code:
            raise GateError("Actual C#7.3/netstandard2.1 compilation failed, not a behavioral rejection: " + label)
        return project_root / "bin/Release/net10.0/PlaybookChecks.dll"

    def check(label, dll, catalog, native, quick=False):
        work = ARTIFACTS / label
        work.mkdir(exist_ok=True)
        receipt = work / "checks-result.json"
        receipt.unlink(missing_ok=True)
        command = ["dotnet", str(dll), "--work-root", str(work), "--catalog", str(catalog), "--oracle", str(native)]
        if quick:
            command.append("--quick")
        code, stdout, _ = execute(label, command, timeout=360)
        summaries = [json.loads(line) for line in stdout.splitlines() if line.startswith('{"schema":')]
        if len(summaries) != 1 or not receipt.is_file() or json.loads(receipt.read_text(encoding="utf-8")) != summaries[0]:
            raise GateError("Exactly one fresh matching C# console/file receipt is required: " + label)
        return code, summaries[0]

    try:
        original_before = {name: blob_sha1(data) for name, data in verify_inputs(source_root).items()}
        evidence["original_input_blob_sha1_before"] = original_before
        contents, before = snapshot()
        evidence["production_sha256_before"] = before
        recipe = json.loads(contents["tools/playbooks/recipe.json"])
        require_recipe(recipe)
        source = workspace / "source"
        copy_sources(source, contents)
        code, stdout, stderr = execute("runner-unit-tests", [sys.executable, "-B", "-m", "unittest", "discover",
                                                           "-s", str(SUITE), "-p", "test_*.py", "-v"])
        require_unit_tests(code, stdout + stderr, 14)
        code, stdout, stderr = execute("producer-unit-tests", [sys.executable, "-B", "-m", "unittest", "discover",
            "-s", str(source / "tools/playbooks"), "-p", "test_*.py", "-v"])
        require_unit_tests(code, stdout + stderr, 11)
        schema = json.loads(contents["tools/playbooks/manifest.schema.json"])
        converted = source / "tools/playbooks/.artifacts/converted"
        repeated = source / "tools/playbooks/.artifacts/repeated"
        for label, output in (("producer", converted), ("producer-repeat", repeated)):
            code, _, _ = execute(label, [sys.executable, "-B", str(source / "tools/playbooks/convert.py"),
                                       "--source-root", str(source_root), "--output", str(output)])
            if code:
                raise GateError("Actual permanent source producer failed: " + label)
            catalog = (output / "catalog.tsv").read_bytes()
            manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
            require_manifest(manifest, catalog, recipe, contents, schema)
        if ((converted / "catalog.tsv").read_bytes() != (repeated / "catalog.tsv").read_bytes()
                or (converted / "manifest.json").read_bytes() != (repeated / "manifest.json").read_bytes()):
            raise GateError("Actual producer output is not byte-reproducible.")
        saved = ARTIFACTS / "converted"
        saved.mkdir(exist_ok=True)
        for name in ("catalog.tsv", "manifest.json"):
            shutil.copyfile(converted / name, saved / name)
        evidence["producer"] = {"status": "passed", "synthetic_tests": 11, "byte_reproducible": True,
                                "manifest": manifest, "catalog_sha256": sha256(catalog)}
        require_unchanged(before)
        native_root = workspace / "oracle"
        evidence["original_c"] = oracle.build(source_root, native_root, recipe, environment)
        if evidence["original_c"]["selected_function_count"] != len(oracle.FUNCTIONS):
            raise GateError("Original C function inventory incomplete.")
        shutil.copyfile(native_root / "receipt.json", ARTIFACTS / "oracle-receipt.json")
        shutil.copyfile(native_root / "compiler.log", ARTIFACTS / "original-compiler.log")
        native = native_root / "source-oracle"
        dll = compile_checks("baseline-build", source)
        code, baseline = check("baseline", dll, saved / "catalog.tsv", native)
        evidence.update(baseline_exit_code=code, baseline=baseline)
        require_baseline(code, baseline)
        require_unchanged(before)
        print("PASS: complete original C / actual P1 baseline (" + str(baseline["assertions"]) + " assertions).", flush=True)
        for mutation in MUTATIONS:
            name, kind, _, _ = mutation
            if kind == "catalog":
                changed = mutate_catalog(catalog, name)
                target = workspace / (name + ".tsv")
                target.write_bytes(changed)
                mutated_dll, input_catalog = dll, target
                changed_path = "generated/catalog.tsv"
            else:
                mutant_root = workspace / "source-mutant"
                copy_sources(mutant_root, contents)
                changed_path = "Assets/Scripts/Gameplay/SourceDroneAI.cs"
                target = mutant_root / changed_path
                original = target.read_text(encoding="utf-8")
                if original.count(DRONE_ANCHOR) != 1:
                    raise GateError("Actual production FindReceiver mutation anchor must be unique.")
                target.write_text(original.replace(DRONE_ANCHOR, DRONE_REPLACEMENT), encoding="utf-8")
                changed = target.read_bytes()
                mutated_dll = compile_checks("source-mutant-build", mutant_root)
                input_catalog = saved / "catalog.tsv"
            code, summary = check("mutation-" + name, mutated_dll, input_catalog, native, quick=True)
            evidence["mutations"].append({
                "id": name, "artifact": changed_path, "mutated_sha256": sha256(changed),
                "exit_code": code, "summary": summary, "live_source_modified": False,
            })
            require_mutation(code, summary, mutation)
            evidence["mutations"][-1]["behaviorally_rejected"] = True
            require_unchanged(before)
            print("PASS mutation: " + name, flush=True)
        code, final = check("final-baseline", dll, saved / "catalog.tsv", native)
        evidence.update(final_baseline_exit_code=code, final_baseline=final)
        require_baseline(code, final)
        if final != baseline:
            raise GateError("Complete final baseline receipt differs after isolated mutations.")
        evidence["status"] = "passed"
        exit_code = 0
    except (OSError, ValueError, KeyError, GateError, subprocess.SubprocessError) as error:
        evidence["error"] = str(error)
        print("P1 REGRESSIONS FAILED: " + str(error), file=sys.stderr)
    finally:
        try:
            evidence["production_sha256_after"] = require_unchanged(before) if before is not None else None
            evidence["production_hashes_unchanged"] = before is not None
            original_after = {name: blob_sha1(data) for name, data in verify_inputs(source_root).items()}
            evidence["original_input_blob_sha1_after"] = original_after
            evidence["original_input_hashes_unchanged"] = original_before is not None and original_after == original_before
            if not evidence["original_input_hashes_unchanged"]:
                raise GateError("Original input inventory/bytes changed during the run.")
        except (OSError, GateError) as error:
            evidence["hash_error"] = str(error)
            evidence["status"] = "failed"
            exit_code = 1
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_source_build_catalog_and_native_trees_removed"] = not workspace.exists()
        evidence["runtime_seconds"] = round(time.monotonic() - started, 3)
        report.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print("Evidence: " + report.relative_to(ROOT).as_posix(), flush=True)
    if exit_code == 0:
        print("PASS: permanent producer + complete pinned host-C/P1 matrix + three actual-artifact/source mutants; LiveSources unchanged.", flush=True)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
