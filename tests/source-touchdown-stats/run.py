#!/usr/bin/env python3
"""Bounded actual-production component entry; a separate parent release is mandatory."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import selectors
import stat
import subprocess
import sys
import time


ROOT = "/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f"
CAMPAIGN = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-after-current52-next-gap/implementation-01"
PARENT_RELEASE = CAMPAIGN + "/parent-execution-release.json"
PACKET = CAMPAIGN + "/execution-packet.json"
PREFLIGHT = CAMPAIGN + "/implementation-preflight-handoff-r1.json"
MAIN = "Blitz.SourceTouchdownStats.Tests.Program"
METADATA_READS = 0
LIMITS = {
    "preparationPythonStarts": 2, "campaignControllerStarts": 1,
    "unitEntryStarts": 1, "negativeEntryStarts": 8, "compilerStarts": 6, "managedStarts": 5,
    "componentCases": 28, "componentAssertions": 78, "compiledControls": 4, "staticControls": 3,
    "unitCases": 15, "peakOwnedChildren": 1, "compilerSeconds": 180, "managedSeconds": 90,
    "negativeEntrySeconds": 20, "terminationWaitSeconds": 5,
    "controllerEnvelopeSeconds": 1800, "unitEnvelopeSeconds": 240,
    "compilerStdoutBytes": 32768, "compilerStderrBytes": 32768,
    "managedStdoutBytes": 32768, "managedStderrBytes": 8192,
    "negativeStdoutBytes": 4096, "negativeStderrBytes": 4096,
    "metadataBytes": 65536, "assemblyBytes": 4194304,
    "artifactFiles": 96, "artifactBytes": 33554432, "entryStdoutBytes": 4096,
}


class GateFailure(RuntimeError):
    def __init__(self, code, field):
        super().__init__(field)
        self.code, self.field = code, field


def expected_environment():
    c = CAMPAIGN + "/runtime"
    return {
        "PATH": "/usr/bin:/bin", "HOME": c + "/home",
        "TMPDIR": c + "/scratch", "TMP": c + "/scratch", "TEMP": c + "/scratch",
        "XDG_CACHE_HOME": c + "/cache", "XDG_CONFIG_HOME": c + "/config",
        "XDG_DATA_HOME": c + "/xdg-data", "DOTNET_CLI_HOME": c + "/dotnet-home",
        "NUGET_PACKAGES": c + "/nuget-packages", "NUGET_HTTP_CACHE_PATH": c + "/nuget-http-cache",
        "LANG": "C", "LC_ALL": "C", "TZ": "UTC", "SHLVL": "1",
        "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1", "DOTNET_MULTILEVEL_LOOKUP": "0",
        "NUGET_XMLDOC_MODE": "skip", "DOTNET_EnableDiagnostics": "0",
    }


def safe_entry(environment=None, emit=True):
    if emit:
        print(json.dumps({"actualUtc": datetime.now(timezone.utc).isoformat(),
                          "entry": "source-touchdown-stats"}), flush=True)
    actual = os.environ if environment is None else environment
    wanted = expected_environment()
    keys, approved = frozenset(actual), frozenset(wanted)
    if emit:
        print(json.dumps({
            "actualKeyCount": len(keys), "expectedKeyCount": len(approved),
            "extraKeyCount": len(keys - approved), "missingKeyCount": len(approved - keys),
            "safeApprovedPresentKeyNames": sorted(keys & approved), "metadataReads": 0,
        }, separators=(",", ":")), flush=True)
    if keys != approved:
        raise GateFailure(93, "environment-keys")
    if any(actual[key] != value for key, value in wanted.items()):
        raise GateFailure(94, "environment-values")
    if Path.cwd() != Path(ROOT):
        raise GateFailure(95, "working-directory")
    return wanted


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def read_pin(row, cap=33554432):
    if (type(row.get("bytes")) is not int or not 0 < row["bytes"] <= cap
            or not isinstance(row.get("path"), str) or not Path(row["path"]).is_absolute()
            or not isinstance(row.get("sha256"), str)
            or re.fullmatch(r"[0-9a-f]{64}", row["sha256"]) is None):
        raise GateFailure(95, "input-pin-shape")
    descriptor = os.open(row["path"], os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, "rb") as stream:
        if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
            raise GateFailure(95, "input-regular-file")
        raw = stream.read(row["bytes"] + 1)
    if len(raw) != row["bytes"] or digest(raw) != row["sha256"]:
        raise GateFailure(95, "input-pin")
    return raw


def metadata_pin(row, path=None):
    global METADATA_READS
    if path is not None and row.get("path") != path:
        raise GateFailure(95, "metadata-path")
    if type(row.get("bytes")) is not int or not 0 < row["bytes"] <= LIMITS["metadataBytes"]:
        raise GateFailure(95, "metadata-size")
    METADATA_READS += 1
    return json.loads(read_pin(row, LIMITS["metadataBytes"]))


class StrictParser(argparse.ArgumentParser):
    def error(self, message):
        raise GateFailure(95, "arguments")


def arguments(argv):
    parser = StrictParser(add_help=False, allow_abbrev=False)
    parser.add_argument("--release", required=True)
    parser.add_argument("--release-bytes", required=True, type=int)
    parser.add_argument("--release-sha256", required=True)
    parser.add_argument("--execution-label", default="main")
    args = parser.parse_args(argv)
    if args.execution_label != "main":
        raise GateFailure(95, "execution-label")
    if args.release != PARENT_RELEASE or re.fullmatch(r"[0-9a-f]{64}", args.release_sha256) is None:
        raise GateFailure(95, "external-release-identity")
    if not 0 < args.release_bytes <= LIMITS["metadataBytes"]:
        raise GateFailure(95, "metadata-size")
    return args


def load_release(args, require_unused=True):
    release = metadata_pin({"path": args.release, "bytes": args.release_bytes,
                            "sha256": args.release_sha256}, PARENT_RELEASE)
    if (release.get("decision") != "ADMIT_SOURCE_TOUCHDOWN_STATS_EXECUTION_R1"
            or release.get("owner") != "Unity" or release.get("sourceRoot") != ROOT
            or release.get("limits") != LIMITS):
        raise GateFailure(95, "parent-execution-authority")
    packet = metadata_pin(release["package"], PACKET)
    preflight = metadata_pin(release["preflight"], PREFLIGHT)
    if (packet.get("schema") != "source-touchdown-stats-execution-packet/v1"
            or packet.get("status") != "PROPOSED_ONLY_UNACCEPTED"
            or packet.get("sourceRoot") != ROOT or packet.get("campaignRoot") != CAMPAIGN
            or packet.get("main") != MAIN or packet.get("limits") != LIMITS
            or packet.get("environment") != expected_environment()
            or preflight.get("executionPacket") != release["package"]
            or preflight.get("accepted") is not False
            or preflight.get("status") != "PREPARED_NONEXECUTING_AWAITING_PARENT_ADMISSION"):
        raise GateFailure(95, "packet-or-preflight")
    execution = Path(CAMPAIGN) / "execution"
    if require_unused and (os.path.lexists(execution) or os.path.lexists(Path(CAMPAIGN) / "runtime")):
        raise GateFailure(96, "execution-root-conflict")
    return packet, execution


def write_path(path):
    path = Path(path)
    try:
        relative = path.relative_to(ROOT)
        path.relative_to(CAMPAIGN)
    except ValueError:
        raise GateFailure(95, "write-boundary") from None
    if ".." in relative.parts or Path.cwd() != Path(ROOT):
        raise GateFailure(95, "write-boundary")
    current = Path(CAMPAIGN)
    if current.is_symlink():
        raise GateFailure(95, "write-parent-symlink")
    for part in path.relative_to(CAMPAIGN).parts[:-1]:
        current /= part
        if current.is_symlink():
            raise GateFailure(95, "write-parent-symlink")
    return relative


def write_new(path, raw, cap=65536):
    if len(raw) > cap:
        raise GateFailure(97, "artifact-byte-cap")
    relative = write_path(path)
    descriptor = os.open(relative, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(raw)
    return {"path": str(path), "bytes": len(raw), "sha256": digest(raw)}


def write_json(path, value):
    return write_new(path, (json.dumps(value, separators=(",", ":"), ensure_ascii=True) + "\n").encode())


def new_directory(path):
    write_path(path).mkdir(parents=True, exist_ok=False)


def require_wait(receipt, kind):
    if (receipt.get("kind") != kind or receipt.get("waited") is not True
            or type(receipt.get("actualWaitExit")) is not int
            or type(receipt.get("ownedPid")) is not int or receipt["ownedPid"] <= 0
            or receipt.get("technical") is not None):
        raise GateFailure(97, "child-wait-closure")


def bounded_child(argv, environment, directory, label, stdout_cap, stderr_cap, seconds,
                  kind, persist_output=True, persist_argv=True):
    started = time.monotonic()
    output = {"stdout": bytearray(), "stderr": bytearray()}
    caps = {"stdout": stdout_cap, "stderr": stderr_cap}
    child = None
    technical = None
    exit_code = None
    waited = False
    try:
        child = subprocess.Popen(argv, cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=False)
        with selectors.DefaultSelector() as selector:
            for name, pipe in (("stdout", child.stdout), ("stderr", child.stderr)):
                os.set_blocking(pipe.fileno(), False)
                selector.register(pipe, selectors.EVENT_READ, name)
            while selector.get_map():
                remaining = seconds - (time.monotonic() - started)
                if remaining <= 0:
                    technical = "child-timeout"
                    break
                for key, _ in selector.select(min(0.1, remaining)):
                    name = key.data
                    block = os.read(key.fileobj.fileno(), min(4096, caps[name] - len(output[name]) + 1))
                    if not block:
                        selector.unregister(key.fileobj)
                        continue
                    if len(output[name]) + len(block) > caps[name]:
                        technical = name + "-byte-cap"
                        break
                    output[name].extend(block)
                if technical is not None:
                    break
        if technical is None:
            try:
                exit_code = child.wait(timeout=max(0.01, seconds - (time.monotonic() - started)))
                waited = True
            except subprocess.TimeoutExpired:
                technical = "child-timeout"
    except (OSError, subprocess.SubprocessError):
        technical = "child-spawn-or-io"
    finally:
        if child is not None:
            if not waited:
                if child.poll() is None:
                    child.terminate()
                try:
                    exit_code = child.wait(timeout=LIMITS["terminationWaitSeconds"])
                    waited = True
                except subprocess.TimeoutExpired:
                    child.kill()
                    try:
                        exit_code = child.wait(timeout=LIMITS["terminationWaitSeconds"])
                        waited = True
                    except subprocess.TimeoutExpired:
                        technical = "owned-child-wait-incomplete"
            child.stdout.close()
            child.stderr.close()
    receipt = {
        "kind": kind, "ownedPid": None if child is None else child.pid, "waited": waited,
        "actualWaitExit": exit_code, "technical": technical,
        "stdoutBytes": len(output["stdout"]), "stderrBytes": len(output["stderr"]),
        "elapsedSeconds": round(time.monotonic() - started, 3),
    }
    if persist_argv:
        receipt["argv"] = argv
    if technical is None and persist_output:
        receipt["stdout"] = write_new(directory / (label + ".stdout"), bytes(output["stdout"]), stdout_cap)
        receipt["stderr"] = write_new(directory / (label + ".stderr"), bytes(output["stderr"]), stderr_cap)
    pin = write_json(directory / (label + ".wait.json"), receipt)
    return receipt, pin, bytes(output["stdout"]), bytes(output["stderr"])


def apply_edit(raw, control):
    before, after = control["before"].encode(), control["after"].encode()
    if raw.count(before) != 1:
        raise GateFailure(95, "actual-production-anchor")
    mutant = raw.replace(before, after, 1)
    if len(mutant) != control["mutantBytes"] or digest(mutant) != control["mutantSha256"]:
        raise GateFailure(95, "actual-production-mutant-pin")
    return mutant


def static_wiring(contents, seal, inverses):
    caller = contents["Assets/Scripts/Gameplay/GameSimulation.Source.cs"].decode()
    if caller.count(seal["callerAnchor"]) != 1:
        raise GateFailure(97, "touchdown_caller_wiring")
    for row in inverses["descendants"]:
        path = row["path"]
        field = ("touchdown_snapshot_wiring" if path.endswith("/GameSnapshot.cs")
                 else "touchdown_owner_inverse" if path.endswith("/SimulationState.cs")
                 else "touchdown_contract_inverse")
        restored = contents[path]
        for edit in row["edits"]:
            addition = edit["new"].encode()
            if restored.count(addition) != 1:
                raise GateFailure(97, field)
            restored = restored.replace(addition, edit["old"].encode(), 1)
        if len(restored) != row["inverse"]["bytes"] or digest(restored) != row["inverse"]["sha256"]:
            raise GateFailure(97, field)


def component_result(data, exit_code, seal, control=None):
    try:
        lines = data.decode("ascii").splitlines()
    except UnicodeError:
        raise GateFailure(97, "component-protocol") from None
    cases, fields = [], []
    summary = None
    current_case = None
    failures = 0
    for index, line in enumerate(lines):
        parts = line.split("|")
        if len(parts) == 2 and parts[0] == "CASE" and summary is None:
            current_case = parts[1]
            cases.append(current_case)
        elif len(parts) == 5 and parts[0] == "ASSERT" and summary is None:
            _, name, expected, actual, status = parts
            if (current_case is None or not name.startswith(current_case + ".")
                    or len(name) > 128 or max(len(expected), len(actual)) > 64
                    or status not in ("PASS", "FAIL") or (expected == actual) != (status == "PASS")):
                raise GateFailure(97, "component-protocol")
            failures += status == "FAIL"
            fields.append((name, expected, actual))
        elif len(parts) == 5 and parts[0] == "SUMMARY" and index == len(lines) - 1:
            if any(re.fullmatch(r"[0-9]{1,3}", token) is None for token in parts[1:]):
                raise GateFailure(97, "component-summary")
            summary = [int(token) for token in parts[1:]]
        else:
            raise GateFailure(97, "component-protocol")
    if cases != seal["caseIds"]:
        raise GateFailure(97, "component-case-roster")
    if [[name, expected] for name, expected, _ in fields] != seal["assertions"]:
        raise GateFailure(97, "component-field-roster")
    if summary != [28, 78, failures, 0]:
        raise GateFailure(97, "component-summary")
    if control is None:
        for name, expected, actual in fields:
            if actual != expected:
                raise GateFailure(97, "component-literal-" + name)
        if exit_code != 0:
            raise GateFailure(97, "component-clean-exit")
    else:
        selected = next((row for row in fields if row[0] == control["requiredFailure"]), None)
        if (exit_code != 1 or failures == 0 or selected is None
                or selected[2] == selected[1] or selected[2] != control["expectedActual"]):
            raise GateFailure(97, "mutant-selected-literal-failure")
        if any(expected != actual and not (name.startswith("T") or name.startswith("C08."))
               for name, expected, actual in fields):
            raise GateFailure(97, "mutant-preservation-field")
    return {"cases": len(cases), "assertions": len(fields), "failures": failures,
            "selected": None if control is None else list(selected)}


def verify_inputs(packet):
    production = metadata_pin(packet["production"], CAMPAIGN + "/production-inputs.json")
    if (len(production) != 77 or len({row["path"] for row in production}) != 77
            or any(not row["path"].startswith(ROOT + "/Assets/Scripts/")
                   or not row["path"].endswith(".cs") for row in production)):
        raise GateFailure(95, "ordered77-production")
    contents = {row["path"][len(ROOT) + 1:]: read_pin(row, 2097152) for row in production}
    main = packet["newMain"]
    if main["path"] != ROOT + "/tests/source-touchdown-stats/Program.cs":
        raise GateFailure(95, "only-new-main")
    read_pin(main, 65536)
    expected_tools = {
        "dotnet": "/home/long2know/.dotnet/dotnet",
        "compiler": "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll",
        "python": "/usr/bin/python3.12",
    }
    if set(packet["tools"]) != set(expected_tools):
        raise GateFailure(95, "tools")
    for name, path in expected_tools.items():
        if packet["tools"][name]["path"] != path:
            raise GateFailure(95, "tool-path")
        read_pin(packet["tools"][name])
    references = {}
    for key, count, directory in (
        ("net10", 167, "/home/long2know/.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0/"),
        ("ns21", 121, "/dev/shm/ap.bweV08/t18/pkg/netstandard.library.ref/2.1.0/ref/netstandard2.1/"),
    ):
        rows = metadata_pin(packet["references"][key], CAMPAIGN + "/references-" + key + ".json")
        if len(rows) != count or len({row["path"] for row in rows}) != count:
            raise GateFailure(95, "reference-count")
        for row in rows:
            if not row["path"].startswith(directory) or "/" in row["path"][len(directory):]:
                raise GateFailure(95, "reference-directory")
            read_pin(row)
        references[key] = rows
    catalogue = metadata_pin(packet["catalogue"], CAMPAIGN + "/proposed57-catalogue.json")
    if (catalogue.get("accepted") is not False or len(catalogue["paths"]) != 57
            or len({row["path"] for row in catalogue["paths"]}) != 57):
        raise GateFailure(95, "unaccepted57-catalogue")
    for row in catalogue["paths"]:
        if row["path"].startswith("/") or ".." in Path(row["path"]).parts:
            raise GateFailure(95, "catalogue-path")
        read_pin(dict(row, path=ROOT + "/" + row["path"]), 2097152)
    seal = metadata_pin(packet["literalSeal"], CAMPAIGN + "/literals-anchors-sealed.json")
    inverses = metadata_pin(packet["productionInverses"], CAMPAIGN + "/production-inverses.json")
    if (seal.get("stage") != "BEFORE_OLD_RUNNER_HELPER_COMPILER_REFERENCE_BODY_READS"
            or seal.get("caseCount") != 28 or seal.get("expectedAssertionCount") != 78
            or seal.get("oldBodiesReadBeforeSeal") is not False):
        raise GateFailure(95, "literal-anchor-precedence")
    if packet["newMain"]["sha256"] != next(
            row["sha256"] for row in seal["productionAndMain"] if row["path"].endswith("/Program.cs")):
        raise GateFailure(95, "independent-main-pin")
    for key, expected_rows, id_prefix, count in (
        ("mutations", seal["mutations"], "M", 4),
        ("staticControls", seal["staticControls"], "S", 3),
    ):
        controls = packet[key]
        if [row["id"] for row in controls] != [id_prefix + str(i).zfill(2) for i in range(1, count + 1)]:
            raise GateFailure(95, "control-roster")
        for control, expected in zip(controls, expected_rows):
            if any(control.get(name) != value for name, value in expected.items()):
                raise GateFailure(95, "sealed-control-anchor")
    return production, contents, references, seal, inverses


def artifact_budget(execution):
    count = total = 0
    for parent, directories, files in os.walk(execution, followlinks=False):
        if len(directories) > 32:
            raise GateFailure(97, "artifact-directory-cap")
        for name in directories + files:
            if (Path(parent) / name).is_symlink():
                raise GateFailure(97, "artifact-symlink")
        for name in files:
            count += 1
            total += (Path(parent) / name).stat().st_size
            if count > LIMITS["artifactFiles"] or total > LIMITS["artifactBytes"]:
                raise GateFailure(97, "artifact-total-cap")
    return {"files": count, "bytes": total}


def campaign(packet, execution, environment):
    production, contents, references, seal, inverses = verify_inputs(packet)
    static_wiring(contents, seal, inverses)
    new_directory(execution)
    for name in ("home", "scratch", "cache", "config", "xdg-data", "dotnet-home",
                 "nuget-packages", "nuget-http-cache"):
        new_directory(Path(CAMPAIGN) / "runtime" / name)
    report = {
        "schema": "source-touchdown-stats-execution/v1", "status": "INCOMPLETE",
        "compilerStarts": 0, "managedStarts": 0, "waits": [], "staticControls": [],
        "compiledControls": [], "accepted": False, "parentEntryWaitPending": True,
        "qualification": "Pure managed component and static adapter evidence only; no Game/Capture/native/Unity or whole-game qualification.",
    }
    started = time.monotonic()

    def fuel():
        if time.monotonic() - started > LIMITS["controllerEnvelopeSeconds"]:
            raise GateFailure(97, "controller-envelope")
        artifact_budget(Path(CAMPAIGN))

    def compile_one(label, control=None, ns21=False):
        fuel()
        if report["compilerStarts"] >= LIMITS["compilerStarts"]:
            raise GateFailure(97, "compiler-start-cap")
        directory = execution / label
        new_directory(directory)
        assembly = directory / (label + ".dll")
        sources = [row["path"] for row in production]
        if control is not None:
            original = contents[control["path"]]
            mutant = apply_edit(original, control)
            overlay = directory / Path(control["path"]).name
            write_new(overlay, mutant, 2097152)
            target = ROOT + "/" + control["path"]
            sources[sources.index(target)] = str(overlay)
        argv = [
            packet["tools"]["dotnet"]["path"], packet["tools"]["compiler"]["path"],
            "-nologo", "-noconfig", "-nostdlib+", "-langversion:7.3", "-warnaserror+",
            "-deterministic+", "-debug-", "-target:" + ("library" if ns21 else "exe"),
            "-out:" + str(write_path(assembly)),
        ]
        if not ns21:
            argv.append("-main:" + MAIN)
        argv += ["-r:" + row["path"] for row in references["ns21" if ns21 else "net10"]]
        argv += sources
        if not ns21:
            argv.append(packet["newMain"]["path"])
        report["compilerStarts"] += 1
        receipt, pin, _, _ = bounded_child(
            argv, environment, directory, "compiler", LIMITS["compilerStdoutBytes"],
            LIMITS["compilerStderrBytes"], LIMITS["compilerSeconds"], "compiler")
        report["waits"].append(pin)
        require_wait(receipt, "compiler")
        if receipt["actualWaitExit"] != 0 or not assembly.is_file() or assembly.is_symlink():
            raise GateFailure(97, "clean-compilation-required-not-a-kill")
        if assembly.stat().st_size > LIMITS["assemblyBytes"]:
            raise GateFailure(97, "assembly-byte-cap")
        if not ns21:
            write_json(directory / (label + ".runtimeconfig.json"), {
                "runtimeOptions": {"tfm": "net10.0", "framework": {
                    "name": "Microsoft.NETCore.App", "version": "10.0.0"}}})
        fuel()
        return assembly

    def managed_one(assembly, control=None):
        fuel()
        if report["managedStarts"] >= LIMITS["managedStarts"]:
            raise GateFailure(97, "managed-start-cap")
        report["managedStarts"] += 1
        receipt, pin, stdout, _ = bounded_child(
            [packet["tools"]["dotnet"]["path"], str(assembly)], environment, assembly.parent,
            "component", LIMITS["managedStdoutBytes"], LIMITS["managedStderrBytes"],
            LIMITS["managedSeconds"], "managed")
        report["waits"].append(pin)
        require_wait(receipt, "managed")
        result = component_result(stdout, receipt["actualWaitExit"], seal, control)
        fuel()
        return result

    try:
        report["baseline"] = managed_one(compile_one("baseline"))
        for control in packet["staticControls"]:
            mutated = dict(contents)
            mutated[control["target"]] = apply_edit(contents[control["target"]], control)
            try:
                static_wiring(mutated, seal, inverses)
            except GateFailure as error:
                if error.field != control["requiredFailure"]:
                    raise GateFailure(97, "static-control-wrong-field") from None
            else:
                raise GateFailure(97, "static-control-survived")
            report["staticControls"].append({
                "id": control["id"], "field": control["requiredFailure"], "qualification": "STATIC_ONLY"})
        for control in packet["mutations"]:
            result = managed_one(compile_one(control["id"], control), control)
            report["compiledControls"].append({"id": control["id"], "selected": result["selected"],
                                               "cleanCompilation": True})
        compile_one("ns21", ns21=True)
        _, final_contents, _, _, _ = verify_inputs(packet)
        static_wiring(final_contents, seal, inverses)
        if (report["compilerStarts"] != 6 or report["managedStarts"] != 5
                or len(report["staticControls"]) != 3 or len(report["compiledControls"]) != 4):
            raise GateFailure(97, "exact-start-and-control-counts")
        report["status"] = "COMPONENT_STATIC_NS21_COMPLETE_PARENT_WAIT_AND_QUALITY_PENDING"
    except (GateFailure, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        report["status"] = "FAILED_OR_INCONCLUSIVE"
        report["failureField"] = error.field if isinstance(error, GateFailure) else type(error).__name__
        write_json(execution / "driver-result.json", report)
        raise
    report["artifactsBeforeReport"] = artifact_budget(Path(CAMPAIGN))
    write_json(execution / "driver-result.json", report)
    artifact_budget(Path(CAMPAIGN))
    return report


def main(argv=None):
    try:
        environment = safe_entry()
        args = arguments(sys.argv[1:] if argv is None else argv)
        packet, execution = load_release(args)
        report = campaign(packet, execution, environment)
        print(json.dumps({"status": report["status"], "compilerStarts": report["compilerStarts"],
                          "managedStarts": report["managedStarts"], "cases": 28, "assertions": 78,
                          "accepted": False, "parentEntryWaitPending": True}), flush=True)
        return 0
    except GateFailure as error:
        print(json.dumps({"status": "REJECTED_OR_INCONCLUSIVE", "field": error.field,
                          "metadataReads": METADATA_READS, "compilerOrManagedKillCredit": False}), flush=True)
        return error.code
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(json.dumps({"status": "TECHNICAL_INCONCLUSIVE", "exceptionType": type(error).__name__,
                          "metadataReads": METADATA_READS, "compilerOrManagedKillCredit": False}), flush=True)
        return 97


if __name__ == "__main__":
    sys.exit(main())
