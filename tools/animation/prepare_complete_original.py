#!/usr/bin/env python3
"""Extend the immutable282 library with the21 exact original TD/reaction clips."""

import argparse
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import importlib.util
import shutil

import prepare_player_sequences as selected

reader = selected.reader
BASE_SHA256 = "50aa432ff9b332ec9087133f5bccb43ae18e1ca7c0bf7830cbdd675d3e024046"
BASE_CALLBACK_SHA256 = "e9a2ccbfffda0e83ae9ba409e4fbfd6f6c6f074ebd024aa30367d9d06c29c653"
BASE_IMPACT_SHA256 = "456d525aa75ba343942613793cc437d6732f0b16d972a981537929daece9f3d7"
PROFILE = "original-teams-complete-original303"
CALLBACK_PROFILE = "OriginalRelease57Hz-C2-M1-CompleteOriginal303-InitSoundBanks-v1"
PINS = {
    "anim/ENDZONE.WAN": "e272649c098b26db7e26757f11b1da267e5ffb5e",
    "anim/inzone99.wan": "b0337c15b7691aca59a7c7876f21a5e5bdf0948e",
    "anim/misc.wan": "da6818b994f1e4ba93e48e151382a8dc96f27f58",
    "anim/misc3.wan": "ba79820423945efc7c45c2b66fe719511090a1ba",
    "anim/misc4.wan": "911c4f39800c5cd406fd5148cc3d51482b19d1f1",
    "anim/Runs.wan": "2f835b476d64d6b0d5cc37b0cf8ec2c0a997ec9e",
}
CLIPS = (
    ("ENDZ_06", "anim/ENDZONE.WAN", 163, 163, 162),
    ("ENDZ_08", "anim/ENDZONE.WAN", 75, 75, 74),
    ("ENDZ_20", "anim/ENDZONE.WAN", 171, 171, 170),
    ("M_EZDNCE", "anim/inzone99.wan", 369, 185, 368),
    ("M_EZJASN", "anim/inzone99.wan", 194, 97, 192),
    ("M_EZPUSH", "anim/inzone99.wan", 290, 145, 288),
    ("M_EZJON1", "anim/inzone99.wan", 300, 150, 298),
    ("M_EZJON2", "anim/inzone99.wan", 300, 150, 298),
    ("INZONE1", "anim/misc.wan", 333, 333, 332),
    ("INZONE2", "anim/misc.wan", 242, 242, 241),
    ("INZONE3", "anim/misc.wan", 388, 388, 387),
    ("M_CELCHE", "anim/misc3.wan", 295, 295, 294),
    ("M_TAUNBC", "anim/misc3.wan", 215, 108, 214),
    ("M_EZCTHR", "anim/misc3.wan", 220, 220, 219),
    ("M_EZCWHA", "anim/misc3.wan", 275, 275, 274),
    ("M_EZWAV1", "anim/misc3.wan", 335, 335, 334),
    ("M_EZCGUN", "anim/misc3.wan", 400, 300, 299),
    ("M_EZCSPN", "anim/misc3.wan", 325, 325, 324),
    ("M_EZCHRS", "anim/misc4.wan", 350, 151, 300),
    ("R_SLO_D1", "anim/Runs.wan", 186, 186, 185),
    ("R_SLO_D2", "anim/Runs.wan", 218, 218, 217),
)

AV_PROFILE = "original-teams-complete-av306"
AV_FEATURE = "source-complete-original-animation-course-audiovisual/v3"
AV_FACTORY = "SourceC2Composition.CreateCompleteAv306Simulation"
AV_CALLBACK_PROFILE = "OriginalRelease57Hz-C2-M1-CompleteAv306-InitSoundBanks-v1"
AV_ANCESTOR = "aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd"
AV_MANIFEST = "51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d"


def materialize_av306(base, av_delta, team_audio, manifest_bytes, output):
    base, av_delta, team_audio, output = map(selected._absolute, (base, av_delta, team_audio, output))
    for path in (base, av_delta, team_audio, output):
        selected._no_symlinks(path)
    selected._require(output.is_relative_to(reader.wan.SCRATCH) and not output.exists(),
                      "Materialize only a create-new ignored complete AV generation.")
    base_manifest = (base / "manifest.json").read_bytes()
    selected._require(sha(base_manifest) == AV_ANCESTOR and sha(manifest_bytes) == AV_MANIFEST,
                      "Exact immutable1114 ancestor and separately authenticated1153 AV manifest are required.")
    inherited = {entry["path"]: entry for entry in json.loads(base_manifest)["files"]}
    manifest = json.loads(manifest_bytes)
    pins = {entry["path"]: entry for entry in manifest["files"]}
    selected._require(len(pins) == 1153 and sum(entry["bytes"] for entry in pins.values()) == 300324289 and
                      all(pins.get(path) == entry for path, entry in inherited.items()),
                      "The complete AV generation must retain all1114 exact payload pins.")
    output.mkdir(parents=True)
    for path, pin in sorted(pins.items()):
        if path in inherited:
            source = base / path
        elif path == "audio/complete-av306-manifest.json":
            source = team_audio / "manifest.json"
        elif path.startswith("audio/team-speech/"):
            source = team_audio / path.removeprefix("audio/")
        else:
            source = av_delta / path
        selected._require(source.is_file() and source.stat().st_size == pin["bytes"] and
                          sha(source.read_bytes()) == pin["sha256"], "Actual AV input differs: " + path)
        destination = output / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    (output / "manifest.json").write_bytes(manifest_bytes)
    for path in output.rglob("*"):
        if path.is_file():
            path.chmod(0o400)
    for path in sorted((path for path in output.rglob("*") if path.is_dir()), reverse=True):
        path.chmod(0o500)
    output.chmod(0o500)
    selected._require((base / "manifest.json").read_bytes() == base_manifest, "Immutable ancestor changed.")
    return dict(schema="actual-complete-av306-readonly-materialization/v1", status="all1153-payloads-verified",
                manifestSha256=AV_MANIFEST, files=len(pins), bytes=300324289,
                inherited1114BytesUnchanged=True, output=str(output), permissions="owner-read-only files400/directories500")


