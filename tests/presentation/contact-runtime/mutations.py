#!/usr/bin/env python3
"""Compile real isolated Contact272 production mutations; demand named assertion failures."""

import argparse
import json
import shutil
import subprocess
import sys
import run

PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
SAMPLER = "Assets/Scripts/AssetImport/SourceActorPoseSampler.cs"
FACTORY = "Assets/Scripts/Runtime/SourceContactComposition.cs"
DEFAULT = (
    ("contact-key-aliases157", PACKAGE, 'ContactAnimationKey = "SourceData/local-plyr266-contact272-9cae962b"',
     'ContactAnimationKey = "SourceData/local-plyr266-movement157-f0ae9395"', "new272 resource key not old157"),
    ("contact-manifest-aliases157", PACKAGE, 'ContactAnimationSha256 = "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9"',
     'ContactAnimationSha256 = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"', "trusted new272 manifest identity"),
    ("contact-services-alias-oldSound", PACKAGE, 'ContactServicesResourceKey = "BlitzSourceInterface/original-contact272-soundvm-match-services"',
     'ContactServicesResourceKey = "BlitzSourceInterface/original-movement157-soundvm-match-services"', "new C1 services key"),
    ("trusted-manifest-check-lost", PACKAGE, "manifestSha256 != AnimationSha256 || ", "",
     "correct272 counts cannot conceal wrong manifest"),
    ("contact-frame-count-lost", PACKAGE, " || frames != FrameCount", "", "missing selected frame cannot be accepted"),
    ("current-puppet-pose-interpolated", SAMPLER, "return false;\n            long difference", "return true;\n            long difference",
     "real hit/puppet/attack output stays at current source snapshot"),
)
ACTUAL = (
    ("factory-SAFN-bind-lost", FACTORY, "game.BindSourceSoundVmData(soundFunctions);", "",
     "exact SAFN bound before factory returns/first wake"),
    ("factory-zero-reference-pivots", FACTORY, "new SourceCatchGeometry(animations, referencePivots)",
     "new SourceCatchGeometry(animations, new SourceVector3[SourceTiming.JointCount])", "actual Core/render root placement seam"),
    ("factory-detached-library-object", FACTORY, "new SourceCatchGeometry(animations, referencePivots)",
     "new SourceCatchGeometry((ISourceAnimationLibrary)typeof(object).GetMethod(\"MemberwiseClone\", "
     "System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(animations, null), referencePivots)",
     "actual collision/camera geometry receives SAME272 instance"),
    ("C1-impact-unbound", PACKAGE, "if (IsContact272 && (library.ContentSha256", "if (!IsContact272 && (library.ContentSha256",
     "old impact catalog cannot masquerade as C1 generation"),
    ("C1-root-uses-previous-snapshot", SAMPLER, "amount) : animation.RootOffsetSourceUnits;",
     "amount) : previous.Animation.RootOffsetSourceUnits;", "actual C1 source root/current-frame position not invented"),
    ("contact-plan-reverts157", PACKAGE, "SourceTeamResourceProfile.Contact272, soundVmMovement157.CopyMovement()",
     "SourceTeamResourceProfile.Movement157, soundVmMovement157.CopyMovement()", "actual C1 plan selects only new services key"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    run.arguments(parser)
    args = parser.parse_args()
    output = (args.output or run.SUITE / ".artifacts/mutations").resolve()
    run.require(output.is_relative_to(run.SUITE / ".artifacts") or
                output.is_relative_to(run.ROOT / ".squad/.scratch/contact-profile"), "mutations must stay in own ignored artifacts")
    output.mkdir(parents=True, exist_ok=True)
    work = output / "source"
    sources = run.source_inventory()
    rows = []
    actual = run.actual_arguments(args, output)
    cases = DEFAULT + (ACTUAL if actual else ())
    try:
        run.snapshot(sources, work)
        env = run.environment(output)
        for label, name, before, after, expected in cases:
            original = sources[name].decode()
            run.require(original.count(before) == 1, label + ": actual mutation site changed")
            changed = original.replace(before, after, 1).encode()
            (work / name).write_bytes(changed)
            assembly = run.compile_project(run.SUITE / "ContactPresentation.Checks.csproj", work, output, env)
            result = subprocess.run(["dotnet", str(assembly), *actual], cwd=run.ROOT, env=env,
                                    capture_output=True, text=True, timeout=300)
            log = result.stdout + result.stderr
            (output / (label + ".log")).write_text(log)
            run.require(result.returncode != 0 and "CONTACT ASSERTION: " + expected in log,
                        label + ": must fail its named causal assertion, not compilation/unrelated exception:\n" + log)
            rows.append({"name": label, "compiled": True, "rejected": True, "assertion": expected,
                         "mutated_source_sha256": run.sha(changed)})
            (work / name).write_bytes(sources[name])
            print("REJECTED compiled " + label, flush=True)
        run.require(run.source_inventory() == sources, "original source changed during isolated mutation campaign")
        print("PASS " + str(len(rows)) + " compiled Contact272 causal mutants rejected")
        return 0
    finally:
        (output / "result.json").write_text(json.dumps({
            "schema": "contact272-presentation-mutations/v1", "expected": len(cases), "mutants": rows,
            "complete": len(rows) == len(cases), "source_inventory_unchanged": run.source_inventory() == sources,
            "source_sha256": {name: run.sha(raw) for name, raw in sources.items()},
            "actual_data": bool(actual), "Editor_executed": False}, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    sys.exit(main())
