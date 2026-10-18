#!/usr/bin/env python3
"""Compile isolated actual C2 binding mutations and require their causal assertions."""
import argparse
import json
import shutil
import subprocess
import sys
import run

PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
FACTORY = "Assets/Scripts/Runtime/SourceC2Composition.cs"
TRANSPORT = "Assets/Scripts/Runtime/SourceSoundVmTransport.cs"
PLAYBACK = "Assets/Scripts/Presentation/SourceCinematicPlayback.cs"
CATALOG = "Assets/Scripts/AssetImport/SourceCinematicLibrary.cs"
DEFAULT = (
    ("new281-key-aliases272", PACKAGE, 'C2AnimationKey = "SourceData/local-plyr266-contact281-c2-e87401c8"',
     'C2AnimationKey = "SourceData/local-plyr266-contact272-9cae962b"', "C2 animation not272 alias"),
    ("wrong277-profile", PACKAGE, "C2StableId, C2AnimationKey, C2AnimationSha256, 281, 26230, false",
     "C2StableId, C2AnimationKey, C2AnimationSha256, 277, 26230, false",
     "full nine-script281 closure not277"),
    ("C2-JSON-resource-aliases-old6", PACKAGE, 'C2CallbackResourceKey = "SourceData/source-c2-callbacks-9d2cb7ac"',
     'C2CallbackResourceKey = "SourceData/sound-functions-safn1-4a557304"', "C2 JSON resource identity"),
    ("old6-pin-rewritten", PACKAGE,
     'SoundVmFunctionsSha256 = "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280"',
     'SoundVmFunctionsSha256 = "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904"',
     "old6 SAFN identity unchanged"),
)
M1_ACTUAL = (
    ("m1-reader-aliases-old-nine", PACKAGE,
     "return ReadC2CallbacksForProfile(callbacks, hitExtension, SourceTeamResourceProfile.Contact281C2M1);",
     "return ReadC2CallbacksForProfile(callbacks, hitExtension, SourceTeamResourceProfile.Contact281C2);",
     "M1 canonical14 schema and reader must load"),
    ("m1-factory-aliases-old-nine", FACTORY,
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations, referencePivots, playbook,",
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations, referencePivots, playbook,",
     "new factory rejects old-nine bootstrap must reject"),
    ("m1-missing-running-tuple6373", CATALOG,
     "if (cue.Script == script && cue.Line == originalLine && cue.Kind == kind) return cue;",
     "if (cue.Line != 6373 && cue.Script == script && cue.Line == originalLine && cue.Kind == kind) return cue;",
     "M1 exact14 catalog caller tuples including6373 must exist"),
)
ACTUAL = (
    ("bootstrap-omitted-until-after-AI", FACTORY, "rosters, presentation, movementData, movementSettings, c2Bootstrap, handicapSettings);",
     "rosters, presentation, movementData, movementSettings, null, handicapSettings);", "C2 bootstrap supplied before constructor bank RNG and AI"),
    ("legacy-SAFN-not-bound-before-wake", FACTORY, "game.BindSourceSoundVmData(legacySoundFunctions);", "",
     "legacy SAFN bound before first wake"),
    ("geometry-detached-same-content-library", FACTORY, "new SourceCatchGeometry(animations, referencePivots)",
     "new SourceCatchGeometry((ISourceAnimationLibrary)typeof(object).GetMethod(\"MemberwiseClone\", "
     "System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(animations, null), referencePivots)",
     "geometry SAME281 instance"),
    ("missing-new-speech-site3144", CATALOG,
     'if (cue.Script == script && cue.Line == originalLine && cue.Kind == kind) return cue;',
     'if (cue.Line != 3144 && cue.Script == script && cue.Line == originalLine && cue.Kind == kind) return cue;',
     "all8 speech and hit7538 catalog bindings"),
    ("actorless-native-fabricated0", TRANSPORT, "RequireNativeContext(dispatch).ActorId;", "0;",
     "native actorless whistle stays -1, not actor0"),
    ("native-legacy-getter-before-discriminator", TRANSPORT,
     "return dispatch.HasOriginalAnimationCue ? dispatch.OriginalCueContext.ActorPlayerId :",
     "return dispatch.OriginalCueContext.ActorPlayerId >= 0 ? dispatch.OriginalCueContext.ActorPlayerId :",
     "native getter discriminator must precede guarded legacy access"),
    ("hit-extension-dropped-as-wrong-kind", PLAYBACK,
     "c2Context.Kind != SourceC2SoundFunctionKind.HitFunctionExtension)",
     "c2Context.Kind != SourceC2SoundFunctionKind.TauntSpeech)",
     "correct C2 taunt/hit kind reaches resolved consumer"),
    ("playlist0-muted", TRANSPORT, "if (dispatch.Result.Calls.Count == 0) return;",
     "if (dispatch.Result.Calls.Count == 0 || dispatch.Result.Calls[0].PlaylistId == 0) return;",
     "playlist0 is nonempty raw output, never muted"),
    ("deferred-native-output-dropped", TRANSPORT,
     "nativePaired.Add(dispatch);\n                DeliverResolved(dispatch);",
     "nativePaired.Add(dispatch);\n                if (dispatch.C2Context.Kind == SourceC2SoundFunctionKind.WhistleImmediate) DeliverResolved(dispatch);",
     "both actual deferred native outputs retained in single FIFO"),
    ("raw-output-duplicated", TRANSPORT, "output.ConsumeResolved(dispatch);",
     "output.ConsumeResolved(dispatch); output.ConsumeResolved(dispatch);",
     "C2 literal recorded once, not audio playback proof"),
)
HANDICAP = (
    ("C1-handicap-constructor-omission", "Assets/Scripts/Runtime/SourceContactComposition.cs",
     "handicapSettings: handicapSettings);", "handicapSettings: null);",
     "factory C1 forwards explicit raw settings to actual constructor"),
    ("old-nine-handicap-entry-omission", FACTORY,
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations, referencePivots, playbook,\n"
     "                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,\n"
     "                c2Bootstrap, randomFactory, seed, handicapSettings);",
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2, animations, referencePivots, playbook,\n"
     "                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,\n"
     "                c2Bootstrap, randomFactory, seed, null);",
     "factory old-nine forwards explicit raw settings to actual constructor"),
    ("M1-handicap-entry-substitution", FACTORY,
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations, referencePivots, playbook,\n"
     "                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,\n"
     "                c2Bootstrap, randomFactory, seed, handicapSettings);",
     "return CreateChecked(SourceTeamResourceProfile.Contact281C2M1, animations, referencePivots, playbook,\n"
     "                configuration, rosters, presentation, movementData, movementSettings, legacySoundFunctions,\n"
     "                c2Bootstrap, randomFactory, seed, null);",
     "factory M1 forwards explicit raw settings to actual constructor"),
    ("shared-C2-handicap-constructor-omission", FACTORY,
     "rosters, presentation, movementData, movementSettings, c2Bootstrap, handicapSettings);",
     "rosters, presentation, movementData, movementSettings, c2Bootstrap, null);",
     "factory old-nine forwards explicit raw settings to actual constructor"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    run.arguments(parser); args = parser.parse_args()
    output = (args.output or run.SUITE / ".artifacts/mutations").resolve()
    run.require(output.is_relative_to(run.SUITE / ".artifacts"), "mutations stay in own ignored outputs")
    output.mkdir(parents=True, exist_ok=True)
    values = run.sources(); work = output / "source"; rows = []
    actual = run.input_args(args, output)
    cases = DEFAULT + (ACTUAL if actual else ()) + (M1_ACTUAL if args.m1_inputs else ()) + (HANDICAP if args.handicap_reference else ())
    try:
        run.snapshot(values, work); env = run.env_for(output)
        for label, path, before, after, assertion in cases:
            original = values[path].decode()
            run.require(original.count(before) == 1, label + ": actual mutation site changed")
            changed = original.replace(before, after, 1).encode(); (work / path).write_bytes(changed)
            assembly = run.compile_project(run.SUITE / "C2Presentation.Checks.csproj", work, output, env)
            compile_log = (output / "C2Presentation.Checks.log").read_bytes()
            (output / (label + ".compile.log")).write_bytes(compile_log)
            result = subprocess.run(["dotnet", str(assembly), *actual], cwd=run.ROOT, env=env,
                                    capture_output=True, text=True, timeout=300)
            log = result.stdout + result.stderr; (output / (label + ".log")).write_text(log)
            run.require(result.returncode != 0 and "C2 ASSERTION: " + assertion in log,
                        label + ": must fail named causal assertion, not compilation/unrelated exception:\n" + log)
            rows.append({"name": label, "compiled_zero_warnings_errors": True, "rejected": True, "assertion": assertion,
                         "mutated_source_sha256": run.sha(changed), "compile_log_sha256": run.sha(compile_log)})
            (work / path).write_bytes(values[path])
            print("REJECTED compiled " + label, flush=True)
        run.require(run.sources() == values, "source generation changed during mutation campaign")
        print("PASS " + str(len(rows)) + " compiled C2 production causal controls")
        return 0
    finally:
        (output / "result.json").write_text(json.dumps({"schema": "c2-production-causal-controls/v1",
            "complete": len(rows) == len(cases), "expected": len(cases), "mutants": rows,
            "source_inventory_unchanged": run.sources() == values, "source_sha256": {n: run.sha(b) for n, b in values.items()},
            "actual_data": bool(actual), "Editor_native_DSP_or_fullgame_proof": False}, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    sys.exit(main())