def prepare_av_supplement(source, base, output, tree_pins):
    source, base, output = map(selected._absolute, (source, base, output))
    for path in (source, base, output):
        selected._no_symlinks(path)
    selected._require(output.is_relative_to(reader.wan.SCRATCH) and not output.exists(),
                      "Choose a create-new ignored complete AV output.")
    selected._require(tree_pins["sha"] == "a7e0352125d46075b7d98e87253ab89b9160a4c7"
                      and tree_pins["truncated"] is False, "The actual pinned original Git tree is required.")
    pins = {entry["path"]: entry["sha"] for entry in tree_pins["entries"]}
    def original(path):
        raw = (source / path).read_bytes()
        selected._require(reader.wan.git_blob_sha1(raw) == pins[path], "Original Git blob changed: " + path)
        return raw
    raw_manifest = (base / "manifest.json").read_bytes()
    selected._require(sha(raw_manifest) == AV_ANCESTOR, "The immutable actual1114 ancestor is required.")
    manifest = json.loads(raw_manifest)
    inherited = {entry["path"]: entry for entry in manifest["files"]}
    def inherited_bytes(role):
        path = manifest["entrypoints"][role]
        raw = (base / path).read_bytes()
        selected._require(sha(raw) == inherited[path]["sha256"], "Inherited entrypoint changed: " + role)
        return raw
    raw_inventory = inherited_bytes("animations")
    inventory = json.loads(raw_inventory)
    selected._require(inventory["profile"] == PROFILE and len(inventory["clips"]) == 303,
                      "The complete actual303 original inventory is required.")
    outputs, additions, input_provenance = {}, [], {}
    for name, path in (("RUSH_I", "anim/Line.wan"), ("QB_RDY", "anim/tmsel.wan"), ("QB_RDYF", "anim/tmsel.wan")):
        raw = original(path)
        input_provenance[path] = dict(path=path, git_blob_sha1=pins[path], sha256=sha(raw), bytes=len(raw))
        clip = reader.full_clip(raw, name, {
            "repository": "historicalsource/nflblitz2000", "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
            "path": path, "git_blob_sha1": pins[path], "local_input": path,
            "source_linkage": "actual pinned tree/path and independently verified Git blob",
        })
        encoded = reader.encode_clip(clip).encode("utf-8")
        sequence = clip["sequence"]
        entry = selected._inventory_entry({
            "runtime_name": name, "source_path": path, "source_git_blob_sha1": pins[path],
            "source_frames": sequence["source_frame_count"], "selected_frames": sequence["selected_frame_count"],
            "source_first": sequence["selected_source_frame_indices"][0],
            "source_last": sequence["selected_source_frame_indices"][-1],
        }, clip, encoded)
        selected._require(entry["runtime_name"] not in {value["runtime_name"] for value in inventory["clips"]},
                          "The three original animation additions must be disjoint.")
        outputs["animation/" + entry["path"]] = encoded
        additions.append(entry)
    inventory.update(profile=AV_PROFILE, base_complete_original303_inventory_sha256=sha(raw_inventory),
                     producer_policy="immutable-original303-plus-three-initial-team-ready-clips/v1",
                     clips=sorted(inventory["clips"] + additions, key=lambda value: value["runtime_name"]))
    frame_count = sum(entry["selected_frames"] for entry in inventory["clips"])
    outputs["animation/export-inventory-complete-av306.json"] = reader.encode_clip(inventory).encode("utf-8")
    callbacks = json.loads(inherited_bytes("callbacks"))
    callbacks.update(profile=AV_CALLBACK_PROFILE,
                     animation_manifest_sha256=sha(outputs["animation/export-inventory-complete-av306.json"]),
                     base_complete_original303_callback_sha256=sha(inherited_bytes("callbacks")))
    callbacks["format_source"].update(clip_count=306, selected_frame_count=frame_count)
    outputs["kernel/c2-complete-av306-callback-data.json"] = canonical(callbacks)
    impact = json.loads(inherited_bytes("impact"))
    impact["schema"] = "original-impact-complete-av306/v1"
    impact["contact_binding"].update(
        animation_manifest_sha256=sha(outputs["animation/export-inventory-complete-av306.json"]),
        c2_payload_sha256=sha(outputs["kernel/c2-complete-av306-callback-data.json"]),
        base_complete_original303_catalog_sha256=sha(inherited_bytes("impact")))
    outputs["kernel/impact-complete-av306-presentation.json"] = canonical(impact)
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "rosters"))
    from c_source import CData, Macro
    camera_raw, header_raw = original("CamData.c"), original("include/camdefs.h")
    game_raw, animation_header = original("include/GAME.H"), original("include/ani3d.h")
    game = CData(game_raw.decode("latin1"), {"SEATTLE": Macro("1")})
    animation = CData(animation_header.decode("latin1"), game.expressions.macros).enums()
    environment = animation.expressions.macros
    environment["DG2RD"] = Macro("(x)", ("x",))
    header = CData(header_raw.decode("latin1"), environment)
    camera = CData(camera_raw.decode("latin1"), header.expressions.macros)
    table, _ = camera.array("csi_cd_endplay")
    entries = []
    for ordinal, row in enumerate(table[:-1]):
        mode = int(camera.expressions.evaluate(row[0].raw))
        points = []
        if ordinal:
            symbol = row[6].raw.removeprefix("&")
            scalars, _ = camera.array(symbol)
            values = [float(camera.expressions.evaluate(value.raw)) for value in scalars]
            selected._require(len(values) % 3 == 0 and values[-3:] == [0, 0, 0],
                              "The original course stream needs its exact zero terminator.")
            points = [values[index:index + 3] for index in range(0, len(values), 3)]
        entries.append(dict(ordinal=ordinal, source_line=row[0].line, mode=mode,
                            mode_symbol=row[0].raw, no_selection=ordinal == 0,
                            mode_ticks=int(camera.expressions.evaluate(row[1].raw)),
                            travel_ticks=int(camera.expressions.evaluate(row[2].raw)),
                            theta_degrees=0, phi_degrees=0, distance=0, control_points=points))
    selected._require(len(entries) == 7 and all(entry["mode"] == 17 for entry in entries[1:]),
                      "The original result-box camera requires one NOP and six CAM_CO_C entries.")
    course = dict(schema="original-result-course-camera/v1", profile=AV_PROFILE,
                  source_revision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                  source_tree=tree_pins["sha"], tick_rate=57, table="csi_cd_endplay", entries=entries,
                  units="original centered source-world XYZ; authored points unscaled",
                  inputs=[dict(path=path, git_blob_sha1=pins[path], sha256=sha(raw), bytes=len(raw))
                          for path, raw in (("CamData.c", camera_raw), ("include/camdefs.h", header_raw),
                                            ("include/GAME.H", game_raw), ("include/ani3d.h", animation_header))])
    outputs["camera/complete-av306-course.json"] = canonical(course)
    manifest.update(schema="blitz-portable-source-player/v3", profile=AV_PROFILE,
                    featureContract=AV_FEATURE, factory=AV_FACTORY, ancestorManifestSha256=AV_ANCESTOR)
    manifest["entrypoints"].update(animations="animation/export-inventory-complete-av306.json",
                                  callbacks="kernel/c2-complete-av306-callback-data.json",
                                  impact="kernel/impact-complete-av306-presentation.json",
                                  courseCamera="camera/complete-av306-course.json")
    for path, raw in outputs.items():
        selected._require(path not in inherited, "The AV successor must not overwrite inherited1114 bytes: " + path)
        manifest["files"].append(dict(path=path, bytes=len(raw), sha256=sha(raw)))
    manifest["files"].sort(key=lambda entry: entry["path"])
    outputs["manifest.json"] = canonical(manifest)
    output.mkdir(parents=True)
    for name, raw in outputs.items():
        path = output / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    selected._verify_bundle(output, outputs)
    selected._require((base / "manifest.json").read_bytes() == raw_manifest, "The immutable ancestor changed.")
    return dict(schema="original-complete-av306-supplement/v1", profile=AV_PROFILE,
                featureContract=AV_FEATURE, factory=AV_FACTORY, ancestorManifestSha256=AV_ANCESTOR,
                files=[dict(path=path, bytes=len(raw), sha256=sha(raw)) for path, raw in sorted(outputs.items())],
                animationManifestSha256=sha(outputs["animation/export-inventory-complete-av306.json"]),
                callbackSha256=sha(outputs["kernel/c2-complete-av306-callback-data.json"]),
                impactSha256=sha(outputs["kernel/impact-complete-av306-presentation.json"]),
                courseSha256=sha(outputs["camera/complete-av306-course.json"]),
                manifestSha256=sha(outputs["manifest.json"]), clips=306, selectedFrames=frame_count,
                courseEntries=len(entries), coursePoints=[len(entry["control_points"]) for entry in entries],
                addedClips=additions, inheritedFiles=len(inherited), totalFiles=len(manifest["files"]),
                inheritedBytesUnchanged=True, originalClipInputs=list(input_provenance.values()))

