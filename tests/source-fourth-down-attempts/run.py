#!/usr/bin/env python3
"""Execution is unavailable without a separately pinned parent PhaseC release."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys


ROOT = "/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f"
CAMPAIGN = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-fourth-down-attempts/campaign-01"
PARENT_RELEASE = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-fourth-down-attempts/parent-phase-c-release.json"
MAIN = "Blitz.SourceFourthDownAttemptsComponent.Program"
METADATA_READS = 0


def expected_environment():
    c = CAMPAIGN
    return {
        "PATH": "/usr/bin:/bin", "HOME": c + "/home",
        "TMPDIR": c + "/tmp", "TMP": c + "/tmp", "TEMP": c + "/tmp",
        "XDG_CACHE_HOME": c + "/cache", "XDG_CONFIG_HOME": c + "/config",
        "XDG_DATA_HOME": c + "/xdg-data", "DOTNET_CLI_HOME": c + "/dotnet-home",
        "NUGET_PACKAGES": c + "/nuget-packages", "NUGET_HTTP_CACHE_PATH": c + "/nuget-http-cache",
        "LANG": "C", "LC_ALL": "C", "TZ": "UTC", "SHLVL": "1",
        "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1", "DOTNET_MULTILEVEL_LOOKUP": "0",
        "NUGET_XMLDOC_MODE": "skip", "DOTNET_EnableDiagnostics": "0",
    }


class GateFailure(RuntimeError):
    def __init__(self, code, field):
        super().__init__(field)
        self.code, self.field = code, field


def safe_entry(environment=None, emit=True):
    if emit:
        print(json.dumps({"actualUtc": datetime.now(timezone.utc).isoformat(),
                          "entry": "fourth-down-attempts-driver"}), flush=True)
    actual = os.environ if environment is None else environment
    wanted = expected_environment()
    keys = frozenset(actual.keys())
    allowed = frozenset(wanted)
    projection = {
        "actualKeyCount": len(keys), "expectedKeyCount": len(allowed),
        "extraKeyCount": len(keys - allowed), "missingKeyCount": len(allowed - keys),
        "safeApprovedPresentKeyNames": sorted(keys & allowed), "metadataReads": 0,
    }
    if emit:
        print(json.dumps(projection, separators=(",", ":")), flush=True)
    if keys != allowed:
        raise GateFailure(93, "environment-keys")
    if any(actual[key] != value for key, value in wanted.items()):
        raise GateFailure(94, "environment-values")
    return wanted


def metadata_pin(row):
    global METADATA_READS
    METADATA_READS += 1
    if type(row.get("bytes")) is not int or not 0 < row["bytes"] <= 65536:
        raise GateFailure(95, "metadata-size")
    descriptor = os.open(row["path"], os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, "rb") as stream:
        raw = stream.read(row["bytes"] + 1)
    if len(raw) != row["bytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
        raise GateFailure(95, "metadata-pin")
    return json.loads(raw)


def import_pinned(row, name):
    global METADATA_READS
    METADATA_READS += 1
    descriptor = os.open(row["path"], os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, "rb") as stream:
        raw = stream.read(65537)
    if len(raw) != row["bytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
        raise GateFailure(95, "helper-pin")
    spec = importlib.util.spec_from_file_location(name, row["path"])
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def arguments(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--release", required=True)
    parser.add_argument("--release-bytes", required=True, type=int)
    parser.add_argument("--release-sha256", required=True)
    parser.add_argument("--precheck-only", action="store_true")
    parser.add_argument("--execution-label", default="main")
    return parser.parse_args(argv)


def load_release(args):
    if args.release != PARENT_RELEASE or not re.fullmatch(r"[0-9a-f]{64}", args.release_sha256):
        raise GateFailure(95, "external-release-identity")
    release = metadata_pin({"path": args.release, "bytes": args.release_bytes,
                            "sha256": args.release_sha256})
    if (release.get("decision") != "ADMIT_PHASE_C_SOURCE_FOURTH_DOWN_ATTEMPTS"
            or release.get("agentId") != "e738b90a-1df9-4e80-8617-1d3878ff45ae"):
        raise GateFailure(95, "parent-phase-c-authority")
    package_row = release["package"]
    if package_row["path"] != CAMPAIGN + "/phase-c-inputs-r1.json":
        raise GateFailure(95, "package-path")
    package = metadata_pin(package_row)
    if (package.get("schema") != "source-fourth-down-attempts-phase-c-inputs/v1"
            or package["campaignRoot"] != CAMPAIGN or package["sourceRoot"] != ROOT
            or package["environment"] != expected_environment()
            or release["limits"] != package["proposedLimits"]):
        raise GateFailure(95, "package-or-limits")
    labels = ("main", "probe-driver-conflict", "probe-waiter-conflict")
    if args.execution_label not in labels or args.execution_label != "main" and not args.precheck_only:
        raise GateFailure(95, "execution-label")
    execution = Path(CAMPAIGN) / "execution" / args.execution_label
    if os.path.lexists(execution):
        raise GateFailure(96, "execution-root-conflict")
    return release, package, execution


def expected_cases(literal):
    result = {}
    for row in literal["stateCases"]:
        result[row[0]] = [["attempts.home", row[6]], ["attempts.away", row[7]]] + literal["stateUnchangedFields"]
    result[literal["freshReset"]["id"]] = literal["freshReset"]["fields"]
    for row in literal["temporalCases"]:
        result[row["id"]] = [[key, row["counts"][i % 2]]
                             for i, key in enumerate(literal["temporalFieldOrder"])]
    result[literal["snapshot"]["id"]] = literal["snapshot"]["fields"]
    for row in literal["dtoCases"]:
        result[row["id"]] = row["fields"]
    result[literal["legacyOverloads"]["id"]] = literal["legacyOverloads"]["fields"]
    for row in literal["invalidCases"]:
        result[row["id"]] = [
            ["rejection", True], ["exception", row["exception"]], ["parameter", row["parameter"]],
            ["attempts.home", 5], ["attempts.away", 9],
        ] + literal["stateUnchangedFields"]
    return result


def same(left, right):
    return type(left) is type(right) and left == right


def component_result(data, exit_code, literal, selected=None):
    payload = json.loads(data)
    if payload.get("schema") != "source-fourth-down-attempts/component-v1" or payload.get("technical") is not None:
        raise GateFailure(97, "component-technical")
    if payload.get("fixture") != literal["fixtureFields"]:
        raise GateFailure(97, "typed-fixture-diagnostics")
    for actual, expected in zip(payload["fixture"], literal["fixtureFields"]):
        if not same(actual[1], expected[1]):
            raise GateFailure(97, "typed-fixture-value-type")
    cases = expected_cases(literal)
    if selected is None:
        if (exit_code != 0 or payload.get("failure") is not None or payload.get("selectedCase") != "all"
                or payload.get("selectedField") is not None
                or payload.get("assertions") != literal["expectedAssertionCount"]
                or [row["id"] for row in payload["cases"]] != list(cases)):
            raise GateFailure(97, "clean-component-status-or-inventory")
        for actual in payload["cases"]:
            expected = cases[actual["id"]]
            if actual["fields"] != expected or any(not same(a[1], e[1]) for a, e in zip(actual["fields"], expected)):
                raise GateFailure(97, "clean-component-literal-field")
        return payload
    failure = payload.get("failure")
    if (exit_code != 1 or payload.get("selectedCase") != selected["case"]
            or payload.get("selectedField") != selected["field"]
            or payload.get("assertions") != literal["expectedFixtureFieldCount"] + 1
            or not isinstance(failure, dict)
            or failure.get("case") != selected["case"] or failure.get("field") != selected["field"]
            or not same(failure.get("expected"), selected["expected"])
            or same(failure.get("actual"), selected["expected"])
            or [row["id"] for row in payload["cases"]] != [selected["case"]]):
        raise GateFailure(97, "mutant-not-selected-semantic-assertion")
    fields = payload["cases"][0]["fields"]
    names = [pair[0] for pair in cases[selected["case"]]]
    if [pair[0] for pair in fields] != names[:len(fields)]:
        raise GateFailure(97, "mutant-field-order")
    observed = [pair[1] for pair in fields if pair[0] == selected["field"]]
    if len(observed) != 1 or not same(observed[0], failure["actual"]):
        raise GateFailure(97, "mutant-failure-not-actual-emitted-field")
    return payload


def apply_edits(raw, edits):
    result = raw
    for edit in edits:
        anchor, replacement = edit["anchor"].encode(), edit["replacement"].encode()
        if result.count(anchor) != 1:
            raise GateFailure(95, "actual-production-anchor-not-unique")
        result = result.replace(anchor, replacement, 1)
    return result


def verify_inputs(package, sealer):
    rows = package["production"]
    if len(rows) != 77 or len({row["path"] for row in rows}) != 77:
        raise GateFailure(95, "ordered77")
    for row in rows:
        if not row["path"].startswith(ROOT + "/Assets/Scripts/") or not row["path"].endswith(".cs"):
            raise GateFailure(95, "production-path")
        sealer.check_pin(row)
    for row in package["harness"]:
        sealer.check_pin(row)
    if len(package["harness"]) != 2 or package["main"] != MAIN:
        raise GateFailure(95, "two-harness-sources-and-new-main")
    for row in package["tools"].values():
        sealer.check_pin(row)
    for key, count, directory in (
        ("net10", 167, "/home/long2know/.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0/"),
        ("ns21", 121, "/dev/shm/ap.bweV08/t18/pkg/netstandard.library.ref/2.1.0/ref/netstandard2.1/"),
    ):
        refs = sealer.load_json_pin(package["referenceManifests"][key])
        if len(refs) != count or len({row["path"] for row in refs}) != count:
            raise GateFailure(95, "reference-count")
        for row in refs:
            if not row["path"].startswith(directory) or "/" in row["path"][len(directory):]:
                raise GateFailure(95, "reference-directory")
            sealer.check_pin(row)
    for row in package["helpers"].values():
        sealer.check_pin(row, 65536)
    for row in package["tests"]:
        sealer.check_pin(row, 65536)


def guard_module(package, sealer):
    sealer.check_pin(package["guardImport"], 65536)
    return import_pinned(package["contractGuard"], "_fourth_down_actual_caller_guard")


def require_static_guard(module, flow, caller, shapes):
    try:
        module.require_caller_wiring(flow, caller)
    except module.StaticAssertionFailure as error:
        raise GateFailure(97, error.field) from None
    text = caller.decode()
    for shape in shapes:
        if text.count(shape) != 1:
            raise GateFailure(97, "attempts.caller.actual-three-converging-callers")


def artifact_budget(execution):
    total = count = 0
    for parent, directories, filenames in os.walk(execution, followlinks=False):
        for name in directories + filenames:
            path = Path(parent) / name
            if path.is_symlink():
                raise GateFailure(97, "artifact-symlink")
        for name in filenames:
            count += 1
            if count > 480:
                raise GateFailure(97, "artifact-count-cap")
            total += (Path(parent) / name).stat().st_size
            if total > 66060288:
                raise GateFailure(97, "artifact-total-byte-cap")
    return total


def campaign(package, execution, environment, sealer):
    native = sealer.load_json_pin(package["nativeProof"])
    if native.get("phaseAComplete") is not True:
        raise GateFailure(95, "native-proof-seal")
    literal = sealer.load_json_pin(package["literal"])
    seal = sealer.load_json_pin(package["literalAnchorSeal"])
    if (seal["authoredLiteralPin"]["sha256"] != package["literal"]["sha256"]
            or seal["status"] != "SEALED_LITERALS_SOURCE_VECTORS_ACTUAL_ANCHORS_AND_FIVE_EXACT_ADDITIVE_INVERSES_BEFORE_IMPORTED_HELPER_OR_REFERENCE_BODY_READS"):
        raise GateFailure(95, "literal-anchor-precedence")
    verify_inputs(package, sealer)
    execution.parent.mkdir(parents=True, exist_ok=True)
    execution.mkdir()
    report = {
        "schema": "source-fourth-down-attempts-execution/v1", "status": "INCOMPLETE",
        "compilerStarts": 0, "managedStarts": 0, "compiledSemanticControls": [],
        "staticOnlyControls": [], "waitReceipts": [],
        "qualification": "Managed component/static wiring/compatibility only; no native or Unity gameplay execution.",
    }
    references = {key: sealer.load_json_pin(row) for key, row in package["referenceManifests"].items()}
    sources = [row["path"] for row in package["production"]]
    flow = sealer.check_pin(package["flow"])
    caller = sealer.check_pin(package["caller"])
    guard = guard_module(package, sealer)
    require_static_guard(guard, flow, caller, seal["callerShapes"])

    def compile_one(label, overlay=None, ns=False):
        if report["compilerStarts"] >= 17:
            raise GateFailure(97, "compiler-start-cap")
        report["compilerStarts"] += 1
        directory = execution / label
        directory.mkdir()
        assembly = directory / (label + ".dll")
        selected_sources = list(sources)
        if overlay is not None:
            target = ROOT + "/" + overlay["path"]
            if target not in selected_sources:
                raise GateFailure(95, "mutated-production-source-not-in77")
            original = next(row for row in package["production"] if row["path"] == target)
            raw = sealer.check_pin(original)
            mutant = apply_edits(raw, [{"anchor": overlay["anchor"], "replacement": overlay["replacement"]}])
            if len(mutant) != overlay["mutantBytes"] or sealer.digest(mutant) != overlay["mutantSha256"]:
                raise GateFailure(95, "actual-mutant-pin")
            mutated_path = directory / Path(overlay["path"]).name
            sealer.write_new(mutated_path, mutant, 262144)
            selected_sources[selected_sources.index(target)] = str(mutated_path)
        argv = [
            package["tools"]["dotnet"]["path"], package["tools"]["compiler"]["path"],
            "-nologo", "-noconfig", "-nostdlib+", "-langversion:7.3", "-warnaserror+",
            "-deterministic+", "-target:" + ("library" if ns else "exe"), "-out:" + str(assembly),
        ]
        if not ns:
            argv.append("-main:" + MAIN)
        argv += ["-r:" + row["path"] for row in references["ns21" if ns else "net10"]]
        argv += selected_sources
        if not ns:
            argv += [row["path"] for row in package["harness"]]
        receipt, pin, _, _ = sealer.bounded_child(
            argv, environment, directory, "compiler", 32768, 32768, 180,
        )
        report["waitReceipts"].append(pin)
        if receipt["technical"] is not None or receipt["actualWaitExit"] != 0 or not assembly.is_file():
            raise GateFailure(97, "clean-compilation-required-not-a-semantic-kill")
        if assembly.stat().st_size > 4194304:
            raise GateFailure(97, "assembly-byte-cap")
        if not ns:
            sealer.write_json(directory / (label + ".runtimeconfig.json"), {
                "runtimeOptions": {"tfm": "net10.0", "framework": {
                    "name": "Microsoft.NETCore.App", "version": "10.0.0"}}})
        artifact_budget(execution)
        return assembly

    def managed_one(label, assembly, control=None):
        if report["managedStarts"] >= 16:
            raise GateFailure(97, "managed-start-cap")
        report["managedStarts"] += 1
        argv = [package["tools"]["dotnet"]["path"], str(assembly)]
        if control is not None:
            argv += ["--case", control["case"], "--field", control["field"]]
        receipt, pin, stdout, _ = sealer.bounded_child(
            argv, environment, assembly.parent, "component", 65536, 16384, 90,
        )
        report["waitReceipts"].append(pin)
        if receipt["technical"] is not None:
            raise GateFailure(97, "component-technical-not-a-semantic-kill")
        payload = component_result(stdout, receipt["actualWaitExit"], literal, control)
        artifact_budget(execution)
        return payload

    try:
        baseline = managed_one("baseline", compile_one("baseline"))
        report["baseline"] = {"cases": len(baseline["cases"]), "assertions": baseline["assertions"]}
        for control in seal["staticOnlyActualCallerControls"]:
            mutant = apply_edits(caller, control["edits"])
            if len(mutant) != control["mutantBytes"] or sealer.digest(mutant) != control["mutantSha256"]:
                raise GateFailure(95, "static-actual-caller-mutant-pin")
            try:
                guard.require_caller_wiring(flow, mutant)
            except guard.StaticAssertionFailure as error:
                if error.field != control["field"]:
                    raise GateFailure(97, "static-control-wrong-literal-field")
            else:
                raise GateFailure(97, "static-control-survived")
            report["staticOnlyControls"].append({"id": control["id"], "field": control["field"],
                                                 "evidence": "STATIC_ONLY"})
        for control in seal["compiledActualProductionControls"]:
            assembly = compile_one(control["id"], control)
            payload = managed_one(control["id"], assembly, control)
            report["compiledSemanticControls"].append({
                "id": control["id"], "case": control["case"], "field": control["field"],
                "failure": payload["failure"], "cleanCompilation": True,
            })
        final = managed_one("final", compile_one("final"))
        report["final"] = {"cases": len(final["cases"]), "assertions": final["assertions"]}
        compile_one("ns21", ns=True)
        verify_inputs(package, sealer)
        require_static_guard(guard, flow, sealer.check_pin(package["caller"]), seal["callerShapes"])
        if report["compilerStarts"] != 17 or report["managedStarts"] != 16:
            raise GateFailure(97, "exact-actual-start-counts")
        report["status"] = "COMPLETE_MANAGED_COMPONENT_STATIC_WIRING_NS21_ONLY"
    except (GateFailure, sealer.InputFailure, OSError, ValueError, KeyError, TypeError,
            ImportError, subprocess.SubprocessError) as error:
        report["status"] = "FAILED_OR_INCONCLUSIVE"
        report["failureField"] = error.field if isinstance(error, GateFailure) else type(error).__name__
        sealer.write_json(execution / "driver-result.json", report)
        raise
    report["artifactBytes"] = artifact_budget(execution)
    sealer.write_json(execution / "driver-result.json", report)
    return report


def main(argv=None):
    handled_runtime = ()
    try:
        environment = safe_entry()
        args = arguments(sys.argv[1:] if argv is None else argv)
        _, package, execution = load_release(args)
        if args.precheck_only:
            print(json.dumps({"status": "PRECHECK_ONLY", "metadataReads": METADATA_READS,
                              "compilerStarts": 0, "managedStarts": 0, "reservationCreated": False}))
            return 0
        sealer = import_pinned(package["helpers"]["input_sealer"], "_fourth_down_input_sealer")
        handled_runtime = (sealer.InputFailure,)
        report = campaign(package, execution, environment, sealer)
        print(json.dumps({"status": report["status"], "compilerStarts": report["compilerStarts"],
                          "managedStarts": report["managedStarts"], "compiledControls": 14,
                          "staticOnlyControls": 6, "casesEachClean": 34, "assertionsEachClean": 342}))
        return 0
    except GateFailure as error:
        print(json.dumps({"status": "REJECTED_OR_INCONCLUSIVE", "field": error.field,
                          "metadataReads": METADATA_READS, "compilerOrManagedKillCredit": False}))
        return error.code
    except (handled_runtime + (OSError, ValueError, KeyError, TypeError,
                              ImportError, subprocess.SubprocessError)) as error:
        print(json.dumps({"status": "TECHNICAL_INCONCLUSIVE", "exceptionType": type(error).__name__,
                          "metadataReads": METADATA_READS, "compilerOrManagedKillCredit": False}))
        return 97


if __name__ == "__main__":
    sys.exit(main())
