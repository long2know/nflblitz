#!/usr/bin/env python3
"""Focused real production storage/codec checks; no downloads or native ABI claims."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / ".squad/.scratch/original-records-20261030"
REFERENCE = ROOT.parent / "reference/nflblitz2000"
PINS = {
    "cap.c": "42605264092a30f861f5390535bbe217aac13b66",
    "include/cap.h": "79e6a1f726f7e55ee35d7de9b1a58cf21027ba18",
    "include/PlayStuf.h": "3df5e8e8a3f478c3b02860a401c950d1e83c5ad3",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "lib/ani3d/TRIGS.H": "a07d76798931f1240bdf7f3b8e753ebd7832763a",
    "lib/select/record.c": "b0eb18e3440967c767a4cc98874a16f34183a617",
    "include/nflcmos.h": "5b7e01c42ba321bdc7073f2954f6489216767cdf",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
}


def original(roots, name):
    for root in roots:
        path = root / name
        if not path.is_file():
            continue
        data = path.read_bytes()
        observed = hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()
        if observed != PINS[name]:
            raise RuntimeError("Pinned original mismatch: " + name)
        return data.decode("latin1")
    raise RuntimeError("Missing authenticated original helper: " + name)


def function(text, name):
    match = re.search(r"(?:static\s+)?int\s+" + re.escape(name) + r"\s*\([^;{}]*\)\s*\{", text)
    if not match:
        raise RuntimeError("Missing exact original function " + name)
    opening = text.index("{", match.start())
    depth = 1
    at = opening + 1
    while depth:
        if text[at] == "{":
            depth += 1
        elif text[at] == "}":
            depth -= 1
        at += 1
    return text[match.start():at]


def oracle(roots, output):
    source = {name: original(roots, name) for name in PINS}
    definitions = "\n".join(
        line for name in ("include/cap.h", "include/PlayStuf.h")
        for line in source[name].splitlines()
        if re.match(r"\s*#define\s+(?:CPPT_|DS_)", line)
    )
    prelude = """
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#include <math.h>
#define FALSE 0
#define TRUE 1
#define fsqrt sqrtf
typedef struct { unsigned char x,y,flags; } cap_point_t;
"""
    body = "\n".join(function(source["lib/ani3d/pipe3D.c"], name) for name in ("iasin", "iacos"))
    body += "\n" + "\n".join(function(source["cap.c"], name) for name in ("dist_from", "angle_from", "convert_route"))
    main = """