def reachable_inventory(source, runtime, coverage=None):
    root = Path(__file__).resolve().parents[2]
    sys.path.insert(0, str(root / "tools/rosters"))
    from c_source import CData, closing_brace
    spec = importlib.util.spec_from_file_location("original_animation_source", root / "tests/contact/source.py")
    original = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(original)
    pins = dict(original.PINS, **{
        "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
        "cambot.c": "00c9c913bc6a697dfd590666bf5597f2c2bc1eb7",
        "plyrinfo.c": "e955e1c98a0fd0123ce0b865ddafdfd7c32c59d4",
        "sounds.c": "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9",
        "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
        "playsel.c": "f5e87b921a4038d03f55174bbd5425241939e798",
    })
    paths = ("player.c", "detect.c", "drone.c", "nfl.c", "playsel.c", "plyrinfo.c",
             "sounds.c", "playbook.c", "cambot.c", "lib/ani3d/ANIM.C", "CamData.c")
    functions, texts, source_pins = {}, {}, {}
    for path in paths:
        raw = (source / path).read_bytes()
        blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        if path in pins and blob != pins[path]:
            raise ValueError("Reachability source Git blob differs: " + path)
        source_pins[path] = dict(gitBlob=blob, sha256=sha(raw))
        text = CData(raw.decode("latin1")).text
        texts[path] = text
        for definition in re.finditer(r"(?m)^(?:static\s+)?(?:void|int|float|char\s*\*)\s+(\w+)\s*\([^;{}]*\)\s*\{", text):
            end = closing_brace(text, definition.end() - 1)
            functions[definition[1]] = dict(path=path, line=text[:definition.start()].count("\n") + 1,
                                          body=text[definition.start():end + 1])
    scripts = original.scripts((source / "plyrseq.c").read_bytes())
    tables, all_text = {}, "\n".join(texts.values())
    for declaration in re.finditer(r"\b(\w+)\s*(?:\[[^]\n]*\]\s*)+(?:\)\s*\([^;{}]*\))?\s*=\s*\{", all_text):
        end = closing_brace(all_text, declaration.end() - 1)
        tables.setdefault(declaration[1], set()).update(re.findall(r"\b\w+\b", all_text[declaration.end():end]))
    roots = set(("InitGameProc", "game_proc", "player_proc", "tmsel_player_proc", "detect_proc", "ball_proc",
                 "pre_player_proc", "last_player_proc", "cambot_proc", "plyrinfo_proc", "update_arrows_proc",
                 "result_box", "show_end_quarter_proc", "play_select", "versus_speech_proc", "pre_snap_speech_proc"))
    visited, selected, callers, pending = set(), set(), {}, [(name, "production-root") for name in sorted(roots & functions.keys())]
    while pending:
        name, caller = pending.pop()
        callers.setdefault(name, set()).add(caller)
        if name in visited or name in selected:
            continue
        if name in functions:
            visited.add(name)
            identifiers = set(re.findall(r"\b\w+\b", functions[name]["body"]))
        else:
            selected.add(name)
            identifiers = set(re.findall(r"\b\w+\b", "\n".join(row[2] for row in scripts[name]["rows"])))
        expanded = set(identifiers)
        for word in identifiers:
            expanded.update(tables.get(word, ()))
        pending.extend((target, name) for target in sorted(expanded & (functions.keys() | scripts.keys())))
    ported = {entry["symbol"]: entry for entry in runtime["programs"]}
    hits = {} if coverage is None else coverage.get("instructions", {})
    animation = []
    for name in sorted(selected):
        entry = ported.get(name)
        source_rows = scripts[name]["rows"]
        conditions = [dict(line=line, instruction=text) for line, kind, text in source_rows
                      if kind == "byte" and text.split(",")[0] in
                      ("ANI_CODE_BNZ", "ANI_CODE_BZ", "ANI_JIFNSEQ", "ANI_WAITTRUE", "ANI_WAITFALSE")]
        covered = hits.get(name, {})
        excluded = name == "m_hophop_anim"
        animation.append(dict(symbol=name, sourceLine=scripts[name]["line"], callers=sorted(callers[name]),
            status="excluded-unreachable" if excluded else "missing" if entry is None else
                   "implemented+production-covered" if covered else "verification-only",
            implementation=entry is not None, productionInstructionCoverage=covered,
            conditions=conditions, opcodes=sorted({text.split(",")[0] for _, kind, text in source_rows
                                                   if kind == "byte" and text.startswith("ANI_")}),
            qualification="player.c:2785 randrng(10)<35 is always true; the else hop is unreachable" if excluded else
                          "Static call/table/ANI closure is conservative; per-condition production coverage is separate."))
    sound_code = (root / "Assets/Scripts/Gameplay/SourceSoundVm.cs").read_text()
    providers = {
        "announce_eoq_speech": "BeginSourceOriginalPeriodSound", "eoq_speech_proc": "DispatchSourceOriginalPeriodSpeech",
        "clock_low_sp": "SourceOriginalSelectionClockSpeech", "need_big_play_sp": "SourceOriginalSelectionClockSpeech",
        "extra_point_speech": "SourceOriginalFreePointSound", "throw_ball_sounds": "SourceOriginalThrowBallSounds",
        "catch_sounds": "SourceOriginalCatchSounds", "catch_speech_proc": "DispatchSourceOriginalGameplayChild",
        "on_fire_sp": "DispatchSourceOriginalGameplayChild", "fumble_ball_sounds": "SourceOriginalFumbleBallSounds",
        "bobble_ball_sounds": "SourceOriginalBobbleBallSounds", "deflected_ball_sp": "DispatchSourceOriginalGameplayChild",
        "hurdle_speech": "EmitSourceOriginalMovementSpeech", "spin_move_speech": "EmitSourceOriginalMovementSpeech",
        "stiff_arm_speech": "EmitSourceOriginalMovementSpeech", "crowd_cheer_snds": "SourceOriginalCrowdCheer",
        "crowd_tackle_snds": "ExecuteSourceOriginalContactSound", "monkey_on_back_sp": "ExecuteSourceOriginalContactSound",
        "qb_duck_speech": "ExecuteSourceOriginalContactSound", "hit_intended_receiver_sp": "ExecuteSourceOriginalContactSound",
        "hit_intended_receiver_sp_proc": "DispatchSourceOriginalContactChild", "late_hit_speech": "ExecuteSourceOriginalContactSound",
        "late_hit_speech_proc": "DispatchSourceOriginalContactChild", "head_plow_speech": "DispatchSourceOriginalContactChild",
        "attack_snd": "SourceOriginalAudioFunctions.SelectAttack", "hut_snd": "SourceOriginalAudioFunctions.SelectHut",
        "get_hit_snd": "SourceAudioFunctions.SelectCompleteOriginal", "grnd_hit_snd": "SourceAudioFunctions.SelectCompleteOriginal",
        "taunt_speech": "SourceC2SoundFunctions.Select", "plyr_open_sp": "SourceOriginalAudioFunctions.SelectSpeechTable",
        "tackle_spch": "ExecuteSourceOriginalSoundVm", "sacked_sp_proc": "DispatchSourceOriginalSpeechChild",
        "tackled_sp_proc": "DispatchSourceOriginalSpeechChild", "tackled2_sp_proc": "DispatchSourceOriginalSpeechChild",
        "intercepted_ball_sp": "GameSimulation.ExecuteSourceInterceptionSpeech",
        "disappointed_speech": "GameSimulation.SourceTouchdownCelebration",
    }
    sound_coverage = {} if coverage is None else coverage.get("sounds", {})
    audio = [dict(symbol=name, sourcePath=functions[name]["path"], sourceLine=functions[name]["line"],
                  callers=sorted(callers[name]), provider=providers.get(name),
                  status="missing" if name not in providers else "implemented+production-covered"
                         if sound_coverage.get(name) else "verification-only",
                  productionCases=sound_coverage.get(name, []))
             for name in sorted(visited) if functions[name]["path"] == "sounds.c"]
    camera_names = set(re.findall(r"\bCAMSEQINFO\s+(csi_\w+)\s*\[", texts["CamData.c"]))
    camera_callers = {}
    for name in visited:
        for table in set(re.findall(r"\bcsi_\w+\b", functions[name]["body"])) & camera_names:
            camera_callers.setdefault(table, []).append(name)
    for name in selected:
        for table in set(re.findall(r"\bcsi_\w+\b", "\n".join(row[2] for row in scripts[name]["rows"]))) & camera_names:
            camera_callers.setdefault(table, []).append(name)
    camera_coverage = {} if coverage is None else coverage.get("cameras", {})
    admitted = {table["name"]: table for table in runtime["cameras"]}
    cameras = [dict(symbol=name, callers=sorted(set(camera_callers[name])),
                    status="missing" if name not in admitted and name not in ("csi_kickoff", "csi_fs_fgtrk") else
                           "implemented+production-covered" if camera_coverage.get(name) else "verification-only",
                    productionCases=camera_coverage.get(name, []))
               for name in sorted(camera_callers)]
    hud_functions = [dict(symbol=name, sourceLine=functions[name]["line"], callers=sorted(callers[name]),
        status="verification-only", qualification="Requires caller-specific source-wake and real HUD primitive coverage; inventory is not proof.")
        for name in sorted(visited) if functions[name]["path"] == "plyrinfo.c"]
    return dict(schema="normal57-a1-v1-reachability/v1", sourceRevision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                profile=dict(ticksPerSecond=57, useOldQb=False, debug=False, noSetCam=False),
                sourcePins=source_pins, manifestSha256=runtime["manifestSha256"], roots=sorted(roots & functions.keys()),
                animation=animation, audio=audio, cameras=cameras, hud=hud_functions,
                runtimePrograms=len(ported), reachableFunctions=len(visited),
                exclusions=["csi_replay1 and cambot replay body are under #if0; playback is FALSE.",
                            "Debug stick-camera mode selectors are not normal-release gameplay.",
                            "Imported/admitted clip or program presence does not establish any production branch coverage."])


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n").encode("ascii")


