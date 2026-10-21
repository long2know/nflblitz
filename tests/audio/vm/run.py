#!/usr/bin/env python3
"""Actual Core ANI_CODE integration, immutable transport, original C and isolated caller mutations."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = HERE / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
PROJECT = HERE / "SourceSoundVm.Checks.csproj"
sys.path.insert(0, str(ROOT / "tests/parity"))
from prepare_inputs import fixture_hashes, MOVEMENT_ANIMATION_SHA, SOUND_SHA, SOUND_INCLUDE_SHA, SOUND_AUDIT_SHA
from fetch_inputs import blob_sha1, input_pins, sound_function_pins, SOUND_STATE_INPUTS
from contact_inputs import ANIMATION_SHA as CONTACT_ANIMATION_SHA, fixture_profile
from kernel_sources import production_paths, canonical_hashes, C2_ANIMATION_SHA

DATA_SHA = SOUND_SHA
INCLUDE_SHA = SOUND_INCLUDE_SHA
EXPECTED_SHORT = 142
EXPECTED_ASSERTIONS = 800735
EXPECTED_CASES = 15912
POST_HOST_SELECTION_BINDING = "defensive-presnap-switch/binding-required/v1"
POST_HOST_SELECTION_SOURCE_SHA256 = "4e41f3c0ecb3f002374090fbfddb92dcbaf51bc79ded50659a1fa1b2c2345ce3"
POST_HOST_SELECTION_ANCHOR = """        private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)
        {
            RequireSourceSoundVmBinding();"""
POST_HOST_SELECTION_DEFERRED = POST_HOST_SELECTION_ANCHOR.replace(
    "RequireSourceSoundVmBinding();",
    "RequireSourceSoundVmBinding(); if (!deliveringDeferred) { deferred.Add(Tuple.Create(actor,cue)); return; }")
MUTATION_FIELDS = {
    "double-consume": "no extra consumption and following opcode runs after sound",
    "wrong-stream": "no extra consumption and following opcode runs after sound",
    "wrong-process-order": "actual current RNG process/order/call offset must precede following VM opcode",
    "cursor-offset-reset": "actual current RNG process/order/call offset must precede following VM opcode",
    "raw-flags-as-role": "actual original raw flags, not ActorRole/default onscreen",
    "mode-as-role": "actual original MODE_LINE value, not ActorRole",
    "wrong-script-tuple": "same function/line/args in wrong actual script is unsupported",
    "unknown-is-null": "unknown producer pointer never becomes default actual null",
    "partial-offset-rollback": "partial failure advances existing Core offset before rethrow",
    "duplicate-delivery": "drain is once-only",
    "post-host-selection": "current source process call offset enters selector",
}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def source_files():
    return production_paths()


def require_summary(code, summary, native, animation_sha=MOVEMENT_ANIMATION_SHA):
    if (code != 0 or not isinstance(summary, dict) or summary.get("schema") != "source-sound-vm-checks/v1"
            or summary.get("status") != "passed"
            or summary.get("assertions") != (EXPECTED_ASSERTIONS if native else EXPECTED_SHORT)
            or summary.get("actual_C_cases") != (EXPECTED_CASES if native else 0)
            or summary.get("actual_implemented_VM_tuples") != 6
            or summary.get("actual_M1_library") != animation_sha
            or summary.get("actual_SAFN") != DATA_SHA
            or summary.get("DSP_playback_SPFX_or_target_PRNG_qualified") is not False):
        raise RuntimeError("Actual sound VM exact assertion/case/input inventory differs.")


def require_mutant(code, summary, field):
    if (code != 1 or not isinstance(summary, dict) or summary.get("schema") != "source-sound-vm-checks/v1"
            or summary.get("status") != "failed"
            or not isinstance(summary.get("assertions"), int) or summary["assertions"] <= 0
            or field not in summary.get("failed_assertion", "")):
        raise RuntimeError("Sound mutation must compile then fail its named offending-field assertion: " + field)


def mutation_specs():
    return [
        ("double-consume", "SourceSoundVm.cs", [
            ("Model.SourceRandomDraws += result.RandomCalls;",
             'Model.SourceRandomDraws += result.RandomCalls; for (int i=0;i<result.RandomCalls;i++) SourceDraw(997, "isolated-double-consume");')]),
        ("wrong-stream", "SourceSoundVm.cs", [
            ("sourceRandom, context);",
             "new SourceInjectedRandom(site => 0, sourceRandom.Provenance), context);")]),
        ("wrong-process-order", "SourceSoundVm.cs", [
            ("sourceRandom, context);",
             "sourceRandom, new SourceRandomContext(context.SourceTick, context.ActorId, context.Process, cue.Context.ProcessOrder, context.Profile, context.FirstCallIndex));")]),
        ("cursor-offset-reset", "SourceSoundVm.cs", [
            ("checked(context.FirstCallIndex + result.RandomCalls)", "context.FirstCallIndex")]),
        ("raw-flags-as-role", "SourceSoundVm.cs", [
            ("GameSnapshot.SourceActorFlags(actor), (int)actor.SourceMode,", "(int)actor.Role, (int)actor.SourceMode,")]),
        ("mode-as-role", "SourceSoundVm.cs", [
            ("GameSnapshot.SourceActorFlags(actor), (int)actor.SourceMode,", "GameSnapshot.SourceActorFlags(actor), (int)actor.Role,")]),
        ("wrong-script-tuple", "SourceSoundVm.cs", [
            ("candidate.ScriptSymbol == cue.Context.ExecutingSymbol\n                    && ", "")]),
        ("unknown-is-null", "SourceSoundVm.cs", [
            ("SourceAudioHitPartner hit = default(SourceAudioHitPartner);", "SourceAudioHitPartner hit = SourceAudioHitPartner.Absent;")]),
        ("partial-offset-rollback", "SourceSoundVm.cs", [
            ("AdvanceSourceSoundRandom(context, error.PartialResult);", "/* isolated rollback: partial cursor not accounted */")]),
        ("duplicate-delivery", "SourceSoundVm.cs", [
            ("var batch = new SourceSoundVmBatch(sourceSoundVmPending);\n            sourceSoundVmPending.Clear();",
             "var batch = new SourceSoundVmBatch(sourceSoundVmPending);")]),
        ("post-host-selection", "SourceSoundVm.cs", [
            ("private bool sourceSoundVmFailed;", "private bool sourceSoundVmFailed;\n        private bool deliveringDeferred;\n        private readonly List<Tuple<ActorState, SourceCallbackCue>> deferred = new List<Tuple<ActorState, SourceCallbackCue>>();"),
            ("var batch = new SourceSoundVmBatch(sourceSoundVmPending);",
             "deliveringDeferred = true; foreach (var item in deferred) ExecuteSourceSoundVm(item.Item1,item.Item2); deferred.Clear(); deliveringDeferred = false;\n            var batch = new SourceSoundVmBatch(sourceSoundVmPending);"),
            (POST_HOST_SELECTION_ANCHOR, POST_HOST_SELECTION_DEFERRED)]),
    ]


def mutated_source(current, name, edits):
    if name == "post-host-selection":
        if (hashlib.sha256(current.encode()).hexdigest() != POST_HOST_SELECTION_SOURCE_SHA256
                or len(edits) != 3
                or edits[-1] != (POST_HOST_SELECTION_ANCHOR, POST_HOST_SELECTION_DEFERRED)):
            raise RuntimeError("Post-host selection requires the current source and binding-required boundary.")
    value = current
    for old, new in edits:
        if value.count(old) != 1:
            raise RuntimeError("Actual mutation anchor changed: " + name)
        value = value.replace(old, new, 1)
    return value


def main():
    global OUT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-manifest", type=Path, required=True, help="Explicit read-only legacy or production272 fixture.")
    parser.add_argument("--input-profile", choices=("legacy", "contact272"), default="legacy",
                        help="Contact272 requires its new explicit schema and actual production library.")
    parser.add_argument("--functions", type=Path, required=True, help="Frozen verified directory with SAFN/manifest/original C include.")
    parser.add_argument("--source-root", type=Path, required=True,
                        help="Explicit pinned originals for sound functions and actual bank/pointer initialization provenance.")
    parser.add_argument("--mutations", action="store_true")
    parser.add_argument("--c2-inputs", type=Path, help="Explicit complete current281 inputs; no272 alias.")
    parser.add_argument("--output", type=Path, help="New ignored child output; preserve historical VM receipts.")
    parser.add_argument("--unity-reference", type=Path, help="Optional explicit actual Unity netstandard2.1 reference; never launches Unity.")
    args = parser.parse_args()
    if args.output is not None:
        selected = args.output.absolute()
        if not selected.is_relative_to(HERE / ".artifacts") or selected.exists():
            parser.error("--output requires a new tests/audio/vm/.artifacts child.")
        OUT = selected
    profile = fixture_profile(args.fixture_manifest, args.input_profile)
    animation_sha = C2_ANIMATION_SHA if args.c2_inputs is not None else (
        CONTACT_ANIMATION_SHA if profile == "contact272" else MOVEMENT_ANIMATION_SHA)
    OUT.mkdir(parents=True, exist_ok=True)
    work = OUT / ("work-" + uuid.uuid4().hex)
    work.mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    directory = args.functions.resolve()
    payload = directory / "sound-functions.safn"
    original = directory / "original-functions.inc"
    audit_path = directory / "manifest.json"
    if sha(payload) != DATA_SHA or sha(original) != INCLUDE_SHA or sha(audit_path) != SOUND_AUDIT_SHA:
        raise RuntimeError("Frozen actual sound-function inputs differ.")
    audit = json.loads(audit_path.read_text())
    files = source_files()
    before = {p.relative_to(ROOT).as_posix(): sha(p) for p in files}
    inputs = fixture_hashes(args.fixture_manifest.resolve(), payload)
    if args.c2_inputs is not None:
        inputs.update(canonical_hashes(args.c2_inputs))
    inputs.update({str(p): sha(p) for p in [original, audit_path]})
    pins = input_pins()
    state_names = sorted(set(sound_function_pins()) | set(SOUND_STATE_INPUTS) | {"nfl.c", "detect.c", "player.c"})
    state_inputs = {}
    for name in state_names:
        path = args.source_root.resolve() / name
        if blob_sha1(path.read_bytes()) != pins[name]:
            raise RuntimeError("Original sound VM state/input blob pin differs: " + name)
        state_inputs[str(path)] = sha(path)
    inputs.update(state_inputs)
    receipt = {"schema": "source-sound-vm-run/v1", "status": "failed", "commands": [],
               "original_input_sha256_before": inputs, "production_sha256_before": before,
               "state_producer_git_blobs": {name: pins[name] for name in state_names},
               "complete_ci_proof_requested": args.mutations,
               "input_profile": profile, "expected_animation_sha256": animation_sha,
               "native_Unity_browser_DSP_or_target_PRNG_qualified": False}

    def run(command, label, check=True, stdin=None):
        receipt["commands"].append([str(x) for x in command])
        scratch = work / "runtime" / label
        scratch.mkdir(parents=True, exist_ok=True)
        execute_env = dict(env, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
        result = subprocess.run([str(x) for x in command], cwd=ROOT, env=execute_env, text=True,
                                input=stdin, capture_output=True, timeout=240)
        text = result.stdout + result.stderr
        (OUT / (label + ".log")).write_text(text)
        if check and result.returncode:
            raise RuntimeError(label + "\n" + text[-6500:])
        return result, text

    def build(source_root, label):
        build_root = work / label
        build_root.mkdir(exist_ok=True)
        command = ["dotnet", "build", PROJECT, "--no-restore", "--verbosity", "quiet",
                   "-p:BlitzSourceRoot=" + str(source_root) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build_root / "bin") + "/"]
        result, text = run(command, label + "-build", False)
        if result.returncode and "NETSDK1004" in text:
            feed = work / "empty-feed"; feed.mkdir(exist_ok=True)
            run(["dotnet", "restore", PROJECT, "--source", feed, "-p:NuGetAudit=false",
                 "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/", "--verbosity", "quiet"],
                label + "-restore-local-SDK")
            result, text = run(command, label + "-build-restored", False)
        if result.returncode:
            raise RuntimeError("Compile failures are NOT mutation proof:\n" + text[-6500:])
        return build_root / "bin/Debug/net10.0/SourceSoundVm.Checks.dll"

    def execute(assembly, label, c_cases=None, c_results=None):
        command = ["dotnet", assembly, args.fixture_manifest.resolve(), payload, OUT / label]
        if c_cases is not None:
            command += [c_cases, c_results]
        if args.c2_inputs is not None:
            command += ["--c2-inputs", args.c2_inputs.absolute()]
        result, text = run(command, label, False)
        summaries = [json.loads(line) for line in result.stdout.splitlines()
                     if line.startswith('{"schema":"source-sound-vm-checks/')]
        return result.returncode, summaries[0] if len(summaries) == 1 else None, text

    try:
        assembly = build(ROOT, "baseline")
        code, summary, text = execute(assembly, "baseline-short")
        require_summary(code, summary, False, animation_sha)
        receipt["short"] = summary

        run(["gcc", "-std=c11", "-O0", "-fwrapv", "-fno-strict-aliasing", "-I", directory,
             ROOT / "tests/audio/functions/oracle.c", "-o", work / "oracle"], "compile-unchanged-original-functions")
        rows = []
        raw_patterns = [[n, n + 1, n + 2, 0, 1, 2] for n in range(12)]
        raw_patterns += [[2147483647, 2147483646, 2147483645, 7, 8, 9]]
        station, line_mode, flag = audit["drone_station"], audit["line_mode"], audit["onscreen_flag"]
        for index, binding in enumerate(audit["bindings"]):
            function = int(binding["function"] == "grnd_hit_snd")
            for flags, mode, partner, primary, stations, raw in itertools.product(
                    [0, flag, -1], [line_mode, line_mode + 1],
                    [(0, line_mode), (1, line_mode), (1, line_mode + 1)], range(3),
                    [(station, station), (station, 0), (0, station), (-2147483648, 2147483647)], raw_patterns):
                present, partner_mode = partner
                if function == 0 and flags & flag and mode == line_mode and not present:
                    continue
                actor = len(rows) % 14
                carrier, receiver = (actor if primary == 0 else -1), (actor if primary == 1 else -1)
                # player.h PF_ANITICK=0x40 is set by the real VM before ANI_CODE.
                values = [len(rows), function, flags | 0x40, mode, actor, stations[0], present, partner_mode,
                          stations[1], carrier, receiver, *binding["arguments"], len(raw), index, *raw]
                rows.append(" ".join(str(x) for x in values))
        cases = OUT / "actual-C-input.txt"
        cases.write_text("\n".join(rows) + "\n")
        _, native_text = run([work / "oracle"], "actual-C", stdin="\n".join(rows) + "\n")
        oracle = OUT / "actual-C-output.jsonl"; oracle.write_text(native_text)
        if len(native_text.splitlines()) != EXPECTED_CASES:
            raise RuntimeError("Unchanged original C inventory differs.")
        code, summary, text = execute(assembly, "baseline-original-C", cases, oracle)
        require_summary(code, summary, True, animation_sha)
        receipt["actual_VM_original_C"] = summary
        receipt["original_C"] = {"cases": len(rows), "fixture_sha256": sha(ROOT / "tests/audio/functions/oracle.c"),
                                 "unchanged_include_sha256": sha(original), "inputs_sha256": sha(cases),
                                 "output_sha256": sha(oracle)}

        if args.mutations:
            specs = mutation_specs()
            snapshot = {p.relative_to(ROOT): p.read_bytes() for p in files}
            if {str(p): hashlib.sha256(data).hexdigest() for p, data in snapshot.items()} != before:
                raise RuntimeError("Actual sound source inventory changed while capturing the isolated copies.")
            if {name for name, _, _ in specs} != set(MUTATION_FIELDS):
                raise RuntimeError("Exact eleven named sound mutation contracts differ.")
            def mutation(case):
                name, filename, edits = case
                source_root = work / name / "source"
                for relative, data in snapshot.items():
                    p = source_root / relative; p.parent.mkdir(parents=True, exist_ok=True); p.write_bytes(data)
                target = source_root / "Assets/Scripts/Gameplay" / filename
                target.write_text(mutated_source(target.read_bytes().decode("utf-8"), name, edits))
                mutant = build(source_root, name)
                code, summary, text = execute(mutant, name)
                require_mutant(code, summary, MUTATION_FIELDS[name])
                relative = "Assets/Scripts/Gameplay/" + filename
                row = {"name": name, "compiled": True, "source": relative,
                       "source_sha256": before[relative], "mutated_source_sha256": sha(target),
                       "required_assertion": MUTATION_FIELDS[name],
                       "offending_assertion": summary["failed_assertion"]}
                if name == "post-host-selection":
                    row.update(binding=POST_HOST_SELECTION_BINDING,
                               source_identity=POST_HOST_SELECTION_SOURCE_SHA256,
                               original_anchor=POST_HOST_SELECTION_ANCHOR,
                               mutated_anchor=POST_HOST_SELECTION_DEFERRED)
                if before != {p.relative_to(ROOT).as_posix(): sha(p) for p in source_files()}:
                    raise RuntimeError("Live production changed during isolated sound mutation proof.")
                if inputs != {p: sha(p) for p in inputs}:
                    raise RuntimeError("Actual sound/fixture inputs changed during isolated mutation proof.")
                return row
            receipt["mutations"] = run_mutations(specs, mutation)

        code, summary, text = execute(assembly, "final-baseline-original-C", cases, oracle)
        require_summary(code, summary, True, animation_sha)
        receipt["final_baseline_original_C"] = summary

        if args.unity_reference:
            reference = args.unity_reference.resolve()
            if tuple(p.lower() for p in reference.parts[-4:]) != ("netstandard", "ref", "2.1.0", "netstandard.dll"):
                raise RuntimeError("Supply explicit actual installed Unity reference, not SDK/Unity shim.")
            sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1]
            version, directory_text = sdk.split(" ", 1)
            compiler = Path(directory_text.strip("[]")) / version / "Roslyn/bincore/csc.dll"
            run(["dotnet", compiler, "-nologo", "-noconfig", "-nostdlib+", "-langversion:7.3",
                 "-warnaserror+", "-target:library", "-r:" + str(reference),
                 "-out:" + str(work / "SoundVm.netstandard21.dll"), *files], "actual-Unity-reference")
            receipt["actual_Unity_reference"] = {"path": str(reference), "sha256": sha(reference),
                                                "source_inventory": before, "Editor_launched": False}
        receipt["status"] = "passed"
    finally:
        receipt["production_sha256_after"] = {p.relative_to(ROOT).as_posix(): sha(p) for p in source_files()}
        receipt["live_source_unchanged"] = before == receipt["production_sha256_after"]
        receipt["original_input_sha256_after"] = {p: sha(p) for p in inputs}
        receipt["original_inputs_unchanged"] = inputs == receipt["original_input_sha256_after"]
        shutil.rmtree(work)
        receipt["own_builds_removed"] = True
        (OUT / "result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    if not receipt["live_source_unchanged"] or not receipt["original_inputs_unchanged"]:
        raise RuntimeError("Production/read-only source fixtures changed during validation.")
    print(json.dumps({"status": receipt["status"], "actual_VM_checks": receipt.get("actual_VM_original_C"),
                      "mutants": len(receipt.get("mutations", []))}, separators=(",", ":")))


if __name__ == "__main__":
    main()
