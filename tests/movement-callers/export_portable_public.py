#!/usr/bin/env python3
"""Export one saved positive Portable case; never run or regenerate gameplay."""
import argparse
import copy
import gzip
import hashlib
import io
import json
import math
from pathlib import Path
import re
import sys

import prepare_public_replay as public


ROOT = Path(__file__).resolve().parents[2]
CASE = "portable-cabinet-wide-pass-clearance-12000"
CONTROLLER = "tests/movement-callers/PortablePublicPolicy.cs"
AUTHOR_EXPORTER = "tests/movement-callers/export_portable_public.py"
AUTHOR_EXPORTER_SHA256 = "c876727da203e794859761ec7d2d92b235cf478648315fa84bf8c6a5e5e79476"
CONFIGURATION = ("OriginalRelease57Hz,60Hz ordinaryhost bridge; TwoStation, team0/team1 roster0..6; "
                 "station0Home; first possessionAway; captainFirst; presentationHome/head-1x4/pup0; "
                 "neutral SourceMovementSettings; actual real281 fullgeometry/pivots and oldSAFN beforewake; seed1")
HANDICAP = ("UNCHANGED new SourceHandicapSettings() defaults: adjustment0/winstreak0/randomDronesfalse/"
            "allteamMask0/NoAssistfalse/SuperDronesfalse; approved Portable last-valid consumers only, "
            "raw values kept; not neutral5/5 cache fixture")
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import production_paths


def sha(data):
    return hashlib.sha256(data).hexdigest()


def source_semantics(value):
    if not value["IsSpecified"]:
        return
    public.require(-1 <= value["TargetPlayerId"] <= 13, "specified source target must be-1..13")
    public.require(0 <= value["PatChoice"] <= 2, "specified source PAT must be0..2")
    public.require(value["KickoffChoice"] is None or 0 <= value["KickoffChoice"] <= 2,
                   "specified source kickoff must be0..2/null")
    public.require(value["AudibleSlot"] >= -1, "specified source audible must be>=-1")
    play = value["Play"]
    if play["IsSpecified"]:
        public.require(type(play["PlaySymbol"]) is str and bool(play["PlaySymbol"].strip()),
                       "specified play requires a nonblank symbol")
        public.require(0 <= play["Playbook"] <= 1, "specified playbook must be0..1")
        for name in ("SlotIndex", "PageIndex", "PoolIndex"):
            public.require(play[name] >= -1, "specified play " + name + " must be>=-1")
        public.require(play["Pool"] is None or 0 <= play["Pool"] <= 12,
                       "specified play pool must be0..12/null")
        public.require((play["Pool"] is not None) == (play["PoolIndex"] >= 0),
                       "specified play pool and ordinal must be supplied together")


def project_row(row, host):
    result = copy.deepcopy(row)
    public.fields(result, {"host", "command"}, "row")
    public.fields(result["command"], public.COMMAND_FIELDS, "command")
    frame = result["command"]["StationInputs"]
    if frame is not None:
        public.fields(frame, {"Stations"}, "command.StationInputs")
        public.require(type(frame["Stations"]) is list, "public Stations must be a list")
        for station in frame["Stations"]:
            public.fields(station, public.STATION_FIELDS | {"IsSpecified"}, "captured station")
            public.require(station["IsSpecified"] is True,
                           "captured station must have constructor-derived IsSpecified=true")
            del station["IsSpecified"]
    public.public_row(result, host)
    source_semantics(result["command"]["Source"])
    if frame is not None:
        for station in frame["Stations"]:
            source_semantics(station["Command"])
    return result


def project_capture(data):
    public.require(len(data) <= 32 * 1024 * 1024 and data.endswith(b"\n"),
                   "bounded captured JSONL must end in LF")
    rows = [public.decode(line) for line in data.decode("ascii").splitlines()]
    public.require(len(rows) == 12000, "positive export requires all12000 ordinary hosts")
    return public.encode([project_row(row, host) for host, row in enumerate(rows)])


