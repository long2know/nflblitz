#!/usr/bin/env python3
"""Copy explicitly pinned, already prepared private inputs; never fetch or convert artwork."""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
SOURCE = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
PINS = {
    "rosters": "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b",
    "model": "9d91e22e0f80dc369a52c8956c6fb3f7b37504a3b266a8cba4f5125d93741149",
    "skeleton": "a957d80a322948d7a41667cf80ab621a2ff7b5e74eee6d4da8fba31f216c0a93",
    "playbook": "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3",
    "movement": "89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6",
    "sound": "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280",
    "animations": "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f",
    "callbacks": "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
    "hit": "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904",
    "impact": "2fee43a7a20dad06d8846b1d558a69d8648041368091ab12677c941e19b66cd1",
    "hud": "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def portable(value):
    require(isinstance(value, str) and 0 < len(value) <= 256 and "\\" not in value,
            "A bounded portable relative asset path is required.")
    path = PurePosixPath(value)
    require(not path.is_absolute() and all(p not in ("", ".", "..") for p in value.split("/"))
            and ":" not in value, "Unsafe asset path: " + value)
    return value


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(root, relative, expected, limit=64 * 1024 * 1024):
    path = Path(os.path.abspath(root / portable(relative)))
    require(path.resolve() == path.absolute() and path.is_file(),
            "Missing/symlinked required original asset: " + str(path))
    require(0 < path.stat().st_size <= limit, "Asset byte limit exceeded: " + relative)
    data = path.read_bytes()
    require(digest(data) == expected, "Original asset SHA256 mismatch: " + relative)
    return data


class Bundle:
    def __init__(self):
        self.files = {}

    def add(self, root, relative, expected, destination=None):
        destination = portable(destination or relative)
        data = read(root, relative, expected)
        require(destination not in self.files, "Duplicate destination: " + destination)
        self.files[destination] = data
        return data

    def document(self, root, relative, expected, destination=None):
        return json.loads(self.add(root, relative, expected, destination))

    def persist(self, output, entrypoints):
        output = output.absolute()
        require(output.resolve() == output and PROJECT in output.parents,
                "Private data output must be a real ignored directory inside this checkout.")
        probe = subprocess.run(["git", "check-ignore", "-q", str(output / "manifest.json")], cwd=PROJECT)
        require(probe.returncode == 0, "Original data output is not Git-ignored.")
        records = [{"path": name, "bytes": len(data), "sha256": digest(data)}
                   for name, data in sorted(self.files.items())]
        manifest = {
            "schema": "blitz-portable-source-player/v1",
            "runtimeFlavor": "portable C# browser preview (not a Unity player build)",
            "profile": "original-teams-contact281-c2-m1",
            "sourceRevision": SOURCE,
            "converterRevision": "1792293aaaea00a919b59e8c05716978e85b4333",
            "entrypoints": entrypoints,
            "files": records,
            "origins": {
                "world": "original field2/stadpie; authored TGA / original WMS level data",
                "players": "original generated limbs, original uniform atlases and BCD jersey geometry",
                "animation": "actual production281 library, including unchanged qualified R_SKIP hold/replay",
                "hud": "original authored atlas/font/draw composer; portable raster origin",
                "camera": "production source-derived normal model and actual source cinematic/MatInput stage",
                "audio": "Audio output unqualified; source selectors execute, no PCM/DSP playback",
            },
        }
        self.files["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode()
        for relative, data in sorted(self.files.items()):
            target = output / relative
            require(not target.is_symlink(), "Refusing symlink output: " + relative)
            if target.exists():
                require(target.is_file() and target.read_bytes() == data,
                        "Different private output already exists; choose a new ignored --output.")
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                require(target.parent.resolve() == target.parent, "Refusing symlink output parent.")
                target.write_bytes(data)
        for relative, data in self.files.items():
            require((output / relative).read_bytes() == data, "Private asset copy did not persist: " + relative)
        return manifest, digest(self.files["manifest.json"])


def prepare(args):
    kernel = args.kernel_fixture.absolute()
    fixture = json.loads(kernel.read_bytes())
    require(fixture["schema"] == "source-contact-kernel-real-inputs/v1" and
            fixture["source_revision"] == SOURCE, "Pinned real C1 source-input manifest required.")
    bundle = Bundle()
    entry = {}
    for key in ("rosters", "model", "skeleton", "playbook", "movement"):
        record = fixture[key]
        require(record["sha256"] == PINS[key], "Unexpected production input pin: " + key)
        dest = "kernel/" + record["path"]
        bundle.add(kernel.parent, record["path"], PINS[key], dest)
        entry[key] = dest
    sound = args.sound_functions.absolute() if args.sound_functions else kernel.parent / "audio-functions/sound-functions.safn"
    bundle.add(sound.parent, sound.name, PINS["sound"], "kernel/sound-functions.safn")
    entry["sound"] = "kernel/sound-functions.safn"
    m1 = args.m1_inputs.absolute()
    inventory = bundle.document(m1, "animation281/export-inventory.json", PINS["animations"],
                                "animation/export-inventory.json")
    require(len(inventory["clips"]) == 281 and
            sum(c["selected_frames"] for c in inventory["clips"]) == 26230,
            "The actual281/26230 animation generation is required.")
    for clip in inventory["clips"]:
        bundle.add(m1 / "animation281", clip["path"], clip["sha256"], "animation/" + clip["path"])
    entry["animations"] = "animation/export-inventory.json"
    bundle.add(m1, "c2-callback-data.json", PINS["callbacks"], "kernel/c2-callback-data.json")
    bundle.add(m1, "m-qbfite-hit-extension.safn", PINS["hit"], "kernel/m-qbfite-hit-extension.safn")
    entry["callbacks"], entry["hit"] = "kernel/c2-callback-data.json", "kernel/m-qbfite-hit-extension.safn"
    impact = args.m1_impact.absolute()
    bundle.add(impact.parent, impact.name, PINS["impact"], "kernel/impact-presentation.json")
    entry["impact"] = "kernel/impact-presentation.json"
    world = bundle.document(args.world.absolute(), "environment.json", args.world_sha256, "world/environment.json")
    require(world["sourceCommit"] == SOURCE and world["stableId"] == "source-field2-stadpie",
            "Original field2/stadpie world required, not a graybox.")
    for mesh in world["meshes"]:
        bundle.add(args.world.absolute(), mesh["meshPath"], mesh["meshSha256"], "world/" + mesh["meshPath"])
    for texture in world["textures"]:
        bundle.add(args.world.absolute(), texture["rgbaPath"], texture["rgbaSha256"], "world/" + texture["rgbaPath"])
    entry["world"] = "world/environment.json"
    teams = bundle.document(args.teams.absolute(), "team-presentation.json", args.teams_sha256,
                            "teams/team-presentation.json")
    require(teams["source_revision"] == SOURCE and teams["roster_sha256"] == PINS["rosters"],
            "Original31-team presentation catalog required.")
    require(not teams["missing_texture_ids"], "Original team catalog has missing texture identities.")
    for source in teams["inputs"]:
        if source["path"].startswith("models/"):
            bundle.add(args.teams.absolute(), source["path"], source["sha256"], "teams/" + source["path"])
    for texture in teams["textures"]:
        bundle.add(args.teams.absolute(), texture["path"], texture["sha256"], "teams/" + texture["path"])
    entry["teams"] = "teams/team-presentation.json"
    hud = bundle.document(args.hud.absolute(), "interface.json", PINS["hud"], "hud/interface.json")
    for texture in hud["textures"]:
        bundle.add(args.hud.absolute(), texture["path"], texture["sha256"], "hud/" + texture["path"])
    entry["hud"] = "hud/interface.json"
    for name, source in (("settings", args.settings), ("bootstrap", args.bootstrap)):
        source = source.absolute()
        target = name + ".json"
        bundle.add(source.parent, source.name, digest(source.read_bytes()), target)
        entry[name] = target
    manifest, manifest_hash = bundle.persist(args.output, entry)
    print(json.dumps({"schema": manifest["schema"], "manifestSha256": manifest_hash,
                      "files": len(manifest["files"]), "bytes": sum(f["bytes"] for f in manifest["files"]),
                      "profile": manifest["profile"], "output": str(args.output.absolute())}, indent=2))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kernel-fixture", type=Path, required=True)
    parser.add_argument("--sound-functions", type=Path)
    parser.add_argument("--m1-inputs", type=Path, required=True)
    parser.add_argument("--m1-impact", type=Path, required=True)
    parser.add_argument("--world", type=Path, required=True)
    parser.add_argument("--world-sha256", required=True)
    parser.add_argument("--teams", type=Path, required=True)
    parser.add_argument("--teams-sha256", required=True)
    parser.add_argument("--hud", type=Path, required=True)
    parser.add_argument("--settings", type=Path, default=HERE / "match-settings.json")
    parser.add_argument("--bootstrap", type=Path, default=HERE / "movement-settings.json")
    parser.add_argument("--output", type=Path, default=HERE / "data")
    args = parser.parse_args(argv)
    try:
        prepare(args)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, "Source Player data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