def pinned(root, name, expected):
    raw = selected._read_bytes(selected._absolute(root / name), 8 * 1024 * 1024)
    selected._require(sha(raw) == expected, "Frozen inherited input changed: " + name)
    return raw


def prepare(source, base, output):
    source, base, output = map(selected._absolute, (source, base, output))
    selected._no_symlinks(source)
    selected._no_symlinks(base)
    selected._no_symlinks(output)
    selected._require(output.is_relative_to(reader.wan.SCRATCH) and output != reader.wan.SCRATCH
                      and not output.exists(), "Choose a create-new ignored animation output.")
    for root in (source, base):
        selected._require(not output.is_relative_to(root) and not root.is_relative_to(output),
                          "Original/inherited/output roots must not overlap.")
    original_raw = pinned(base, "animation/export-inventory-presnap-switch.json", BASE_SHA256)
    original = json.loads(original_raw, object_pairs_hook=selected._unique_object)
    selected._require(len(original["clips"]) == 282
                      and len({e["runtime_name"] for e in original["clips"]}) == 282,
                      "The complete actual282 inventory is required.")
    outputs = {}
    for entry in original["clips"]:
        outputs[entry["path"]] = pinned(base, "animation/" + entry["path"], entry["sha256"])
    cache, additions = {}, []
    for name, path, source_frames, frames, last in CLIPS:
        if path not in cache:
            cache[path] = selected._read_source(source, path)
        raw = cache[path]
        clip = reader.full_clip(raw, name, {
            "repository": "historicalsource/nflblitz2000",
            "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
            "path": path, "git_blob_sha1": PINS[path], "local_input": path,
            "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
        })
        encoded = reader.encode_clip(clip).encode("utf-8")
        entry = selected._inventory_entry({
            "runtime_name": name, "source_path": path, "source_git_blob_sha1": PINS[path],
            "source_frames": source_frames, "selected_frames": frames, "source_first": 0, "source_last": last,
        }, clip, encoded)
        selected._require(entry["path"] not in outputs, "The TD extension must be disjoint from282.")
        outputs[entry["path"]] = encoded
        additions.append(entry)
    inventory = copy.deepcopy(original)
    inventory.update(profile=PROFILE, producer_policy="immutable-source282-plus-disjoint-exact21-td-reactions/v1",
                     base_presnap_switch_animation_manifest_sha256=BASE_SHA256,
                     clips=sorted(original["clips"] + additions, key=lambda e: e["runtime_name"]))
    frame_count = sum(e["selected_frames"] for e in inventory["clips"])
    selected._require(len(inventory["clips"]) == 303
                      and len({e["runtime_name"] for e in inventory["clips"]}) == 303,
                      "The new inventory must contain303 unique original sequences.")
    outputs["export-inventory.json"] = reader.encode_clip(inventory).encode("utf-8")
    callbacks = json.loads(pinned(base, "kernel/c2-presnap-switch-callback-data.json", BASE_CALLBACK_SHA256))
    callbacks.update(profile=CALLBACK_PROFILE, animation_manifest_sha256=sha(outputs["export-inventory.json"]),
                     base_presnap_switch_callback_payload_sha256=BASE_CALLBACK_SHA256)
    callbacks["format_source"].update(clip_count=303, selected_frame_count=frame_count)
    outputs["c2-complete-original-callback-data.json"] = canonical(callbacks)
    impact = json.loads(pinned(base, "kernel/impact-presnap-switch-presentation.json", BASE_IMPACT_SHA256))
    impact["schema"] = "original-impact-complete-original303/v1"
    impact["contact_binding"].update(animation_manifest_sha256=sha(outputs["export-inventory.json"]),
        c2_payload_sha256=sha(outputs["c2-complete-original-callback-data.json"]),
        base_presnap_switch_catalog_sha256=BASE_IMPACT_SHA256)
    outputs["impact-complete-original-presentation.json"] = canonical(impact)
    output.mkdir(parents=True)
    for name, raw in outputs.items():
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    selected._verify_bundle(output, outputs)
    selected._require(pinned(base, "animation/export-inventory-presnap-switch.json", BASE_SHA256)
                      == original_raw, "Immutable282 inventory changed during preparation.")
    return dict(schema="original-complete303-animation-preparation/v1", profile=PROFILE,
                output=str(output), clips=303, selectedFrames=frame_count, addedClips=additions,
                inheritedManifestSha256=BASE_SHA256,
                files=[dict(path=name, bytes=len(raw), sha256=sha(raw))
                       for name, raw in sorted(outputs.items())],
                animationManifestSha256=sha(outputs["export-inventory.json"]),
                callbackSha256=sha(outputs["c2-complete-original-callback-data.json"]),
                impactSha256=sha(outputs["impact-complete-original-presentation.json"]),
                scriptsImplemented=False)


