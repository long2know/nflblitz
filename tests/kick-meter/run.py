#!/usr/bin/env python3
"""Real original-C, cabinet/meter/physics/HUD and isolated compiled causal controls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from oracle import build

ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import production_paths, canonical_hashes
from prepare_inputs import fixture_hashes

GAME = "Assets/Scripts/Gameplay/"
PROCESS = GAME + "SourceKickMeterProcess.cs"
CALLER = GAME + "GameSimulation.Source.cs"
HUD = "Assets/Scripts/Presentation/SourceHudComposer.cs"
DATA = "Assets/Scripts/AssetImport/SourceInterfaceData.cs"
CONTRACT = "Assets/Scripts/Contracts/SourceKickMeterContracts.cs"
MUTATIONS = (
    ("authored-bar-wrong-axis", "meters", [(DATA, "ContentSha256, bar.Width, marker.Width, punt.Height",
        "ContentSha256, bar.Height, marker.Width, punt.Height")], "original-C meter field[2]"),
    ("authored-marker-wrong-axis", "meters", [(DATA, "ContentSha256, bar.Width, marker.Width, punt.Height",
        "ContentSha256, bar.Width, marker.Height, punt.Height")], "original-C meter field[5]"),
    ("authored-punt-wrong-axis", "meters", [(DATA, "ContentSha256, bar.Width, marker.Width, punt.Height",
        "ContentSha256, bar.Width, marker.Width, punt.Width")], "original-C meter field[2]"),
    ("punt-power-inverted", "meters", [(PROCESS, "65 * (limit - Math.Abs(position)) / limit + 25",
        "65 * Math.Abs(position) / limit + 25")], "original-C punt power"),
    ("punt-clamped-every-wake", "meters", [(PROCESS,
        "if (position > limit || position < -limit) { velocity = -velocity; bounces++; }",
        "if (position > limit || position < -limit) { position = Math.Max(-limit, Math.Min(limit, position)); velocity = -velocity; bounces++; }")],
        "original-C meter field[0]"),
    ("first-poll-one-wake-late", "meters", [(PROCESS, "tick = created; nextWake = created + 1;",
        "tick = created; nextWake = created + 2;")], "original-C meter field[0]"),
    ("result-hold29", "meters", [(PROCESS, "hideTick = sourceTick + 30;", "hideTick = sourceTick + 29;")],
        "original-C continuation sleep/wake tick"),
    ("meter-player-race", "callers", [
        (CALLER, "            StepSourceKickMeter();", "            // compiled causal control: wrong post-player continuation"),
        (CALLER, "            UpdateSourceMovementPlayers(command);",
         "            UpdateSourceMovementPlayers(command);\n            StepSourceKickMeter();")],
        "actual-raw-0-Home-FG: original-C meter field[0]"),
    ("punt-release-frame5", "callers", [(GAME + "SourceAnimationPrograms.cs",
        'Seq("M_KICK"), To(4), Call(SourceAnimationSignal.PuntBall)',
        'Seq("M_KICK"), To(5), Call(SourceAnimationSignal.PuntBall)')],
        "punt physics is released at original ANI_CODE frame4"),
    ("FG-invented-delay", "callers", [(CALLER, "if (kickDistance < 8f && Model.CarrierId >= 0)",
        "if (kickDistance < 8f && Model.CarrierId >= 0 && Model.SourceTick > Model.SourceSnapTick + 38)")],
        "FG launch occurs on FIRST kickable-holder source wake"),
    ("wrong-cached-punt-ankle", "callers", [(CALLER,
        ": SourceKickCachedJoint(actor, actor.Flipped ? 14 : 11);",
        ": SourceKickCachedJoint(actor, actor.Flipped ? 11 : 14);")],
        "actual-raw-launch-0-Home-punt: original-C launch field["),
    ("punt-launch-units", "physics", [(GAME + "SourceBallDriver.cs",
        "float vx = (20 + maxX * model.SourcePuntStrength / 100) / 10f;",
        "float vx = (20 + maxX * model.SourcePuntStrength / 100) / 9f;")],
        "punt-math: original-C launch field[3]"),
    ("HUD-ball-wrong-direction", "callers", [(HUD,
        'MeterSprite("kick/fg/ball", "k_ball", x + meter.SpriteOffsetPixels, y, viewport);',
        'MeterSprite("kick/fg/ball", "k_ball", x - meter.SpriteOffsetPixels, y, viewport);')],
        "actual HUD ball coordinate consumes original INTEGER meter offset"),
    ("disabled-native-guard-bypassed", "disabled", [(CALLER,
        "&& sourceKickMeterMetrics == null)\n                throw new NotSupportedException",
        "&& sourceKickMeterMetrics != null)\n                throw new NotSupportedException")],
        "DISABLED817: default gate rejects the actual raw-button caller"),
    ("URL-silent-enable", "options", [(CONTRACT,
        "if (string.IsNullOrEmpty(url)) return SourceKickMeterMode.Disabled;",
        "if (string.IsNullOrEmpty(url)) return SourceKickMeterMode.PortableAuthored;")],
        "no URL is disabled"),
    ("historical-snapshot-shape-leak", "lifecycle", [(GAME + "GameSnapshot.cs",
        "private readonly SourceKickMeterMetrics kickMeterMetrics;",
        "private readonly SourceKickMeterMetrics kickMeterMetrics;\n        public SourceKickMeterMetrics LeakedPortableMetrics { get { return kickMeterMetrics; } }")],
        "enabled non-kicking profile leaves historical public snapshot byte representation unchanged"),
    ("side-ownership-shortcut", "callers", [(PROCESS,
        "return new SourceKickMeterView(kind, status, owner, sourceTeam, attribute, fake, created,",
        "return new SourceKickMeterView(kind, status, TeamId.Home, sourceTeam, attribute, fake, created,")],
        "source process remains owned by the actual offensive human side and play"),
)
EXTRA_PURE = (
    "Assets/Scripts/Runtime/SourceCabinetInput.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs",
    "Assets/Scripts/Presentation/SourceHudModel.cs",
    "Assets/Scripts/Presentation/SourceHudViewport.cs",
    HUD,
)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-root", "interface-root", "fixture-manifest", "sound-functions", "old-c2-inputs",
                 "c2-m1-inputs", "hud", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--without-controls", action="store_true", help="Explicit reference/caller baseline only, never mutation qualification.")
    args = parser.parse_args()
    output = args.output.absolute()
    if (output.exists() or ".." in args.output.parts or not output.is_relative_to(ROOT / ".squad/.scratch")
            or any(p.is_symlink() for p in (output, *output.parents))):
        parser.error("Use a NEW regular ignored output inside this worktree's .squad/.scratch.")
    output.mkdir(parents=True)
    work = output / "work"
    (work / "empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1",
               PYTHONDONTWRITEBYTECODE="1")
    files = set(production_paths()) | {ROOT / name for name in EXTRA_PURE}
    snapshot = {str(path.relative_to(ROOT)): path.read_bytes() for path in files}
    before = {name: hashlib.sha256(raw).hexdigest() for name, raw in snapshot.items()}
    harness_paths = list(SUITE.glob("*.cs")) + list(SUITE.glob("*.py")) + [SUITE / "driver.c", SUITE / "KickMeterChecks.csproj",
        ROOT / "tests/parity/FullKernelInputs.cs", ROOT / "tests/parity/FullKernelSources.props",
        ROOT / "tests/audio/vm/SourceSoundVmSources.props"]
    harness = {str(p.relative_to(ROOT)): sha(p) for p in harness_paths}
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.old_c2_inputs))
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    inputs[str(args.hud.absolute())] = sha(args.hud)
    result = dict(schema="portable-authored-kick-run/v1", status="failed",
                  production_before=before, harness_before=harness, inputs_before=inputs,
                  commands=[], command_results=[], controls=[],
                  original_native_metrics_known=False, native_or_browser_proof=False,
                  feature_is_independent_of_M1_Portable_consumer_corrections=True)

    def command(label, argv):
        result["commands"].append(argv)
        p = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
        (output / (label + ".log")).write_text(p.stdout + p.stderr)
        result["command_results"].append(dict(label=label, exit_code=p.returncode))
        return p

    def compile_source(label, source):
        root = output / "build" / label
        props = ["-p:BlitzSourceRoot=" + str(source) + "/", "-p:BaseIntermediateOutputPath=" + str(root / "obj") + "/",
                 "-p:BaseOutputPath=" + str(root / "bin") + "/"]
        argv = ["dotnet", "build", str(SUITE / "KickMeterChecks.csproj"), "--no-restore",
                "--configuration", "Release", "--verbosity", "quiet", *props]
        p = command(label + "-compile", argv)
        if p.returncode and "NETSDK1004" in p.stdout + p.stderr:
            restore = command(label + "-missing-offline-assets", ["dotnet", "restore", str(SUITE / "KickMeterChecks.csproj"),
                "--source", str(work / "empty-feed"), "--verbosity", "quiet", "-p:NuGetAudit=false", *props])
            if restore.returncode:
                raise RuntimeError("Offline missing SDK assets: " + label)
            p = command(label + "-compile", argv)
        if p.returncode or "0 Warning(s)" not in p.stdout or "0 Error(s)" not in p.stdout:
            raise RuntimeError("Every feature/control must compile with zero diagnostics: " + label + "\n" + p.stdout + p.stderr)
        return root / "bin/Release/net10.0/KickMeterChecks.dll"

    def execute(label, binary, native, section="all"):
        summary_path = output / (label + ".json")
        argv = ["dotnet", str(binary), str(args.fixture_manifest.absolute()), str(args.sound_functions.absolute()),
                str(args.old_c2_inputs.absolute()), str(args.c2_m1_inputs.absolute()), str(args.hud.absolute()),
                str(summary_path), section, str(native)]
        p = command(label, argv)
        summary = json.loads(summary_path.read_text())
        return p.returncode, summary

    def require_baseline(code, summary):
        if (code or summary.get("status") != "passed" or summary.get("disabledAssertions") != 817
                or summary.get("meterFlows") != 648 or summary.get("physicsCases") != 656
                or summary.get("lifecycleCases") != 39 or summary.get("additionalCases") != 15
                or len(summary.get("observations", [])) != 12 or summary.get("referenceCalls", 0) < 20000
                or not all(row.get("originalC_shadow_and_launch_compared") for row in summary["observations"])):
            raise RuntimeError("Exact feature/retained case inventory required: " + json.dumps(summary)[-4000:])

    try:
        native, provenance = build(args.source_root, args.interface_root, output / "original-C")
        result["original_C_provenance"] = provenance
        positive = compile_source("baseline", ROOT)
        code, baseline = execute("baseline", positive, native)
        require_baseline(code, baseline)
        result["baseline"] = baseline
        print("PASS baseline assertions=" + str(baseline["assertions"]) + " +retained817", flush=True)
        for identity, section, edits, expected in (() if args.without_controls else MUTATIONS):
            copy = output / "copies" / identity
            try:
                for name, raw in snapshot.items():
                    target = copy / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(raw)
                for name, old, new in edits:
                    target = copy / name
                    text = target.read_text()
                    if text.count(old) != 1:
                        raise RuntimeError("Causal control anchor must be unique: " + identity + ":" + name)
                    target.write_text(text.replace(old, new))
                binary = compile_source(identity, copy)
                code, summary = execute(identity, binary, native, section)
                if (code != 1 or summary.get("status") != "failed"
                        or expected not in summary.get("error", "")
                        or "KICK_METER_ASSERTION:" not in summary.get("error", "") and "DISABLED817:" not in summary.get("error", "")
                        or "System.InvalidOperationException" not in summary.get("error", "")):
                    raise RuntimeError("Control must fail its named output, not compilation/input/exception: "
                                       + identity + "\n" + json.dumps(summary)[-3000:])
                result["controls"].append(dict(identity=identity, section=section, compiled_zero_diagnostics=True,
                    named_assertion=expected, assertions_before_failure=summary["assertions"],
                    original_C_calls_before_failure=summary.get("referenceCalls", 0)))
                print("PASS compiled control " + identity, flush=True)
            finally:
                if copy.exists():
                    shutil.rmtree(copy)
        code, final = execute("final-baseline", positive, native)
        require_baseline(code, final)
        if baseline != final:
            raise RuntimeError("Exact baseline/final feature observations and counts must be identical.")
        result["final_baseline"] = final
        result["status"] = "passed-reference-only-no-controls" if args.without_controls else "passed-portable-authored-kick-feature"
    except (RuntimeError, OSError, ValueError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr, flush=True)
    finally:
        result["production_after"] = {name: sha(ROOT / name) for name in before}
        result["harness_after"] = {name: sha(ROOT / name) for name in harness}
        result["inputs_after"] = {name: sha(Path(name)) for name in inputs}
        if (result["production_after"] != before or result["harness_after"] != harness
                or result["inputs_after"] != inputs):
            result["status"] = "inconclusive-inputs-changed"
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"].startswith("passed-") else 1


if __name__ == "__main__":
    raise SystemExit(main())