int main(int argc, char **argv) {
    unsigned char bytes[101];
    if (sizeof(int)!=4 || sizeof(float)!=4 || argc!=2 || strlen(argv[1])!=202) return 2;
    for (int i=0;i<101;i++) {
        unsigned value;
        if (sscanf(argv[1]+i*2,"%2x",&value)!=1) return 3;
        bytes[i]=(unsigned char)value;
    }
    putchar('[');
    for (int r=0;r<3;r++) {
        cap_point_t points[9]; int words[512];
        for (int p=0;p<9;p++) {
            int at=20+r*27+p*3;
            points[p].x=bytes[at];points[p].y=bytes[at+1];points[p].flags=bytes[at+2];
        }
        int count=convert_route(points,words);
        if(r)putchar(',');putchar('[');
        for(int i=0;i<count;i++)printf("%s%d",i?",":"",words[i]);
        putchar(']');
    }
    puts("]");return 0;
}
"""
    path = output / "original-cap-route.c"
    path.write_text(prelude + definitions + "\n" + source["lib/ani3d/TRIGS.H"] + "\n" + body + "\n" + main)
    executable = output / "original-cap-route"
    command = ["cc", "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off", str(path), "-o", str(executable), "-lm"]
    subprocess.run(command, cwd=ROOT, check=True)
    return executable, command


def negative_controls(sdk, env, run, catalog, executable):
    cases = (
        ("cap-angle-sign", "Assets/Scripts/Storage/SourceCustomPlayCodec.cs", "SourceCustomPlayCodecPath",
         "new SourcePlanar(-current.X, current.Y), new SourcePlanar(-next.X, next.Y)",
         "new SourcePlanar(current.X, current.Y), new SourcePlanar(next.X, next.Y)",
         "Managed route words must equal original C"),
        ("opposite-interceptions", "Assets/Scripts/Storage/SourcePlayerRecords.cs", "SourcePlayerRecordsPath",
         "side == 0 ? final.Interceptions.AwayInterceptionsMade : final.Interceptions.HomeInterceptionsMade",
         "side == 0 ? final.Interceptions.HomeInterceptionsMade : final.Interceptions.AwayInterceptionsMade",
         "Source record writer must preserve exact asymmetric original counters"),
        ("premature-memory-commit", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
         "if (!await storage.CompareExchangeAsync(image.Primary, replacement))",
         "Current = next;\n            if (!await storage.CompareExchangeAsync(image.Primary, replacement))",
         "The actual async browser write adapter must retain old state"),
        ("world-wrong-full-mask", "Assets/Scripts/Storage/SourcePlayerRecords.cs", "SourcePlayerRecordsPath",
         "if (station.Identity != null)",
         "if (station.Identity != null && match.QuarterParticipation[stationId] == fullMask)",
         "World helper credits an actually named partial station"),
        ("missing-v2-worlds-accepted", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
         "if (number >= 2 && (payload.WorldRecords == null",
         "if (number >= 3 && (payload.WorldRecords == null",
         "A version2 envelope missing its world field is corrupt"),
        ("recovery-only-is-empty", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
         "image.Backup == null && image.Recovery == null",
         "image.Backup == null",
         "Recovery-only storage is evidence of a lost primary"),
        ("culture-aliased-career", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
         "StringComparer.Ordinal.Equals(item.Key, key)",
         "c.Comparer.Compare(item.Key, key) == 0",
         "Integrity-valid culture aliases cannot replace exact source career keys"),
        ("collapsed-career-aliases", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
         "career.EnumerateObject().Count() != 17",
         "false",
         "Integrity-valid extra career aliases must not collapse into valid fields"),
        ("whitespace-receipt", "Assets/Scripts/Contracts/SourceSavedRecordContracts.cs", "SourceSavedRecordContractsPath",
         "sessionId == null || sessionId.Length != 32 || ",
         "",
         "A whitespace-aliased receipt must not import as an unmatched ID"),
    )
    results = []
    originals = {name: (ROOT / name).read_bytes() for _, name, *_ in cases}
    for name, relative, property_name, old, new, expected in cases:
        source = originals[relative].decode()
        if source.count(old) != 1:
            raise RuntimeError("Negative-control source anchor must occur exactly once: " + name)
        directory = run / "negative-controls" / name
        directory.mkdir(parents=True)
        changed = directory / Path(relative).name
        changed.write_text(source.replace(old, new))
        properties = ["-p:SourceRecordsBuildFlavor=mutants/" + run.name + "/" + name + "/",
            "-p:SourceRecordsOutputRoot=" + str(ARTIFACTS) + "/",
            "-p:" + property_name + "=" + str(changed)]
        project = "tests/records/SourceRecords.Checks.csproj"
        build = [sdk, "build", project, "-c", "Release", "--no-restore", "--nologo", "-v", "quiet"] + properties
        built = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
        if built.returncode and "NETSDK1004" in built.stdout + built.stderr:
            subprocess.run([sdk, "restore", project, "--nologo", "-v", "quiet"] + properties,
                cwd=ROOT, env=env, check=True)
            built = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
        (directory / "build.log").write_text(built.stdout + built.stderr)
        if built.returncode:
            raise RuntimeError("Negative control must compile, not fail at build: " + name)
        dll = ARTIFACTS / "build/mutants" / run.name / name / "checks/bin/Release/net10.0/SourceRecords.Checks.dll"
        checked = subprocess.run([sdk, str(dll), str(catalog), str(directory), str(executable)],
            cwd=ROOT, env=env, text=True, capture_output=True)
        output = checked.stdout + checked.stderr
        (directory / "checks.log").write_text(output)
        if checked.returncode == 0 or expected not in output:
            raise RuntimeError("Negative control missed its actual behavioral gate: " + name)
        results.append({"name": name, "buildExit": built.returncode, "checksExit": checked.returncode,
            "rejectedBy": expected, "mutatedCopySha256": hashlib.sha256(changed.read_bytes()).hexdigest()})
    for relative, content in originals.items():
        if (ROOT / relative).read_bytes() != content:
            raise RuntimeError("Canonical owned source changed during isolated negative controls.")
    return results


def custom_block_history_checks(sdk, env, run, mutations):
    relatives = [
        "Assets/Scripts/Storage/SourcePlayerRecords.cs", "Assets/Scripts/Storage/SourceCustomPlayCodec.cs",
        "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "tests/records/Program.cs",
        "tests/records/SourceRecords.Checks.csproj", "tests/records/SourceRecords.Production.csproj",
        "tests/records/run.py",
    ]
    relatives += [str(path.relative_to(ROOT)) for path in (ROOT / "Assets/Scripts/Contracts").glob("*.cs")]
    relatives += ["Assets/Scripts/Gameplay/" + name + ".cs" for name in
        ("SourceClock", "SourceMatchRules", "SourceMatchFlow", "SourcePlaybook", "SourceDroneAI", "SourceRandom")]
    originals = {relative: (ROOT / relative).read_bytes() for relative in relatives}
    commands = []

    def execute(name, property_name=None, changed=None, rejected_by=None):
        directory = run / name
        directory.mkdir()
        properties = ["-p:SourceRecordsOutputRoot=" + str(ARTIFACTS) + "/",
            "-p:SourceRecordsBuildFlavor=history/" + name + "/"]
        if changed:
            relative, source = changed
            copy = directory / Path(relative).name
            copy.write_text(source)
            properties.append("-p:" + property_name + "=" + str(copy))
        project = "tests/records/SourceRecords.Checks.csproj"
        build = [sdk, "build", project, "-c", "Release", "--no-restore", "--nologo", "-v", "quiet", "-t:Rebuild"] + properties
        built = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
        (directory / "build-first.log").write_text(built.stdout + built.stderr)
        restore = None
        if built.returncode and "NETSDK1004" in built.stdout + built.stderr:
            restore = [sdk, "restore", project, "--nologo", "-v", "quiet"] + properties
            restored = subprocess.run(restore, cwd=ROOT, env=env, text=True, capture_output=True)
            (directory / "restore.log").write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("History restore failed: " + name)
            built = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
        (directory / "build.log").write_text(built.stdout + built.stderr)
        if built.returncode:
            raise RuntimeError("History checks/control must compile: " + name + "\n" + built.stdout + built.stderr)
        dll = ARTIFACTS / "build/history" / name / "checks/bin/Release/net10.0/SourceRecords.Checks.dll"
        checks = [sdk, str(dll), "--custom-block-history"]
        checked = subprocess.run(checks, cwd=ROOT, env=env, text=True, capture_output=True)
        output = checked.stdout + checked.stderr
        (directory / "checks.log").write_text(output)
        if rejected_by:
            if checked.returncode == 0 or rejected_by not in output:
                raise RuntimeError("History semantic control missed its actual assertion: " + name)
            observed = {"rejectedBy": rejected_by, "mutatedCopySha256": hashlib.sha256(source.encode()).hexdigest()}
        else:
            print(output, end="")
            if checked.returncode:
                raise RuntimeError("History behavior checks failed: " + name)
            observed = json.loads(checked.stdout.strip().splitlines()[-1])
        commands.append({"name": name, "buildCommand": build, "restoreCommand": restore, "checksCommand": checks,
            "buildExit": built.returncode, "checksExit": checked.returncode, "observed": observed})

    execute("baseline")
    if mutations:
        cases = (
            ("empty-cap-acquisition-lost", "tools/source-player/Runtime/SourcePlayerRecordStore.cs", "SourceRecordStorePath",
             "return current.CommitCustomBlock(identity);", "return current;",
             "Successful CAP empty-block commit records acquisition"),
            ("never-allocated-exists-accepted", "Assets/Scripts/Contracts/SourceSavedRecordContracts.cs", "SourceSavedRecordContractsPath",
             "customBlockState == SourceCustomBlockState.NeverAllocated && copy[i] != null && copy[i].Exists",
             "customBlockState == (SourceCustomBlockState)3 && copy[i] != null && copy[i].Exists",
             "NeverAllocated with an actual Exists slot is contradictory"),
        )
        for name, relative, property_name, old, new, expected in cases:
            source = originals[relative].decode()
            if source.count(old) != 1:
                raise RuntimeError("History semantic-control anchor must occur exactly once: " + name)
            execute(name, property_name, (relative, source.replace(old, new)), expected)
        execute("final-fresh")
    for relative, content in originals.items():
        if (ROOT / relative).read_bytes() != content:
            raise RuntimeError("History canonical compiled input changed during checks: " + relative)
    receipt = {"schema": "source-custom-block-history-checks/v1", "status": "passed",
        "inputs": {name: hashlib.sha256(content).hexdigest() for name, content in sorted(originals.items())},
        "commands": commands,
        "scope": "Production C#7.3 contracts/domain and explicit v3 store serialization/migration/atomicity/recovery; "
            "not public CAP menu, browser IndexedDB/AOT or native disk/CRC execution",
        "output": str(run)}
    (run / "result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    (ARTIFACTS / "latest-result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("Receipt: " + str(run / "result.json"))


def main():
    global ARTIFACTS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, help="Existing admitted original data; required except for --custom-block-history")
    parser.add_argument("--original-root", type=Path, action="append", default=[], help="Additional exact pinned original reference root")
    parser.add_argument("--output", type=Path, default=ARTIFACTS, help="Explicit owned artifact root; choose a fresh dated path to retain earlier evidence")
    parser.add_argument("--mutations", action="store_true", help="Compile isolated actual-source semantic negative controls")
    parser.add_argument("--custom-block-history", action="store_true", help="Only new opt-in v3 history/migration/failure checks; no original assets or old suites")
    args = parser.parse_args()
    if not args.custom_block_history and args.data_root is None:
        parser.error("--data-root is required except for --custom-block-history")
    ARTIFACTS = args.output.resolve()
    roots = args.original_root + [ARTIFACTS / "original", ROOT / ".squad/.scratch/reference/nflblitz2000", REFERENCE,
        ROOT / ".squad/.scratch/original-native-parity-20261030/original/nflblitz2000"]
    run = ARTIFACTS / "runs" / uuid.uuid4().hex
    run.mkdir(parents=True)
    for directory in ("sdk-scratch", "cli", "nuget"):
        (ARTIFACTS / directory).mkdir(exist_ok=True)
    sdk = os.environ.get("SOURCE_PLAYER_DOTNET") or shutil.which("dotnet")
    if not sdk:
        candidate = ROOT / "tools/source-player/.runtime/dotnet/dotnet"
        sdk = str(candidate) if candidate.is_file() else None
    if not sdk:
        raise RuntimeError("Existing dotnet SDK is required; runner never installs one.")
    env = dict(os.environ, TMPDIR=str(ARTIFACTS / "sdk-scratch"),
        DOTNET_CLI_HOME=str(ARTIFACTS / "cli"), NUGET_PACKAGES=str(ARTIFACTS / "nuget"),
        DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true", DOTNET_NOLOGO="1")
    if args.custom_block_history:
        custom_block_history_checks(sdk, env, run, args.mutations)
        return
    executable, c_command = oracle(roots, run)
    project = "tests/records/SourceRecords.Checks.csproj"
    properties = ["-p:SourceRecordsOutputRoot=" + str(ARTIFACTS) + "/"]
    build = [sdk, "build", project, "-c", "Release", "--no-restore", "--nologo", "-v", "quiet"] + properties
    result = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
    (run / "build-first.log").write_text(result.stdout + result.stderr)
    restore = None
    if result.returncode and "NETSDK1004" in result.stdout + result.stderr:
        restore = [sdk, "restore", project, "--nologo", "-v", "quiet"] + properties
        subprocess.run(restore, cwd=ROOT, env=env, check=True)
        result = subprocess.run(build, cwd=ROOT, env=env, text=True, capture_output=True)
    (run / "build.log").write_text(result.stdout + result.stderr)
    print(result.stdout + result.stderr, end="")
    if result.returncode:
        raise RuntimeError("Focused production/checks build failed; see " + str(run / "build.log"))
    dll = ARTIFACTS / "build/checks/bin/Release/net10.0/SourceRecords.Checks.dll"
    catalog = args.data_root / "kernel/playbook/catalog.tsv"
    checks = [sdk, str(dll), str(catalog), str(run), str(executable)]
    checked = subprocess.run(checks, cwd=ROOT, env=env, text=True, capture_output=True)
    (run / "checks.log").write_text(checked.stdout + checked.stderr)
    print(checked.stdout + checked.stderr, end="")
    if checked.returncode:
        raise RuntimeError("Focused behavior checks failed; see " + str(run / "checks.log"))
    fresh = [sdk, str(dll), "--fresh-process", str(run / "file-store")]
    reloaded = subprocess.run(fresh, cwd=ROOT, env=env, text=True, capture_output=True)
    (run / "fresh-process.log").write_text(reloaded.stdout + reloaded.stderr)
    print(reloaded.stdout + reloaded.stderr, end="")
    if reloaded.returncode:
        raise RuntimeError("Fresh-process durable reload failed.")
    mutations = negative_controls(sdk, env, run, catalog, executable) if args.mutations else []
    if mutations:
        print("Compiled semantic negative controls: " + str(len(mutations)) + " rejected")
    receipt = {"schema": "source-original-record-checks/v1", "status": "passed",
        "sourcePins": PINS, "oracleCommand": c_command, "buildCommand": build, "restoreCommand": restore,
        "checksCommand": checks, "freshProcessCommand": fresh,
        "checks": json.loads(checked.stdout.strip().splitlines()[-1]),
        "freshProcess": json.loads(reloaded.stdout.strip().splitlines()[-1]),
        "semanticNegativeControls": mutations,
        "scope": "Source-derived grid/route/record logic, hosted original-C numerical comparison, atomic local storage; not executed native ABI/CMOS or browser integration",
        "output": str(run)}
    (run / "result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    (ARTIFACTS / "latest-result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("Receipt: " + str(run / "result.json"))


if __name__ == "__main__":
    main()
