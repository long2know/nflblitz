#!/usr/bin/env python3
"""Pure SDK checks by default; original C oracle and actual Unity refs are explicit."""
import argparse
import hashlib
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = HERE / ".artifacts"
PROJECT = HERE / "SourceAudioFunctions.Checks.csproj"
DATA = ROOT / "Assets/Scripts/AssetImport/SourceAudioFunctionData.cs"
SELECTOR = ROOT / "Assets/Scripts/Presentation/SourceAudioFunctions.cs"
PRODUCER = ROOT / "tools/audio/prepare_functions.py"


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_producer():
    spec = importlib.util.spec_from_file_location("audio_functions", PRODUCER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def environment():
    scratch = OUT / "build-scratch"
    scratch.mkdir(parents=True, exist_ok=True)
    return dict(os.environ, TMPDIR=str(scratch), DOTNET_NOLOGO="1",
                DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1")


def run(args, label, check=True, stdin=None):
    result = subprocess.run([str(x) for x in args], cwd=ROOT, env=environment(),
                            input=stdin, text=True, capture_output=True)
    text = result.stdout + result.stderr
    (OUT / (label + ".log")).write_text(text)
    if check and result.returncode:
        raise RuntimeError(label + ":\n" + text[-6000:])
    return result, text


def support(core):
    path = core / "Assets/Scripts/Gameplay/SourcePlaybook.cs"
    text = path.read_text()
    start = text.index("internal static class SourceP1Guard")
    opening = text.index("{", start)
    depth = 0
    for at in range(opening, len(text)):
        depth += (text[at] == "{") - (text[at] == "}")
        if depth == 0:
            fragment = text[start:at + 1]
            break
    else:
        raise RuntimeError("actual Core guard not found")
    output = OUT / "ActualCoreSupport.cs"
    output.write_text("using System;\nnamespace Blitz.Gameplay {\n" + fragment + "\n}\n")
    return {"source": str(path), "source_sha256": sha(path), "fragment_sha256": sha(output),
            "claim": "Exact unmodified SourceP1Guard declaration from actual Core source, not a replacement API/guard. Avoids compiling unrelated playbook/AI implementations."}


def build(core, data=None, selector=None, label="build"):
    args = ["dotnet", "build", PROJECT, "--no-restore", "--verbosity", "quiet",
            "-p:BlitzSourceRoot=" + str(core)]
    if data: args.append("-p:AudioDataSource=" + str(data))
    if selector: args.append("-p:AudioFunctionsSource=" + str(selector))
    result, text = run(args, label, check=False)
    if result.returncode and "NETSDK1004" in text:
        feed = OUT / "empty-feed"; feed.mkdir(exist_ok=True)
        run(["dotnet", "restore", PROJECT, "--source", feed,
             "-p:NuGetAudit=false", "--verbosity", "quiet"], "restore-local-sdk")
        result, text = run(args, label, check=False)
    if result.returncode:
        raise RuntimeError("Compilation is not a mutation assertion:\n" + text[-5000:])


def execute(payload, oracle=None, label="checks", check=True):
    args = ["dotnet", HERE / "bin/Debug/net10.0/SourceAudioFunctions.Checks.dll", payload, sha(payload)]
    if oracle: args += list(oracle)
    result, text = run(args, label, check)
    return result, json.loads(text.strip()) if result.returncode == 0 else text


def synthetic(producer):
    roles = json.loads((ROOT / "tools/audio/function-inputs.json").read_text())["function_roles"]
    model = {
        "blobs": {name: "00" * 20 for name in ["sounds.c", "include/sndcalls.h", "include/player.h", "plyrseq.c"]},
        "bank": "players", "onscreen_flag": 64, "line_mode": 31, "drone_station": -5,
        "human": {"volume": 217, "primary_priority": 13, "secondary_priority": 16},
        "drone": {"volume": 163, "primary_priority": 19, "secondary_priority": 19},
        "tables": [], "sites": [], "bindings": [],
    }
    for function, names in roles.items():
        for n, role in enumerate(names):
            argument = (2 if role.endswith("Third") or role.endswith("GruntExplicit") else
                        1 if role.endswith("FlyExplicit") or role.endswith("NeckExplicit") else 0)
            random = "Random" in role or role == "HitLine"
            table = "synthetic_" + role if random else ""
            if random:
                model["tables"].append({"name": table, "line": 40 + n, "values": [1001, 1001, 1002]})
            model["sites"].append({
                "role": role, "function": function, "line": 100 + len(model["sites"]), "table": table,
                "argument_index": -1 if random else argument,
                "volume_mode": 1 if role.startswith("Ground") else 0,
                "volume": 0 if role.startswith("Ground") else 217 if "Carrier" in role and "Fly" not in role else 163,
                "pan": 123, "priority_mode": 1 if role.startswith("Ground") and "Grunt" not in role else 0,
                "priority": 17,
            })
    for n, (function, arguments) in enumerate([
            ("get_hit_snd", [-1, -1, 0]), ("get_hit_snd", [1007, 0, 0]),
            ("grnd_hit_snd", [-1, 0, -1]), ("grnd_hit_snd", [-1, 0, 0])]):
        model["bindings"].append({"path": "synthetic.c", "script": "synthetic", "function": function,
                                  "line": 400 + n, "arguments": arguments})
    path = OUT / "synthetic.safn"
    path.write_bytes(producer.encode(model))
    return path


def make_oracle(directory, model):
    executable = OUT / "oracle"
    result, text = run(["gcc", "-std=c11", "-O0", "-fwrapv", "-fno-strict-aliasing",
                       "-I", directory, HERE / "oracle.c", "-o", executable], "compile-actual-C")
    rows = []
    raw_patterns = [[n, n + 1, n + 2, 0, 1, 2] for n in range(12)]
    raw_patterns += [[2147483647, 2147483646, 2147483645, 7, 8, 9]]
    station = model["drone_station"]; line = model["line_mode"]; flag = model["onscreen_flag"]
    for index, binding in enumerate(model["bindings"]):
        function = int(binding["function"] == "grnd_hit_snd")
        for flags, mode, partner, primary, stations, raw in itertools.product(
                [0, flag, -1], [line, line + 1],
                [(0, line), (1, line), (1, line + 1)], range(3),
                [(station, station), (station, 0), (0, station), (-2147483648, 2147483647)], raw_patterns):
            present, partner_mode = partner
            if function == 0 and flags & flag and mode == line and not present:
                continue  # Original null lineman dereference is undefined, separately rejected by the pure selector.
            actor = len(rows) % 14
            carrier = actor if primary == 0 else -1
            receiver = actor if primary == 1 else -1
            values = [len(rows), function, flags, mode, actor, stations[0], present, partner_mode,
                      stations[1], carrier, receiver, *binding["arguments"], len(raw), index, *raw]
            rows.append(" ".join(str(value) for value in values))
    source = "\n".join(rows) + "\n"
    cases = OUT / "cases.txt"; cases.write_text(source)
    _, output = run([executable], "actual-C", stdin=source)
    oracle = OUT / "oracle.jsonl"; oracle.write_text(output)
    if len(output.splitlines()) != len(rows):
        raise RuntimeError("actual C oracle case count differs")
    return (cases, oracle), {
        "cases": len(rows), "compiler": subprocess.check_output(["gcc", "--version"], text=True).splitlines()[0],
        "binary_sha256": sha(executable), "fixture_sha256": sha(HERE / "oracle.c"),
        "unchanged_original_include_sha256": sha(directory / "original-functions.inc"),
        "case_input_sha256": sha(cases), "oracle_sha256": sha(oracle),
        "scope": "Actual unchanged pinned C bodies + original generated macros/tables, with recorded raw randrng draws and snd_scall_bank capture. No DSP/PRNG algorithm is substituted or claimed.",
    }


def unity_compile(editor_data, core):
    standard = editor_data / "NetStandard/ref/2.1.0"
    refs = sorted(standard.glob("*.dll"))
    if not (standard / "netstandard.dll").is_file():
        raise RuntimeError("explicit Unity reference directory is missing netstandard2.1")
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1]
    match = re.fullmatch(r"(\S+)\s+\[(.+)\]", sdk)
    compiler = Path(match[2]) / match[1] / "Roslyn/bincore/csc.dll"
    sources = list((core / "Assets/Scripts/Contracts").rglob("*.cs"))
    sources += [core / "Assets/Scripts/Gameplay" / name for name in
                ["SourceRandom.cs", "SourceMatchFlow.cs", "SourceClock.cs", "SourceMatchRules.cs"]]
    sources += [OUT / "ActualCoreSupport.cs", DATA, SELECTOR]
    result = []
    for define in ["UNITY_EDITOR", "UNITY_WEBGL"]:
        output = OUT / (define + ".dll")
        run(["dotnet", compiler, "-nologo", "-noconfig", "-nostdlib+", "-langversion:7.3",
             "-warnaserror+", "-deterministic+", "-target:library", "-define:" + define, "-out:" + str(output),
             *["-reference:" + str(path) for path in refs], *sources], "actual-api-" + define)
        result.append({"define": define, "sha256": sha(output), "errors": 0, "warnings": 0})
    return {"explicit_reference_root": str(editor_data), "compilations": result,
            "reference_sha256": {str(path.relative_to(editor_data)): sha(path) for path in refs},
            "profile": "C#7.3/netstandard2.1; pure actual Core/new selector sources; no Unity process launched"}


def mutations(core, fixture, original, oracle):
    directory = OUT / "mutations"; directory.mkdir(exist_ok=True)
    selector = SELECTOR.read_text(); data = DATA.read_text()
    specs = {
        "offscreen-fallthrough": (selector, SELECTOR, '& data.OnscreenFlag) == 0) return;', '& data.OnscreenFlag) != 0) return;'),
        "lineman-replaces-main-call": (selector, SELECTOR, 'Emit(SourceAudioSoundSite.HitLine);', '{ Emit(SourceAudioSoundSite.HitLine); return; }'),
        "carrier-and-receiver": (selector, SELECTOR,
            'Need(state.CurrentBallCarrier, "game_info.ball_carrier") ||', 'Need(state.CurrentBallCarrier, "game_info.ball_carrier") &&'),
        "ground-loses-human-short-circuit": (selector, SELECTOR, 'if (!human)', 'if (state.ActorStation.HasValue)'),
        "unknown-hit-is-absent": (selector, SELECTOR,
            'state.HitPartner.Presence == SourceAudioHitPresence.Unknown', '(int)state.HitPartner.Presence < 0'),
        "noncarrier-random-is-explicit": (selector, SELECTOR, 'Emit(SourceAudioSoundSite.HitOtherRandom);', 'Emit(SourceAudioSoundSite.HitOtherExplicit);'),
        "wrong-carrier-table-order": (selector, SELECTOR, 'Emit(SourceAudioSoundSite.HitCarrierRandom);', 'Emit(SourceAudioSoundSite.HitCarrierFlyRandom);'),
        "subcall-order-off-by-one": (selector, SELECTOR, 'template.Site, calls.Count,', 'template.Site, calls.Count + 1,'),
        "reset-Core-random-cursor": (selector, SELECTOR,
            'cursor = new SourceRandomCursor(recorder, context);',
            'cursor = new SourceRandomCursor(recorder, new SourceRandomContext(context.SourceTick, context.ActorId, context.Process, context.ProcessOrder, context.Profile));'),
        "unity-gain-approximation": (selector, SELECTOR,
            'template.VolumeMode == 0 ? template.Volume : branch.Volume', '(template.VolumeMode == 0 ? template.Volume : branch.Volume) / 2'),
        "drop-ground-grunt": (selector, SELECTOR, 'if (cue.Argument2 < 0) Emit(SourceAudioSoundSite.GroundGruntRandom, branch);',
            'if (cue.Argument2 > 0) Emit(SourceAudioSoundSite.GroundGruntRandom, branch);'),
        "wrong-priority-word": (selector, SELECTOR, 'template.PriorityMode == 1 ? branch.PrimaryPriority',
            'template.PriorityMode == 1 ? branch.SecondaryPriority'),
        "deduplicate-weights": (data, DATA, 'Values = Array.AsReadOnly(values);',
            'Values = Array.AsReadOnly(new List<int>(new HashSet<int>(values)).ToArray());'),
        "payload-pin-bypass": (data, DATA, 'if (hash != frozenPayloadSha256)',
            'if (!String.Equals(hash, hash, StringComparison.Ordinal))'),
    }
    records = []
    for name, (source, target, before, after) in specs.items():
        if source.count(before) != 1:
            raise RuntimeError("mutation anchor changed: " + name)
        path = directory / (name + ".cs"); path.write_text(source.replace(before, after))
        build(core, data=path if target == DATA else None, selector=path if target == SELECTOR else None,
              label="mutant-build-" + name)
        result, text = execute(fixture, label="mutant-" + name, check=False)
        if result.returncode == 0 or "CONTRACT_ASSERTION:" not in text:
            raise RuntimeError("mutant must fail an offending assertion, not compilation/exception: " + name + "\n" + str(text)[-2500:])
        records.append({"name": name, "source_sha256": sha(path), "compiled": True,
                        "offending_assertion": text.split("CONTRACT_ASSERTION:", 1)[1].splitlines()[0].strip()})
        path.unlink()
    build(core, label="post-mutant-baseline-build")
    execute(fixture, label="post-mutant-synthetic")
    if original: execute(original, oracle, "post-mutant-original")
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core-source-root", type=Path, default=ROOT)
    parser.add_argument("--original", type=Path, help="explicit ignored original function-data directory")
    parser.add_argument("--reproduce", help="fresh ignored producer output name")
    parser.add_argument("--mutate", action="store_true")
    parser.add_argument("--unity-editor-data", type=Path, help="optional actual Editor/Data references; NEVER launches Editor")
    args = parser.parse_args()
    OUT.mkdir(exist_ok=True)
    core = args.core_source_root.resolve()
    protected = ROOT / ".squad/.scratch/source-audio-frozen"
    baseline = {}
    if protected.exists():
        old = json.loads((protected / "source-audio-handoff.json").read_text())
        for name in old["permanent_files_sha256"]:
            baseline[str(protected / name)] = sha(protected / name)
        for name in ["Assets/Scripts/AssetImport/SourceAudioBankReader.cs.meta",
                     "Assets/Scripts/Presentation/SourceAudioIsolatedPlayer.cs.meta"]:
            baseline[str(protected / name)] = sha(protected / name)
    own = [DATA, SELECTOR, PRODUCER, ROOT / "tools/audio/function-inputs.json",
           PROJECT, HERE / ".gitignore", HERE / "Program.cs", HERE / "oracle.c",
           HERE / "test_producer.py", Path(__file__).resolve(),
           ROOT / "tools/audio/FUNCTIONS.md", ROOT / "tools/audio/sound-functions.schema.json"]
    own_before = {str(path.relative_to(ROOT)): sha(path) for path in own}
    core_files = sorted((core / "Assets/Scripts/Contracts").rglob("*.cs"))
    core_files += [core / "Assets/Scripts/Gameplay" / name for name in
                   ["SourceRandom.cs", "SourcePlaybook.cs", "SourceMatchFlow.cs", "SourceClock.cs", "SourceMatchRules.cs"]]
    core_before = {str(path): sha(path) for path in core_files}
    report = {"schema": "source-audio-function-validation/v1", "core_support": support(core),
              "core_source_sha256": core_before}
    _, text = run([sys.executable, "-m", "unittest", "discover", "-s", HERE, "-p", "test_producer.py"], "producer-tests")
    report["producer_tests"] = int(re.search(r"Ran (\d+) tests", text)[1])
    producer = load_producer()
    fixture = synthetic(producer)
    build(core)
    _, report["synthetic"] = execute(fixture, label="synthetic")
    original = oracle = None
    if args.original:
        directory = args.original.resolve()
        original = directory / "sound-functions.safn"
        model = json.loads((directory / "manifest.json").read_text())
        if sha(original) != model["payload_sha256"]:
            raise RuntimeError("original function payload pin changed")
        # Verify original source pins independently of any emitted data.
        fresh, _, _, _ = producer.produce()
        if producer.encode(fresh) != original.read_bytes():
            raise RuntimeError("function payload differs from pinned source")
        oracle, report["actual_C_oracle"] = make_oracle(directory, model)
        _, report["original"] = execute(original, oracle, "original")
        if args.reproduce:
            output = producer.write(args.reproduce)
            names = ["sound-functions.safn", "manifest.json", "original-functions.inc"]
            identical = {name: (output / name).read_bytes() == (directory / name).read_bytes() for name in names}
            if not all(identical.values()): raise RuntimeError("producer output is not deterministic")
            report["reproduction"] = {"output": str(output), "identical": identical}
    if args.unity_editor_data:
        report["actual_unity_api"] = unity_compile(args.unity_editor_data.resolve(), core)
    else:
        report["actual_unity_api"] = {"executed": False, "reason": "optional explicit --unity-editor-data not supplied; clean CI is SDK-only"}
    if args.mutate: report["mutations"] = mutations(core, fixture, original, oracle)
    if own_before != {str(path.relative_to(ROOT)): sha(path) for path in own}:
        raise RuntimeError("new production/test source changed during checks")
    if core_before != {str(path): sha(path) for path in core_files}:
        raise RuntimeError("actual Core input sources changed")
    if baseline != {path: sha(path) for path in baseline}:
        raise RuntimeError("protected frozen decoder14 changed")
    report["permanent_sha256"] = own_before
    report["protected_frozen14_sha256"] = baseline
    report["protected_inputs_preserved"] = True
    output = OUT / "result.json"; output.write_bytes(producer.canonical(report))
    print(json.dumps({"receipt": str(output.relative_to(ROOT)), "sha256": sha(output),
                      "synthetic": report["synthetic"], "original": report.get("original"),
                      "rejected_mutants": len(report.get("mutations", []))}, sort_keys=True))


if __name__ == "__main__":
    main()
