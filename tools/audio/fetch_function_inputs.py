#!/usr/bin/env python3
"""Explicitly acquire exactly four frozen original sound-function inputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import urllib.request
import uuid

HERE = Path(__file__).absolute().parent
PINS = HERE / "function-inputs.json"
ARTIFACTS = HERE / ".artifacts"
REPOSITORY = "historicalsource/nflblitz2000"
REVISION = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
TREE = "a7e0352125d46075b7d98e87253ab89b9160a4c7"
PINS_SHA256 = "af3de5c995179dff42ef786f317d5ee7ca7eaa044d341541101dfe088bb993e5"
NAMES = frozenset(("sounds.c", "include/sndcalls.h", "include/player.h", "plyrseq.c"))
MAX_BYTES = 1024 * 1024


class InputError(ValueError):
    pass


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def no_symlinks(path):
    path = Path(path)
    if ".." in path.parts:
        raise InputError("Traversal components are forbidden.")
    absolute = path.absolute()
    for part in (absolute, *absolute.parents):
        if part.is_symlink():
            raise InputError("Symlink input/output paths are forbidden: " + str(part))
    return absolute


def checked_destination(path):
    destination = no_symlinks(path)
    artifacts = no_symlinks(ARTIFACTS)
    if destination == artifacts or not destination.is_relative_to(artifacts):
        raise InputError("Destination must be an explicit descendant of this checkout's ignored tools/audio/.artifacts.")
    return destination


def validate_reference(config, repository, revision):
    if repository != REPOSITORY or config.get("repository") != REPOSITORY:
        raise InputError("Only the canonical original repository is permitted.")
    if revision != REVISION or config.get("commit") != REVISION or config.get("tree") != TREE:
        raise InputError("Only the canonical full commit/tree pins are permitted; no branch/ref fallback.")
    blobs = config.get("blobs")
    if not isinstance(blobs, dict) or set(blobs) != NAMES:
        raise InputError("Exactly the four canonical case-sensitive input paths are required.")
    for name, pin in blobs.items():
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or str(path) != name:
            raise InputError("Noncanonical original input path.")
        if not isinstance(pin, str) or not re.fullmatch("[0-9a-f]{40}", pin):
            raise InputError("Malformed original Git blob pin.")
    return dict(sorted(blobs.items()))


def input_pins(repository=REPOSITORY, revision=REVISION):
    raw = no_symlinks(PINS).read_bytes()
    if hashlib.sha256(raw).hexdigest() != PINS_SHA256:
        raise InputError("Frozen function-inputs.json identity changed; refusing retargeted pins.")
    return validate_reference(json.loads(raw), repository, revision)


def verify_bytes(data, expected, name):
    if not data or len(data) > MAX_BYTES or blob_sha1(data) != expected:
        raise InputError("Original byte/blob verification failed: " + name)
    return data


def read_cache(root, pins):
    root = no_symlinks(root)
    if not root.is_dir():
        raise InputError("Explicit input cache is not a directory: " + str(root))
    result = {}
    for name, pin in pins.items():
        current = root
        for part in PurePosixPath(name).parts:
            if not current.is_dir() or part not in os.listdir(current):
                raise InputError("Incomplete or wrong-case cache; all four pins are required: " + name)
            current = no_symlinks(current / part)
        if not stat.S_ISREG(current.stat().st_mode):
            raise InputError("Original cache entries must be regular files.")
        descriptor = os.open(current, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(descriptor, "rb") as stream:
            if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
                raise InputError("Original cache entries must be regular files.")
            data = stream.read(MAX_BYTES + 1)
        result[name] = verify_bytes(data, pin, name)
    return result


def download_bytes(name):
    # Same canonical blob verification as the parity/playbook fetchers, but
    # commit/path addressing avoids expanding their broader input/tree unions.
    url = f"https://raw.githubusercontent.com/{REPOSITORY}/{REVISION}/{name}"
    request = urllib.request.Request(url, headers={"User-Agent": "source-audio-function-inputs/1"})
    with urllib.request.urlopen(request, timeout=90) as response:
        if response.status != 200 or response.geturl() != url:
            raise InputError("Unexpected original HTTP identity/status; no redirect/ref fallback.")
        return response.read(MAX_BYTES + 1)


def publish(destination, data):
    if destination.exists():
        raise InputError("Destination appeared during acquisition; it is never overwritten.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    no_symlinks(destination.parent)
    incoming = destination.parent / (destination.name + ".incoming-" + uuid.uuid4().hex)
    incoming.mkdir()
    try:
        for name, raw in data.items():
            target = incoming / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as stream:
                stream.write(raw)
        no_symlinks(destination)
        if destination.exists():
            raise InputError("Destination appeared during acquisition; refusing overwrite.")
        incoming.rename(destination)
    finally:
        if incoming.exists():
            shutil.rmtree(incoming)


def acquire(destination, *, source_root=None, download=False, repository=REPOSITORY, revision=REVISION):
    if download == (source_root is not None):
        raise InputError("Choose exactly one explicit --download or --source-root; no automatic network.")
    pins = input_pins(repository, revision)
    destination = checked_destination(destination)
    # A selected source is always checked, even if a destination already exists.
    # Invalid/missing inputs never fall through to another cache or the network.
    data = read_cache(source_root, pins) if source_root is not None else None
    existed = destination.exists()
    if existed:
        existing = read_cache(destination, pins)
        if data is not None and data != existing:
            raise InputError("Explicit source and existing verified destination bytes disagree.")
        data = existing
    elif download:
        data = {name: verify_bytes(download_bytes(name), pin, name) for name, pin in pins.items()}
    if not existed:
        publish(destination, data)
    verified = read_cache(destination, pins)
    if verified != data:
        raise InputError("Published original bytes differ from verified acquisition.")
    return {
        "schema": "source-audio-function-input-acquisition/v1",
        "repository": REPOSITORY, "revision": REVISION, "tree": TREE,
        "pins_sha256": PINS_SHA256, "destination": str(destination),
        "mode": "download" if download else "source-root",
        "destination_state": "verified-existing" if existed else "created",
        "source_root": str(Path(source_root).absolute()) if source_root is not None else None,
        "verified_files": [
            {"path": name, "git_blob_sha1": pins[name], "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}
            for name, raw in verified.items()
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--download", action="store_true", help="Explicitly permit four pinned public HTTP reads.")
    mode.add_argument("--source-root", type=Path, help="Explicit read-only cache; all four exact inputs must exist.")
    parser.add_argument("--destination", type=Path, required=True,
                        help="Explicit new/fully verified cache under this checkout's tools/audio/.artifacts.")
    parser.add_argument("--repository", default=REPOSITORY)
    parser.add_argument("--revision", default=REVISION)
    args = parser.parse_args()
    try:
        result = acquire(args.destination, source_root=args.source_root, download=args.download,
                         repository=args.repository, revision=args.revision)
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except (InputError, OSError, ValueError) as error:
        print("SOURCE AUDIO FUNCTION INPUTS FAILED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
