#!/usr/bin/env python3
"""Optionally obtain only the pinned public C/header blobs; never clone or overwrite a user cache."""
import argparse
import base64
import json
from pathlib import Path
import subprocess

from oracle import PINS, SUITE, OracleError, blob_sha1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, help="Optional existing user cache; mismatches are rejected, not overwritten.")
    args = parser.parse_args()
    destination = SUITE / ".artifacts" / "original"
    records = []
    for name, expected in PINS["blobs"].items():
        data = None
        record = {"path": name, "expected_git_blob_sha1": expected}
        candidates = [("existing-oracle-input", destination / name)]
        if args.cache is not None:
            candidates.append(("user-cache", args.cache / name))
        for label, path in candidates:
            if not path.is_file():
                continue
            candidate = path.read_bytes()
            actual = blob_sha1(candidate)
            if actual == expected:
                data = candidate
                record["obtained_from"] = label
                break
            record.setdefault("rejected_cache_bytes", []).append({
                "path": str(path), "actual_git_blob_sha1": actual,
            })
        if data is None:
            command = ["gh", "api", f"repos/{PINS['repository']}/git/blobs/{expected}"]
            fetched = subprocess.run(command, capture_output=True, text=True, check=True, timeout=60)
            blob = json.loads(fetched.stdout)
            if blob.get("encoding") != "base64" or blob.get("sha") != expected:
                raise OracleError(f"Unexpected Git blob API identity/encoding: {name}")
            data = base64.b64decode(blob["content"], validate=False)
            record["obtained_from"] = "GitHub Git blob API"
            record["command"] = command
        if blob_sha1(data) != expected:
            raise OracleError(f"Git blob API bytes failed hash verification: {name}")
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        record["verified_byte_length"] = len(data)
        records.append(record)
        print("VERIFIED", name, expected, len(data))
    receipt = {"schema": "original-c-input-receipt/v1", "source": PINS, "blobs": records}
    (destination.parent / "input-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
