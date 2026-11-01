#!/usr/bin/env python3
"""New-release-only component validation. Authoring this file does not authorize execution."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

WT = Path(__file__).resolve().parents[2]
META = WT / ".squad/.scratch/source-following-active-p1/metadata/source-instruction-runtime-integration-01"
RUN = WT / "tests/source-instructional-presentation/validation-output"
HOST = Path("/home/long2know/.dotnet/dotnet")
CSC = Path("/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll")
MODEL = "Assets/Scripts/Presentation/SourceInstructionPresentation.cs"
CONSUMER = "Assets/Scripts/Runtime/SourceInstructionConsumer.cs"
TEXT = "Assets/Scripts/Presentation/SourceInstructionTextProgram.cs"
RUNTIME = "Assets/Scripts/Runtime/BlitzRuntime.cs"
ADAPTER = "Assets/Scripts/Runtime/SourceInstructionRuntime.cs"
HUD = "Assets/Scripts/Presentation/SourceHudRenderer.cs"
DRAW = "Assets/Scripts/Presentation/SourceInstructionHudRenderer.cs"
RESOURCES = "Assets/Scripts/Presentation/SourceInstructionHudResources.cs"
SOURCES = [
    "Assets/Scripts/Contracts/SourcePortableProcessContracts.cs",
    "Assets/Scripts/Contracts/SourceInstructionContracts.cs",
    "Assets/Scripts/Contracts/SourceInstructionPresentationContracts.cs",
    "Assets/Scripts/Gameplay/SourceAnyButtonWait.cs",
    "Assets/Scripts/Gameplay/SourcePortableProcessHost.cs",
    "Assets/Scripts/Gameplay/SourceInstructionProcess.cs",
    TEXT, MODEL, CONSUMER, "tests/source-instructional-presentation/Program.cs",
]
CASES = [
    "LegacyNoInvocationIsInert", "RegisteredProducerOwnsReturnedObjects", "All28SlotsRetainSourceStyleAndArguments",
    "TextFlasherMutatesSharedReturnedStrings", "MaskForwardingUsesNativePredicateAndOrder",
    "ObjectFlashConsumesOwnedHandleAndExactArguments", "PageCleanupRetainsSharedSlotsWithoutAliasing",
    "FinalCleanupPrecedesReturnedDelayAndReentry", "ResetInvalidatesHandlesAndDoesNotRestartProgram",
    "ErrorsFailClosedAndOwnedResourcesAreCleaned", "FormatCatalogRejectsIncompleteOrIncorrectNativeBindings",
    "AdmissionAndLifecycleErrorsAreExplicit", "PresentationTickEpochMapsActualSourceTicks",
    "NativeFormattingDoesNotInferAlignment", "MaskDeletionMatchesSourceStringIds",
    "SoundOrderingKeepsNativeIdentitiesAndLiveGate",
]
MUTANTS = [
    ("shared-message-return", MODEL, "messages[index] = RequireText(text);", "messages[index] = null;", CASES[1]),
    ("early-text-flash", MODEL, "host.Sleep(7);", "host.Sleep(6);", CASES[3]),
    ("missing-character-recolor", MODEL, "text.CharacterColorOverride = value;", "text.CharacterColorOverride = null;", CASES[3]),
    ("resume-instead-of-suspend", MODEL,
     'if (record.Operation == "SuspendMask") host.SuspendMultiple(record.Id, record.Value);',
     'if (record.Operation == "SuspendMask") host.ResumeMultiple(record.Id, record.Value);', CASES[4]),
    ("no-generated-plate", MODEL, "state.Graphics = Graphics(resources.GeneratePlate(", "Graphics(resources.GeneratePlate(", CASES[1]),
    ("retain-deleted-strings", MODEL, "text.IsDeleted = true;", "text.IsDeleted = false;", CASES[6]),
    ("font-not-forwarded", TEXT, "new SourceInstructionFormatCall(slot, request, font, color, position, raster)",
     "new SourceInstructionFormatCall(slot, request, SourceInstructionFont.Bast10, color, position, raster)", CASES[2]),
    ("sound-context-reuses-ordinal", MODEL, "(preparedTick, instructionSerial, soundOrdinal++)",
     "(preparedTick, instructionSerial, soundOrdinal)", CASES[15]),
    ("plate-not-deleted", MODEL,
     "state.IsDeleted = true;\n                foreach (SourceInstructionFlasherState flasher in flashers)",
     "state.IsDeleted = false;\n                foreach (SourceInstructionFlasherState flasher in flashers)", CASES[7]),
    ("returned-string-color-guessed", MODEL, "font.Value, color.Value, layout.StateColor, textPosition.Value",
     "font.Value, color.Value, selectedColor, textPosition.Value", CASES[3]),
]
STATIC_MUTANTS = [
    ("disconnect-runtime-registration", RUNTIME, "sourceInstructions = new SourceInstructionRuntime",
     "var disconnectedInstructions = new SourceInstructionRuntime", "runtime_registration"),
    ("disconnect-real-game-admission", ADAPTER, "return simulation.InvokeSourceInstructions(invocation);",
     "return 1;", "game_explicit_admission"),
    ("disconnect-source-tick", RUNTIME, "sourceInstructions.AfterSourceStep(State);", "/* disconnected */", "source_tick_consumer"),
    ("disconnect-draw", HUD, "runtime.DrawSourceInstructions(viewport);", "/* disconnected */", "retained_renderer"),
    ("disconnect-restart", RUNTIME, "sourceInstructions.OnSourceProgramReset();", "/* disconnected */", "lifecycle"),
    ("disconnect-native-sound", ADAPTER, "transport.Drain(batch);", "/* disconnected */", "sound_transport"),
    ("invent-automatic-admission", RUNTIME, "bridge.Restart();",
     "bridge.Restart(); InvokeSourceInstructions(1, null, null);", "no_automatic_admission"),
    ("disconnect-native-format-evaluation", RESOURCES, "text.Evaluate(request, font, color, raster, position)",
     "null", "native_resource_consumer"),
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def body(source, signature):
    token = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/', re.DOTALL)
    masked = token.sub(lambda match: " " * len(match.group()), source)
    start = masked.index(signature)
    brace = masked.index("{", start)
    depth = 1
    for index in range(brace + 1, len(masked)):
        depth += (masked[index] == "{") - (masked[index] == "}")
        if depth == 0:
            return source[brace + 1:index]
    raise ValueError("Unterminated method " + signature)


def wiring(overrides=None):
    overrides = overrides or {}

    def read(path):
        return overrides.get(path, (WT / path).read_text(encoding="utf-8"))

    runtime, adapter, hud, draw = (read(p) for p in [RUNTIME, ADAPTER, HUD, DRAW])
    bridge = read("Assets/Scripts/Runtime/FixedStepBridge.cs")
    game = read("Assets/Scripts/Gameplay/GameSimulation.cs")
    resources = read(RESOURCES)
    return {
        "runtime_registration": "sourceInstructions = new SourceInstructionRuntime" in body(runtime, "public void BindSourceInstructionPresentation("),
        "game_explicit_admission": "return simulation.InvokeSourceInstructions(invocation);" in body(adapter, "public long Invoke(SourceInstructionInvocation invocation)"),
        "source_tick_consumer": "sourceInstructions.AfterSourceStep(State);" in body(runtime, "private void OnAfterSimulationStep()")
            and "bridge.AfterSimulationStep += OnAfterSimulationStep;" in body(runtime, "private void Awake()")
            and "Action drained = AfterSimulationStep;" in bridge and game.count("Model.SourceTick++;") == 1
            and "if (Model.SourceClockRemainder >= SliceRules.TicksPerSecond)" in game,
        "retained_renderer": "runtime.DrawSourceInstructions(viewport);" in body(hud, "public void OnGUI()")
            and "SourceHudRenderer.DrawPrimitives(resources.Asset, primitives, viewport);" in body(draw, "public void Draw(SourceHudViewport viewport)")
            and "GL.Vertex3(" in body(hud, "internal static void DrawPrimitives("),
        "lifecycle": "sourceInstructions.OnSourceProgramReset();" in body(runtime, "private void ResetSourceSoundTransport()")
            and "sourceInstructions.Dispose();" in body(runtime, "private void OnDestroy()")
            and "sourceInstructions.Abort(error);" in body(runtime, "internal void FailSourceMode(Exception error)"),
        "sound_transport": "transport.Drain(batch);" in body(adapter, "public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context)")
            and "transport.FlushNativePrefix();" in adapter and "SourceSoundVmStatus.Resolved" in adapter,
        "no_automatic_admission": all("InvokeSourceInstructions(" not in body(runtime, signature)
            for signature in ["private void Awake()", "private void Update()", "private void Advance(", "internal void RestartMatch()"]),
        "native_resource_consumer": "text.Evaluate(request, font, color, raster, position)" in body(resources, "public SourceInstructionTextLayout FormatText(")
            and "SourceHudText.Center(" in resources and "spriteLayout.Layout(" in resources
            and "Asset.Data.Image(imageSymbol)" in resources and "Asset.Texture(id);" in resources,
    }


def replacement(path, old, new):
    text = (WT / path).read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise ValueError("Production mutation anchor is not unique: " + path + " / " + old)
    return text.replace(old, new, 1)


def execute(argv, folder, name, timeout, commands):
    record = {"argv": [str(a) for a in argv], "cwd": str(WT), "timeout_seconds": timeout}
    overrides = {"DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                 "DOTNET_NOLOGO": "1", "DOTNET_MULTILEVEL_LOOKUP": "0", "DOTNET_EnableDiagnostics": "0",
                 "COMPlus_EnableDiagnostics": "0", "DOTNET_CLI_HOME": str(RUN / "dotnet-home"), "TMPDIR": str(RUN / "scratch")}
    record["environment_overrides"] = overrides
    save(folder / (name + ".command.json"), record)
    env = dict(os.environ)
    env.update(overrides)
    try:
        result = subprocess.run(record["argv"], cwd=WT, env=env, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, timeout=timeout, check=False)
        stdout, stderr = result.stdout, result.stderr
        record.update({"exit_code": result.returncode, "timed_out": False})
    except subprocess.TimeoutExpired as error:
        stdout, stderr = error.stdout or b"", error.stderr or b""
        record.update({"exit_code": None, "timed_out": True})
    (folder / (name + ".stdout.log")).write_bytes(stdout)
    (folder / (name + ".stderr.log")).write_bytes(stderr)
    record.update({"stdout_bytes": len(stdout), "stderr_bytes": len(stderr), "logs_complete": not record["timed_out"],
                   "stdout_sha256": hashlib.sha256(stdout).hexdigest(), "stderr_sha256": hashlib.sha256(stderr).hexdigest()})
    save(folder / (name + ".command.json"), record)
    commands.append(record)
    if record["timed_out"] or len(stdout) > 262144 or len(stderr) > 262144:
        raise RuntimeError("Command exceeded admitted time/output bound; not a qualified result.")
    return record["exit_code"], stdout.decode("utf-8", errors="strict")


def variant(name, reference, commands, mutation=None):
    folder = RUN / name
    folder.mkdir()
    source_paths = [WT / path for path in SOURCES]
    if mutation is not None:
        _, source, old, new, _ = mutation
        changed = folder / Path(source).name
        changed.write_text(replacement(source, old, new), encoding="utf-8")
        source_paths[SOURCES.index(source)] = changed
    dll = folder / "instruction-presentation-regressions.dll"
    compile_argv = [HOST, CSC, "/noconfig", "/nostdlib+", "/langversion:7.3", "/target:exe",
                    "/deterministic+", "/warnaserror+", "/reference:" + reference, "/out:" + str(dll)] + source_paths
    compiler, _ = execute(compile_argv, folder, "compile", 60, commands)
    if compiler != 0:
        return {"name": name, "compiler_exit": compiler, "qualified": False, "reason": "compiler failure is not a killed mutant"}
    save(dll.with_suffix(".runtimeconfig.json"), {
        "runtimeOptions": {"tfm": "net10.0", "rollForward": "Disable",
                           "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}
    })
    run_exit, output = execute([HOST, dll], folder, "run", 30, commands)
    passed = re.findall(r"^PASS ([A-Za-z0-9_]+)$", output, re.MULTILINE)
    failed = re.findall(r"^FAIL ([A-Za-z0-9_]+):", output, re.MULTILINE)
    named = sorted(passed + failed) == sorted(CASES)
    qualified = named and ((run_exit == 0 and not failed) if mutation is None else (run_exit == 1 and mutation[4] in failed))
    return {"name": name, "compiler_exit": compiler, "run_exit": run_exit, "passed": passed, "failed": failed,
            "expected_failure": None if mutation is None else mutation[4], "qualified": qualified, "all_named_cases_observed": named}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--parent-release", required=True)
    args = parser.parse_args()
    release_path = Path(args.parent_release).resolve()
    if release_path != META / "parent-validation-release.json":
        raise ValueError("Only this new owned parent-release path is admitted.")
    release = json.loads(release_path.read_text(encoding="utf-8"))
    if release["schema"] != "source-instruction-presentation-parent-release-1" or not release["admission_id"]:
        raise ValueError("A fresh explicit parent release is required.")
    if release["manifest_sha256"] != sha(META / "change-manifest.json") or release["proposal_sha256"] != sha(META / "validation-proposal.json"):
        raise ValueError("Parent release does not admit the current source/proposal pins.")
    manifest = json.loads((META / "change-manifest.json").read_text(encoding="utf-8"))
    if manifest["worktree"] != str(WT):
        raise ValueError("Wrong shared feature worktree.")
    pins = manifest["validation_inputs"]
    for item in pins:
        if sha(WT / item["path"]) != item["sha256"] or (WT / item["path"]).stat().st_size != item["bytes"]:
            raise ValueError("Changed admitted input " + item["path"])
    for key, expected in [("host", HOST), ("compiler", CSC)]:
        item = release[key]
        if item["path"] != str(expected) or sha(expected) != item["sha256"] or expected.stat().st_size != item["bytes"]:
            raise ValueError("Missing genuine parent-admitted tool pin " + key)
    reference = release["reference"]
    reference_path = Path(reference["path"])
    if reference["identity"] != "NETStandard.Library.Ref/2.1.0/netstandard2.1/netstandard.dll" \
            or sha(reference_path) != reference["sha256"] or reference_path.stat().st_size != reference["bytes"]:
        raise ValueError("Missing genuine parent-admitted netstandard2.1 reference.")
    if release["framework"] != {"name": "Microsoft.NETCore.App", "version": "10.0.0", "rollForward": "Disable"}:
        raise ValueError("No runtime discovery, roll-forward or install fallback is allowed.")
    if RUN.exists():
        raise ValueError("The named output footprint has already been consumed; do not rerun here.")
    RUN.mkdir()
    (RUN / "scratch").mkdir()
    (RUN / "dotnet-home").mkdir()
    commands, results = [], []
    save(RUN / "admission.json", {"release_sha256": sha(release_path), "admission_id": release["admission_id"]})
    try:
        guards = wiring()
        static_mutants = []
        for name, source, old, new, guard in STATIC_MUTANTS:
            checks = wiring({source: replacement(source, old, new)})
            static_mutants.append({"name": name, "production_source": source, "production_sha256": sha(WT / source),
                                   "expected_guard": guard, "checks": checks, "killed": not checks[guard]})
        static = {"classification": "STATIC WIRING ONLY; not Game/Unity behavior", "guards": guards, "mutants": static_mutants}
        save(RUN / "static-wiring.json", static)
        if not all(guards.values()) or not all(m["killed"] for m in static_mutants):
            raise RuntimeError("Static wiring or its production mutation did not reject the expected disconnection.")
        baseline = variant("baseline", str(reference_path), commands)
        results.append(baseline)
        if not baseline["qualified"]:
            raise RuntimeError("Baseline did not pass all named component cases.")
        for mutation in MUTANTS:
            result = variant(mutation[0], str(reference_path), commands, mutation)
            results.append(result)
        if not all(result["qualified"] for result in results):
            raise RuntimeError("A production-source mutation did not fail its expected named behavioral assertion.")
        stable = all(sha(WT / item["path"]) == item["sha256"] for item in pins)
        if not stable:
            raise RuntimeError("Admitted source input changed during validation.")
        tools_stable = sha(HOST) == release["host"]["sha256"] and sha(CSC) == release["compiler"]["sha256"] \
            and sha(reference_path) == reference["sha256"]
        if not tools_stable:
            raise RuntimeError("Admitted compiler/host/reference input changed during validation.")
        result = {"status": "CONDITIONAL_COMPONENT_AND_STATIC_WIRING_PASS", "whole100": "OPEN",
                  "variants": results, "input_pins_unchanged": stable, "tool_reference_pins_unchanged": tools_stable, "commands": commands,
                  "not_qualified": ["Full Game execution", "Unity API compilation", "Unity/browser pixels", "native SDK ABI",
                                    "source-resource authenticity/availability", "audio DSP", "automatic initial-QB admission"]}
        save(RUN / "result.json", result)
        print("RESULT CONDITIONAL_COMPONENT_AND_STATIC_WIRING_PASS; Whole100 OPEN; " + str(len(commands)) + " command exits.")
        return 0
    except Exception as error:
        save(RUN / "result.json", {"status": "NOT_QUALIFIED", "whole100": "OPEN", "error": str(error),
                                   "variants": results, "commands": commands})
        print("RESULT NOT_QUALIFIED: " + str(error))
        return 1
    finally:
        save(RUN / "commands.json", commands)


if __name__ == "__main__":
    sys.exit(main())
