#!/usr/bin/env python3
"""Obtain only hash-pinned public movement inputs; never overwrite another owner's cache."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
PINS = json.loads((SUITE / "pins.json").read_text())


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def fetch():
    destination = SUITE / ".artifacts/original"
    records = []
    for name, expected in PINS["blobs"].items():
        candidates = [
            destination / name,
            ROOT / "tests/source-oracles/.artifacts/original" / name,
            ROOT / ".squad/.scratch/reference/nflblitz2000" / name,
            ROOT / ".squad/.scratch/source-playbook/clean-source-layout" / name,
        ]
        if name == "include/handicap.h":
            candidates.append(ROOT / ".squad/.scratch/source-playbook-rng/handicap.h")
        data = None
        rejected = []
        for path in candidates:
            if not path.is_file():
                continue
            candidate = path.read_bytes()
            actual = blob_sha1(candidate)
            if actual == expected:
                data = candidate
                origin = path.relative_to(ROOT).as_posix()
                break
            rejected.append({"path": path.relative_to(ROOT).as_posix(), "observed_blob": actual})
        if data is None:
            command = ["gh", "api", "repos/" + PINS["repository"] + "/git/blobs/" + expected]
            process = subprocess.run(command, capture_output=True, text=True, check=True, timeout=60)
            response = json.loads(process.stdout)
            if response.get("sha") != expected or response.get("encoding") != "base64":
                raise RuntimeError("Unexpected public Git blob response: " + name)
            data = base64.b64decode(response["content"])
            origin = "GitHub Git blob API: " + expected
        if blob_sha1(data) != expected:
            raise RuntimeError("Pinned movement source bytes do not match: " + name)
        path = destination / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        records.append({"path": name, "blob": expected, "bytes": len(data), "origin": origin, "rejected": rejected})
    receipt = {"source": PINS, "blobs": records}
    (destination.parent / "input-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


if __name__ == "__main__":
    print(json.dumps(fetch(), indent=2))
