#!/usr/bin/env python3
"""Focused actual Core/input regressions; no asset copies, Unity or historical campaigns."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
BASE = "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6"
SUCCESSOR = "885c9e04cee21a2bb15d40b243744d3b1bfa6f4846ddf2ea9837c7fd3bec5548"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--mutations", action="store_true", help="Compile two isolated real-source controls; never mutate production.")
    args = parser.parse_args()
    original = (args.data / "manifest.json").read_bytes()
    if hashlib.sha256(original).hexdigest() != BASE:
        raise ValueError("The exact approved563 descriptor is required.")
    manifest = json.loads(original)
    successor = dict(manifest, profile="original-teams-contact282-c2-m1-default-audibles",
                     featureContract="source-default-audibles/v1",
                     factory="SourceC2Composition.CreateDefaultAudiblesSimulation")
    descriptor = (json.dumps(successor, indent=2, sort_keys=True) + "\n").encode()
    if hashlib.sha256(descriptor).hexdigest() != SUCCESSOR:
        raise ValueError("The separate genuine successor descriptor must retain every original pin/entrypoint.")
    artifacts = SUITE / ".artifacts/core-input-intro"
    artifacts.mkdir(parents=True, exist_ok=True)
    path = artifacts / "successor-descriptor.json"
    path.write_bytes(descriptor)
    project = "tests/source-instructional-admission/SourceInputIntro.Checks.csproj"
    dotnet = ["bash", "tools/source-player/dotnet.sh"]
    build = dotnet + ["build", project, "--no-restore", "-c", "Release", "--nologo", "-v", "quiet"]
    result = subprocess.run(build, cwd=ROOT, text=True, capture_output=True)
    if result.returncode and "NETSDK1004" in result.stdout + result.stderr:
        feed = artifacts / "empty-feed"
        feed.mkdir(exist_ok=True)
        subprocess.run(dotnet + ["restore", project, "--source", str(feed), "--nologo", "-v", "quiet"],
                       cwd=ROOT, check=True)
        result = subprocess.run(build, cwd=ROOT, text=True, capture_output=True)
    print(result.stdout + result.stderr, end="")
    result.check_returncode()
    run = dotnet + ["tests/source-instructional-admission/bin/core-input-intro/net10.0/SourceInputIntro.Checks.dll",
                    str(args.data.resolve()), str(path)]
    subprocess.run(run, cwd=ROOT, check=True)
    if args.mutations:
        controls = [
            ("missing-consumed-publication", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
             "            PublishSourceConsumedSwitches(command.StationInputs);\n", "", "SourceStepOverride",
             "physical scan is committed before the actual transform/producer callback"),
            ("intro-before-minimum", "Assets/Scripts/Gameplay/SourcePlaySelectionIntroductionProcess.cs",
             "&& waitTicks >= 40;", "&& waitTicks >= 1;", "IntroductionProcessOverride",
             "held own ABC cannot skip40-source-wake minimum"),
        ]
        for name, relative, old, new, property_name, expected in controls:
            source = ROOT / relative
            before = source.read_bytes()
            text = before.decode()
            if text.count(old) != 1:
                raise ValueError("The exact real-source control operand changed: " + name)
            work = artifacts / name
            if work.exists():
                raise ValueError("Refusing to overwrite an existing control footprint: " + str(work))
            work.mkdir()
            try:
                variant = work / source.name
                variant.write_text(text.replace(old, new, 1))
                compile_result = subprocess.run(build + [
                    "-p:" + property_name + "=" + str(variant),
                    "-p:IntermediateOutputPath=" + str(work / "obj") + "/",
                    "-p:OutputPath=" + str(work / "bin/net10.0") + "/",
                ], cwd=ROOT, text=True, capture_output=True)
                compile_result.check_returncode()
                checked = subprocess.run(dotnet + [str(work / "bin/net10.0/SourceInputIntro.Checks.dll"),
                                                   str(args.data.resolve()), str(path)],
                                         cwd=ROOT, text=True, capture_output=True)
                if checked.returncode == 0 or expected not in checked.stdout + checked.stderr:
                    raise ValueError("Real-source control did not fail at its actual contract: " + name
                                     + "\n" + checked.stdout + checked.stderr)
                print("PASS isolated compiled control: " + name + " -> " + expected)
            finally:
                if source.read_bytes() != before:
                    raise ValueError("Shared production source changed during isolated control: " + relative)
                shutil.rmtree(work)
        subprocess.run(run, cwd=ROOT, check=True)
    if (args.data / "manifest.json").read_bytes() != original:
        raise ValueError("The immutable original descriptor changed.")


if __name__ == "__main__":
    main()