def require_candidate(receipt, poses):
    public.require(receipt.get("failure") is None and receipt.get("completedHosts") == 12000
                   and receipt.get("SourceTick") == 11400
                   and receipt.get("gameplayCompatibility") == "Portable"
                   and receipt.get("factory") == "REAL SourceC2Composition.CreateM1Simulation"
                   and receipt.get("profile") == "original-teams-contact281-c2-m1"
                   and receipt.get("sourceData") ==
                   "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
                   "export requires entire fault-free actual Portable M1 first window")
    positive = receipt.get("positive")
    public.require(type(positive) is dict, "no genuine public R_SKIP witness")
    public.require(positive["secondTick"] > positive["firstTick"] > 2299
                   and positive["firstOrdinal"] != positive["secondOrdinal"],
                   "unchanged after2299/same-instance/two-ordinal predicate required")
    selected = []
    for tick, ordinal in ((positive["firstTick"], positive["firstOrdinal"]),
                          (positive["secondTick"], positive["secondOrdinal"])):
        matches = [p for p in poses if p["actor"] == positive["actor"]
                   and p["InstanceId"] == positive["InstanceId"]
                   and p["SourceTick"] == tick and p["ordinal"] == ordinal]
        public.require(len(matches) == 1, "exact retained actual pose for each witness tick required")
        pose = matches[0]
        primary = pose["view"]["Animation"]["Primary"]
        public.require(primary["IsSpecified"] and primary["ScriptSymbol"] == "r_skip_anim"
                       and primary["InstanceId"] == positive["InstanceId"]
                       and primary["Sample"]["SequenceName"] == "R_SKIP"
                       and pose["view"]["Animation"]["SourceTick"] == tick,
                       "retained actual primary R_SKIP instance/tick required")
        for field in ("real15Joints", "real15LocalPose"):
            public.require(len(pose[field]) == 15 and all(
                math.isfinite(j[axis]) for j in pose[field] for axis in ("X", "Y", "Z")),
                "all15 actual finite cached/local joints required")
        selected.append(pose)
    public.require(selected[0]["real15LocalPose"] != selected[1]["real15LocalPose"],
                   "two actual distinct15-bone local poses required")
    return positive


def text(value, where):
    public.require(type(value) is str and 0 < len(value) <= 2048
                   and not value.lstrip().startswith(("{", "["))
                   and all(ord(c) >= 32 and ord(c) < 127 for c in value),
                   where + " must be scalar public text, not structured/private payload")
    return value


def digest(value, where):
    public.require(type(value) is str and re.fullmatch(r"[0-9a-f]{64}", value) is not None,
                   where + " must be a SHA256 string")
    return value


def manifest(value):
    return sha(json.dumps(value, sort_keys=True, separators=(",", ":")).encode())


