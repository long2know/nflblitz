#!/usr/bin/env python3
"""Fetch only metadata-pinned public Git blobs, or verify explicit local caches."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
from support import ARTIFACTS, PINS, GateError, blob_sha1, verify_inputs


def fetch(caches):
    destination = ARTIFACTS / "original"
    records = []
    for name, expected in PINS["blobs"].items():
        data = None
        record = {"path": name, "git_blob_sha1": expected}
        for cache in [destination, *caches]:
            candidate = cache / name
            if not candidate.exists():
                continue
            if candidate.is_symlink() or not candidate.resolve().is_relative_to(cache.resolve()):
                raise GateError("Input cache path escapes its explicit root: " + name)
            data = candidate.read_bytes()
            if blob_sha1(data) != expected:
                raise GateError("Wrong cached bytes for " + name + "; refusing to repair, normalize or overwrite this input.")
            record["obtained_from"] = str(cache)
            break
        if data is None:
            command = ["gh", "api", f"repos/{PINS['repository']}/git/blobs/{expected}"]
            process = subprocess.run(command, capture_output=True, text=True, check=True, timeout=90)
            response = json.loads(process.stdout)
            if response.get("sha") != expected or response.get("encoding") != "base64":
                raise GateError("Unexpected public Git blob identity/encoding: " + name)
            data = base64.b64decode(response["content"], validate=False)
            if blob_sha1(data) != expected:
                raise GateError("Fetched public blob failed decoded-byte verification: " + name)
            record.update(obtained_from="GitHub Git blob API", command=command)
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        record["byte_length"] = len(data)
        records.append(record)
        print("VERIFIED", name, expected)
    verify_inputs(destination)
    (ARTIFACTS / "input-receipt.json").write_text(json.dumps({
        "schema": "source-playbook-input-receipt/v1", "status": "passed",
        "repository": PINS["repository"], "revision": PINS["revision"], "tree": PINS["tree"], "blobs": records,
    }, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, action="append", default=[],
                        help="Optional read-only canonical-path cache; repeat for multiple caches. Incorrect bytes fail.")
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    (ARTIFACTS / "input-receipt.json").unlink(missing_ok=True)
    try:
        fetch([path.resolve() for path in args.cache])
    except (OSError, ValueError, KeyError, GateError, subprocess.SubprocessError) as error:
        print("P1 INPUT FETCH FAILED: " + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