def compare_vm_observations(source, observations, output):
    """Run unchanged original routines on the admitted decoded-float clip boundary."""
    import struct
    import subprocess
    root = Path(__file__).resolve().parents[2]
    sys.path.insert(0, str(root / "tests/contact"))
    import source as original
    source, output = selected._absolute(source), selected._absolute(output)
    selected._no_symlinks(output)
    selected._require(output.is_relative_to(reader.wan.SCRATCH) and not output.exists(),
                      "Choose a create-new ignored original VM comparison output.")
    output.mkdir(parents=True)
    pins = {
        "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
        "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
        "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
        "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
        "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
        "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    }
    raw = {}
    for path, pin in pins.items():
        raw[path] = (source / path).read_bytes()
        selected._require(original.blob(raw[path]) == pin, "Original VM source blob differs: " + path)
    data = json.loads(observations.read_bytes())
    selected._require(data["schema"] == "actual-original-animation-vm-observations/v1",
                      "Actual production VM observations are required.")
    scripts = original.scripts(raw["plyrseq.c"])
    owners = {name: name for name in scripts}
    for name, script in scripts.items():
        owners.update({text: name for _, kind, text in script["rows"] if kind == "label"})
    needed = {action["script"] for case in data["cases"] for action in case["actions"] if action["script"]}
    pending = list(needed)
    while pending:
        for _, kind, text in scripts[pending.pop()]["rows"]:
            for value in text.split(",") if kind == "word" else ():
                if value in owners and owners[value] not in needed:
                    needed.add(owners[value]); pending.append(owners[value])
    table = re.search(rb"PANIF ani_functions\[256\]\s*=\s*\{(.*?)\};", raw["lib/ani3d/ANIM.C"], re.S)[1]
    functions = re.findall(r"_ani_\w+|NULL", table.decode("ascii"))
    constants = {name[1:].upper(): index | 0x80 for index, name in enumerate(functions) if name != "NULL"}
    defines = {}
    for path in ("include/ani3d.h", "include/player.h"):
        for name, value in re.findall(rb"(?m)^\s*#define\s+(\w+)\s+([^\r\n]+)", raw[path]):
            name, value = name.decode(), value.decode().split("//", 1)[0].strip()
            if name.startswith(("MODE_", "AA_", "AH_", "PF_ANITICK", "RF_TURNHACK", "SCRIPT")):
                defines[name] = value
    def integer(text):
        text = text.strip().strip("()")
        if "|" in text:
            result = 0
            for word in text.split("|"): result |= integer(word)
            return result
        if text in constants: return constants[text]
        if text in defines: return integer(defines[text])
        return int(text, 0)
    def number(value):
        return struct.unpack("<f", struct.pack("<f", value))[0].hex() + "f"
    clip_names = [clip["name"] for clip in data["clips"]]
    clip_ids = {name: index for index, name in enumerate(clip_names)}
    arrays, labels, fixups = {}, {}, []
    for name in sorted(needed):
        buf = bytearray((128,)); labels[name] = (name, 1)
        for _, kind, text in scripts[name]["rows"]:
            if kind == "label": labels[text] = (name, len(buf)); continue
            for item in map(str.strip, text.split(",")):
                if kind == "byte": buf.append(integer(item) & 255)
                elif kind == "float": buf.extend(struct.pack("<f", float(item)))
                elif item.startswith("SEQ_"):
                    selected._require(item[4:] in clip_ids, "Unobserved original clip required: " + item)
                    buf.extend(struct.pack("<I", clip_ids[item[4:]]))
                elif item in owners or item == "is_inplay":
                    fixups.append((name, len(buf), item)); buf.extend(b"\0" * 4)
                elif re.fullmatch(r"-?\d+", item) or item.startswith(("MODE_", "AA_", "PF_")):
                    buf.extend(struct.pack("<i", integer(item)))
                else:
                    # Paths reaching an unqualified callback abort, rather than inventing its ABI/return.
                    fixups.append((name, len(buf), "unsupported")); buf.extend(b"\0" * 4)
        arrays[name] = buf
    body = []
    for path, names in (
        ("lib/ani3d/ANIM.C", ("_animate_block", "_next_frame", "_interpolate_frame", "_stream_frame",
          "_ani_nop", "_ani_end", "_ani_newseq", "_ani_skipframes", "_ani_setmode", "_ani_goto",
          "_ani_waittrue", "_ani_zerovels", "_ani_inttoseq", "_ani_runtoend", "_ani_intstream",
          "_ani_runtoframe", "_ani_streamtoend", "change_anim", "change_anim1", "change_anim2", "animate_model")),
        ("lib/ani3d/pipe3D.c", ("qxq", "slerp", "isin", "icos", "asin_rad", "acos_rad", "sin_rad")),
        ("player.c", ("is_inplay",)),
    ):
        for name in names:
            _, _, text = original.function(raw[path], name)
            body.append(text.decode("latin1"))
    header = raw["include/ani3d.h"].decode("latin1")
    types = []
    for tag in ("anim_header", "_anidata", "_obj_3d"):
        match = re.search(r"typedef struct " + tag + r"\s*\{.*?\}\s*\w+;", header, re.S)
        selected._require(match is not None, "Original type is required: " + tag)
        types.append(match[0])
    declarations = [
        "#include <stdint.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <math.h>\n",
        "#define TRUE 1\n#define FALSE 0\n#define PI_RAD 3.1415926f\n#define EPSILON .00001f\n#define HALFPI (PI_RAD*.5f)\n",
        "#define GRAVITY -.08f\n#define LESSER(a,b) ((a)<(b)?(a):(b))\n#define ANI_DEBUG 0\n",
        "#define X 0\n#define Y 1\n#define Z 2\n#define W 3\n#define GM_IN_PLAY 2\n",
        "typedef void *Texture_node_t;\n" + "\n".join(types),
        "typedef struct fbplyr_data {obj_3d odata; struct fbplyr_data *puppet_link;} fbplyr_data;\n",
        "static struct {int team_fire[2],game_mode;} game_info;\n",
        "static char *anim_blocks[1]; static char *seq_names[4096]; static float lookup[256];\n",
        "static int interpolate_hack,raw_random,random_calls; static float fsqrt(float x){return sqrtf(x);}\n",
        "static int randrng(int n){random_calls++;return raw_random%n;}\n",
        "static void unsupported(void){fprintf(stderr,\"Unqualified original callback/puppet branch\\n\");abort();}\n",
        "static void set_puppet_pos(obj_3d *a,obj_3d *b){(void)a;(void)b;unsupported();}\n",
        "static void lockup(void){abort();} static void sleep(int n){(void)n;abort();}\n",
        "#define GETWORD(x) ((int32_t)((uint32_t)(uint8_t)pani->pscript[(x)]+((uint32_t)(uint8_t)pani->pscript[(x)+1]<<8)+((uint32_t)(uint8_t)pani->pscript[(x)+2]<<16)+((uint32_t)(uint8_t)pani->pscript[(x)+3]<<24)))\n",
        "typedef int (*PANIF)(obj_3d*,anidata*); static PANIF ani_functions[256];\n",
        "static void w32(char *p,uintptr_t v){if(v>INT32_MAX)abort();uint32_t n=(uint32_t)v;memcpy(p,&n,4);}\n",
        "static uint32_t bits(float v){uint32_t n;memcpy(&n,&v,4);return n;}\n",
        "\n".join("#define " + key + " " + value for key, value in defines.items()),
        "static float trigs[257]={" + ",".join(number(x) for x in data["quarterSine"]) + "};\n",
    ]
    for text in body:
        declarations.append(text[:text.index("{")].strip() + ";")
    declarations.extend(body)
    declarations.append("static int source_flags[]={" + ",".join(str(clip["flags"]) for clip in data["clips"]) + "};")
    declarations.append(r'''
/* The fixture has decoded float frame buffers, not shipped delta-compressed SEQ
   bytes. Preserve the source compressed SKIP traversal through the unchanged
   _next_frame; only its pose-storage representation is already decoded. */
static int decoded_skip(obj_3d *pobj,anidata *pani){
 if(!(source_flags[pani->seq_index]&AH_COMPRESSED))return _ani_skipframes(pobj,pani);
 int count=(int)pani->pscript[0];pani->pscript++;
 if(count==AA_RAND){int arg=(int)pani->pscript[0],base=(int)pani->pscript[1];pani->pscript+=2;count=base+randrng(arg);}
 while(count--)_next_frame(pobj,pani,0);
 return 0;
}
''')
    for name, buf in arrays.items():
        declarations.append("static char script_" + name + "[]={" + ",".join(str(x if x < 128 else x - 256) for x in buf) + "};")
    init = []
    for index, clip in enumerate(data["clips"]):
        roots = [x for frame in clip["frames"] for x in frame["root"]]
        quats = [x for frame in clip["frames"] for joint in frame["pose"] for x in joint]
        declarations.append("static float roots_" + str(index) + "[]={" + ",".join(map(number, roots)) + "};")
        declarations.append("static float quats_" + str(index) + "[]={" + ",".join(map(number, quats)) + "};")
    size = 4 + len(clip_names) * 16 + sum(len(clip["frames"]) * 63 * 4 for clip in data["clips"])
    init.append("anim_blocks[0]=calloc(1," + str(size) + ");anihdr *headers=(anihdr*)(anim_blocks[0]+4);int offset=" + str(len(clip_names) * 16) + ";")
    for index, clip in enumerate(data["clips"]):
        count = len(clip["frames"])
        init.append(f'headers[{index}]=(anihdr){{{count},15,offset,0}};seq_names[{index}]="{clip["name"]}";'
                    f'memcpy((char*)headers+offset,roots_{index},{count*12});'
                    f'memcpy((char*)headers+offset+{count*12},quats_{index},{count*240});offset+={count*252};')
    for name, position, target in fixups:
        value = "(uintptr_t)&is_inplay" if target == "is_inplay" else "(uintptr_t)&unsupported" if target == "unsupported" else \
            "(uintptr_t)(script_" + labels[target][0] + "+" + str(labels[target][1]) + ")"
        init.append(f"w32(script_{name}+{position},{value});")
    included = {re.search(r"\b(\w+)\s*\(", text)[1] for text in body}
    for index, function in enumerate(functions):
        if function in included and function.startswith("_ani_"):
            init.append(f"ani_functions[{index}]={function};")
    init.append("ani_functions[3]=decoded_skip;")
    declarations.append(r'''
static void row(int c,int a,obj_3d *o){
 printf("{\"case\":%d,\"action\":%d,\"randomCalls\":%d,\"root\":[%u,%u,%u],\"position\":[%u,%u,%u],\"layers\":[",
 c,a,random_calls,bits(o->ax),bits(o->ay),bits(o->az),bits(o->x),bits(o->y),bits(o->z));
 for(int k=0;k<2;k++){anidata *p=o->adata+k;if(k)printf(",");
 printf("{\"sequence\":\"%s\",\"mode\":%d,\"iframe\":%d,\"hold\":%d,\"fframe\":%u,\"icnt\":%d,\"scnt\":%d,\"destTicks\":%d,\"pose\":[",
 seq_names[p->seq_index],p->animode,p->iframe,p->anicnt,bits(p->fframe),p->icnt,p->scnt,(p+2)->icnt);
 for(int i=0;i<60;i++)printf("%s%u",i?",":"",bits(p->pq?p->pq[i]:0));printf("]}");}printf("]}\n");}
int main(void){
''')
    declarations.extend(init)
    for case_index, case in enumerate(data["cases"]):
        declarations.append(f"{{obj_3d o={{0}};o.x=4;o.z=-7;o.y=1.5f;o.ascale={number(case['scale'])};o.fwd={case['heading']};o.tgt_fwd=o.fwd;o.fwd2=o.fwd;o.tgt_fwd2=o.fwd;raw_random={case['randomValue']};random_calls=0;game_info.game_mode=0;"
                            "for(int i=0;i<4;i++){o.adata[i].adata=calloc(60,4);o.adata[i].idata=calloc(60,4);o.adata[i].vcount=4;o.adata[i].vframe=-1;}o.adata[1].animode=MODE_SECONDARY;")
        for action_index, action in enumerate(case["actions"]):
            kind = action["kind"]
            if kind == "advance": code = "animate_model(&o);"
            elif kind == "begin-lower":
                code = "float *a=o.adata[1].adata,*b=o.adata[1].idata;o.adata[1]=o.adata[0];o.adata[1].adata=a;o.adata[1].idata=b;o.adata[1].animode|=MODE_SECONDARY|MODE_TWOPART;"
                code += f"o.adata[0].vcount={action['vcount']};o.adata[0].vframe=-1;change_anim1(&o,script_{action['script']}+1);"
            elif kind == "upper":
                code = f"o.adata[1].vcount={action['vcount']};o.adata[1].vframe=-1;o.adata[1].hackval={action['turn']};o.adata[1].animode|=MODE_TURNHACK;change_anim2(&o,script_{action['script']}+1);"
            elif kind == "lower":
                code = f"o.adata[0].vcount={action['vcount']};o.adata[0].vframe=8;change_anim1(&o,script_{action['script']}+1);"
            else:
                if action["script"] == "qb_t_run_anim":
                    code = f"o.adata[0].vframe=-1;o.adata[0].vcount=4;change_anim(&o,script_{action['script']}+1);"
                else: code = f"change_anim(&o,script_{action['script']}+1);"
            declarations.append(code + f"row({case_index},{action_index},&o);")
        declarations.append("}")
    declarations.append("return 0;}")
    cpp = output / "original_vm.c"; cpp.write_text("\n".join(declarations))
    command = ["gcc", "-std=c11", "-O0", "-ffp-contract=off", "-fno-fast-math", "-fno-strict-aliasing",
               "-fsigned-char", "-no-pie", "-Wno-int-to-pointer-cast", str(cpp), "-lm", "-o", str(output / "original_vm")]
    compiled = subprocess.run(command, text=True, capture_output=True)
    (output / "compile.log").write_text(compiled.stdout + compiled.stderr)
    selected._require(compiled.returncode == 0, "Original VM compilation failed: " + compiled.stderr[-6000:])
    execution = subprocess.run([str(output / "original_vm")], text=True, capture_output=True, timeout=30)
    (output / "oracle.jsonl").write_text(execution.stdout)
    selected._require(execution.returncode == 0, "Original VM execution failed: " + execution.stderr[-2000:])
    def bit(value): return struct.unpack("<I", struct.pack("<f", value))[0]
    failures, assertions = [], 0
    for line in execution.stdout.splitlines():
        actual = json.loads(line); case = data["cases"][actual["case"]]; observed = case["actions"][actual["action"]]
        def compare(field, expected, value):
            nonlocal assertions
            assertions += 1
            if expected != value: failures.append(dict(case=case["name"], tick=observed["tick"],
                                                       field=field, original=expected, production=value))
        compare("randomCalls", actual["randomCalls"], observed["randomCalls"])
        for name in ("root", "position"):
            for axis, (expected, value) in enumerate(zip(actual[name], observed[name])):
                compare(name + str(axis), expected, bit(value))
        for index, name in enumerate(("primary", "secondary")):
            value = observed[name]
            if value is None: continue
            expected = actual["layers"][index]; interpolating = bool(value["mode"] & 0x00080000)
            compare(name + ".sequence", expected["sequence"], value["sequence"])
            compare(name + ".mode", expected["mode"], value["mode"])
            compare(name + ".hold", expected["hold"], value["hold"])
            compare(name + ".ordinal", expected["iframe"], value["sourceOrdinal"] if interpolating else value["ordinal"])
            if value["mode"] & 8:
                compare(name + ".fframe", expected["fframe"], bit(value["floatingFrame"]))
                compare(name + ".streamCount", expected["icnt"], value["streamCount"])
                compare(name + ".streamTicks", expected["scnt"], value["streamTicks"])
            if interpolating:
                compare(name + ".transitionCount", expected["icnt"], value["transitionCount"])
                compare(name + ".transitionTicks", expected["destTicks"], value["transitionTicks"])
            for component, (a, b) in enumerate(zip(expected["pose"], (x for q in value["pose"] for x in q))):
                compare(name + ".pose" + str(component), a, bit(b))
    result = dict(schema="unchanged-original-animation-vm-comparison/v1", assertions=assertions,
                  cases=len(data["cases"]), failures=failures, sourcePins=pins, compilerCommand=command,
                  originalFunctions=sorted(included), dataManifestSha256=data["manifestSha256"],
                  boundary="Genuine admitted selected WAN float poses/roots materialized in decoded float memory; source compressed SKIP traversal calls unchanged _next_frame through an explicit decoded-storage adapter. All five requested routines are unchanged. No shipped SEQ numeric IDs/compression quantization/native binary identity claimed.")
    (output / "result.json").write_text(json.dumps(result, indent=2))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--base", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--runtime-inventory", type=Path)
    parser.add_argument("--coverage", type=Path)
    parser.add_argument("--av-supplement", action="store_true")
    parser.add_argument("--tree-pins", type=Path)
    parser.add_argument("--materialize-av", action="store_true")
    parser.add_argument("--av-delta", type=Path)
    parser.add_argument("--team-audio", type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--vm-observations", type=Path)
    args = parser.parse_args()
    if args.vm_observations:
        if args.output is None: parser.error("Original VM comparison requires a create-new --output.")
        result = compare_vm_observations(args.source, args.vm_observations, args.output)
        print(json.dumps({key: value for key, value in result.items() if key != "failures"}, sort_keys=True))
        print(json.dumps(dict(failureCount=len(result["failures"]), firstFailures=result["failures"][:15]), sort_keys=True))
        if result["failures"]: raise SystemExit(1)
        return
    if args.runtime_inventory:
        runtime = json.loads(args.runtime_inventory.read_bytes())
        coverage = None if args.coverage is None else json.loads(args.coverage.read_bytes())
        print(json.dumps(reachable_inventory(args.source, runtime, coverage), indent=2, sort_keys=True))
        return
    if args.base is None or args.output is None:
        parser.error("Preparation requires --base and a create-new --output.")
    if args.materialize_av:
        if args.av_delta is None or args.team_audio is None or args.manifest is None:
            parser.error("AV materialization requires --av-delta, --team-audio and the exact --manifest.")
        print(json.dumps(materialize_av306(args.base, args.av_delta, args.team_audio,
                                           args.manifest.read_bytes(), args.output), indent=2, sort_keys=True))
        return
    if args.av_supplement:
        if args.tree_pins is None:
            parser.error("AV preparation requires the actual pinned --tree-pins metadata.")
        print(json.dumps(prepare_av_supplement(args.source, args.base, args.output,
                                              json.loads(args.tree_pins.read_bytes())), indent=2, sort_keys=True))
        return
    print(json.dumps(prepare(args.source, args.base, args.output), indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