def validate_policy(raw, receipt, producer_source=None):
    public.require(sha(raw) == digest(receipt["policySha256"], "receipt.policySha256"),
                   "policy bytes differ from retained receipt.policySha256")
    policy = public.decode(raw)
    for name in ("name", "version", "sourceHandicapPolicy", "sameApprovedConfiguration",
                 "actualFactory", "compatibility"):
        text(policy[name], "policy." + name)
    controller = (ROOT / CONTROLLER).read_bytes()
    controller_sha = sha(controller)
    public.require(digest(policy["harnessSha256"][CONTROLLER], "controllerSourceSha256") == controller_sha
                   and policy["frozenCodeSha256"][CONTROLLER] == controller_sha,
                   "controller source differs from retained policy evidence")
    for path in ("tests/movement-callers/Canonical.cs", "tests/movement-callers/Program.cs",
                 "tests/parity/FullKernelInputs.cs"):
        actual = sha((ROOT / path).read_bytes())
        public.require(digest(policy["harnessSha256"][path], "retained factory harness SHA256") == actual
                       and policy["frozenCodeSha256"][path] == actual,
                       "retained factory harness source differs: " + path)
    source = controller.decode("ascii")
    for name, symbol in (("name", "PortablePolicyName"), ("version", "PortablePolicyVersion")):
        found = re.findall(r'private const string ' + symbol + r' = "([^"]+)";', source)
        public.require(len(found) == 1 and policy[name] == found[0] == receipt[name],
                       "policy." + name + " differs from actual controller/receipt")
    public.require(policy["name"] + "-12000" == CASE and policy["actualFactory"] ==
                   receipt["factory"].removeprefix("REAL ") == "SourceC2Composition.CreateM1Simulation",
                   "policy factory/case differs from retained actual factory")
    public.require(policy["sameApprovedConfiguration"] == CONFIGURATION
                   and policy["sourceHandicapPolicy"] == HANDICAP
                   and policy["compatibility"] == "Portable explicitly; StrictSource defaults unchanged"
                   and receipt["gameplayCompatibility"] == "Portable",
                   "policy configuration/compatibility differs from retained authoring contract")
    random = policy["random"]
    profile = text(random["profile"], "policy.random.profile")
    actual_profile = re.findall(r'new SourceRandomProvenance\("([^"]+)"', source)
    vector = re.findall(r'int\[\] rawVector = \{([^}]+)\}', source, re.S)
    public.require(len(actual_profile) == len(vector) == 1 and profile == actual_profile[0],
                   "provider profile differs from actual controller source")
    raw_vector = [int(part.strip()) for part in vector[0].split(",")]
    public.require(type(random["rawVector"]) is list
                   and all(type(v) is int for v in random["rawVector"])
                   and type(receipt["rawVector"]) is list
                   and all(type(v) is int for v in receipt["rawVector"])
                   and random["rawVector"] == receipt["rawVector"] == raw_vector
                   and type(random["seed"]) is int and random["seed"] == 1
                   and random["globalUnfilteredRepeatedVectorAtEverySourceDraw"] is True
                   and random["targetPRNGClaim"] is False and random["evidence"] == "ExternalUnverified",
                   "provider/vector/seed evidence differs from retained source and receipt")
    compiled = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths(ROOT)}
    public.require(manifest(compiled) == digest(policy["pureCompile67ManifestSha256"], "source67ManifestSha256")
                   and all(policy["frozenCodeSha256"].get(p) == pin for p, pin in compiled.items()),
                   "pure compilation sources differ from retained policy")
    inputs = policy["validated1122InputSha256"]
    public.require(type(inputs) is dict and len(inputs) == 1122
                   and manifest(inputs) == digest(policy["validated1122InputManifestSha256"],
                                                  "runtimeInput1122ManifestSha256"),
                   "runtime input manifest differs from retained policy")
    origin = Path(text(policy["worktree"], "policy.worktree"))
    public.require(origin.is_absolute() and ".." not in origin.parts, "policy worktree must be an absolute retained root")
    for path, pin in inputs.items():
        public.require(type(path) is str, "retained input path must be text")
        path = Path(path)
        public.require(path.is_absolute() and ".." not in path.parts
                       and path.is_relative_to(origin / ".squad/.scratch")
                       and not path.is_symlink() and not path.name.startswith(".env")
                       and path.suffix not in (".ulf", ".pem", ".key"),
                       "retained input path must be an ordinary ignored data artifact")
        public.require(sha(path.read_bytes()) == digest(pin, "retained input SHA256"),
                       "retained input bytes differ from policy manifest")
    for name, suffix in (("M1CallbackDataSha256", "/c2-callback-data.json"),
                         ("animationSha256", "/animation281/export-inventory.json"),
                         ("oldSAFNSha256", "/audio-functions/sound-functions.safn")):
        pin = digest(policy[name], "policy." + name)
        matches = [path for path, value in inputs.items() if type(path) is str
                   and path.replace("\\", "/").endswith(suffix) and value == pin]
        public.require(bool(matches) and all(sha(Path(path).read_bytes()) == pin for path in matches),
                       "policy." + name + " differs from retained payload evidence")
    public.require(policy["M1CallbackDataSha256"] == receipt["sourceData"],
                   "callback data differs from retained receipt")
    public.require(digest(policy["oldCanonicalPublicCommandsSha256"], "canonical_a33_readerInputSha256")
                   == public.PUBLIC_SHA256 and digest(policy["oldCanonicalGzipSha256"], "canonical_a33_gzipSha256")
                   == public.FIXTURE_SHA256 and sha(public.FIXTURE.read_bytes()) == public.FIXTURE_SHA256,
                   "historical a33 identity differs")
    if producer_source is None:
        producer_source = origin / AUTHOR_EXPORTER
    public.require(Path(producer_source).name == "export_portable_public.py"
                   and not Path(producer_source).is_symlink(), "original producer must be retained regular source")
    producer_sha = sha(Path(producer_source).read_bytes())
    public.require(producer_sha == AUTHOR_EXPORTER_SHA256,
                   "retained original producer source evidence differs")
    return policy, producer_sha


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("capture", "receipt", "poses", "policy", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--producer-source", type=Path,
                        help="Retained original author exporter source; defaults to the frozen policy worktree")
    args = parser.parse_args(argv)
    output = public.output_path(args.output)
    public.require(not output.exists() and output.is_relative_to(ROOT / ".squad/.scratch"),
                   "export needs a NEW ignored local output; never overwrite a33 or prior proof")
    receipt = public.decode(args.receipt.read_bytes())
    policy_raw = args.policy.read_bytes()
    policy, producer_sha = validate_policy(policy_raw, receipt, args.producer_source)
    capture = args.capture.read_bytes()
    poses = public.decode(args.poses.read_bytes())
    require_candidate(receipt, poses)
    public.require(sha(capture) == receipt["publicInputSha256"], "capture identity differs")
    projected = project_capture(capture)
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode="wb", filename="", mtime=0, compresslevel=9) as stream:
        stream.write(projected)
    packed = buffer.getvalue()
    public.require(gzip.decompress(packed) == projected, "gzip must roundtrip exact projected inputs")
    jsonl = output / (CASE + ".jsonl")
    archive = output / (CASE + ".jsonl.gz")
    recipe = {
        "schema": "portable-source-public-input-case/v1", "caseName": CASE,
        "policy": policy["name"], "policyVersion": policy["version"],
        "commandsOnly": True, "hosts": 12000,
        "closedSchema": "UNCHANGED prepare_public_replay.public_row / ReadPublicReplay",
        "jsonl": {"file": jsonl.name, "sha256": sha(projected), "bytes": len(projected)},
        "gzip": {"file": archive.name, "sha256": sha(packed), "bytes": len(packed),
                 "recipe": "standard gzip; empty filename; mtime0; compressionlevel9"},
        "captureSha256": sha(capture),
        "projection": "Remove ONLY each station's constructor-derived IsSpecified=true, "
                      "then existing public.encode canonical key order/ASCII/LF. All other values unchanged.",
        "producerSourceSha256": producer_sha,
        "controllerSourceSha256": policy["harnessSha256"]["tests/movement-callers/PortablePublicPolicy.cs"],
        "frozenPolicySha256": sha(policy_raw),
        "sourceHandicapPolicy": policy["sourceHandicapPolicy"],
        "configuration": policy["sameApprovedConfiguration"],
        "compatibility": "Portable explicitly; not original-hardware/native identical",
        "factory": policy["actualFactory"],
        "source67ManifestSha256": policy["pureCompile67ManifestSha256"],
        "runtimeInput1122ManifestSha256": policy["validated1122InputManifestSha256"],
        "callbackDataSha256": policy["M1CallbackDataSha256"],
        "animationManifestSha256": policy["animationSha256"],
        "oldSafnSha256": policy["oldSAFNSha256"],
        "providerProfile": policy["random"]["profile"], "seed": 1,
        "providerAndUnfilteredGlobalVectorUnchanged": True, "targetPRNGClaim": False,
        "canonical_a33_readerInputSha256": policy["oldCanonicalPublicCommandsSha256"],
        "canonical_a33_gzipSha256": policy["oldCanonicalGzipSha256"],
        "canonicalFixtureAndCINotReplaced": True,
        "containsNoModelRNGOrPoseArrays": True,
        "fullM1CIOrNativeQualification": "NOT_RUN; Parent review and separate qualification lease required",
    }
    output.mkdir(parents=True)
    jsonl.write_bytes(projected)
    archive.write_bytes(packed)
    path = output / (CASE + ".recipe.json")
    path.write_text(json.dumps(recipe, indent=2) + "\n")
    print(json.dumps({"schema": "portable-public-input-export-result/v1", "status": "exported-not-full-qualified",
                      "jsonl_sha256": sha(projected), "gzip_sha256": sha(packed),
                      "capture_sha256": sha(capture), "recipe_sha256": sha(path.read_bytes()),
                      "validator_source_sha256": sha(Path(__file__).read_bytes()),
                      "gameplayExecutions": 0}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
