#!/usr/bin/env python3
"""Export an unchanged gate's complete receipts through a caller-owned open archive descriptor."""

import hashlib
import json
import os
from pathlib import Path
import signal
import sys
import tarfile


ROOT = Path(__file__).resolve().parents[2]


def export(root, destination, native_files=()):
    root = root.resolve()
    if not root.is_dir() or not root.is_relative_to(ROOT / ".squad/.scratch"):
        raise ValueError("Only an explicit owned project scratch result may be exported.")
    digest = hashlib.sha256()

    class Writer:
        def write(self, data):
            written = destination.write(data)
            digest.update(data[:written])
            return written

    with tarfile.open(fileobj=Writer(), mode="w|gz", compresslevel=1) as archive:
        archive.add(root, arcname=root.name, recursive=True)
        for path in native_files:
            path = path.resolve()
            if not path.is_file() or not path.is_relative_to(ROOT / "tests/movement-callers/.artifacts"):
                raise ValueError("Unexpected original-C comparison artifact.")
            archive.add(path, arcname="original-C-post-VM/" + path.name, recursive=False)
    destination.flush()
    return digest.hexdigest()


def main(argv):
    root, code = Path(argv[0]), int(argv[1])
    try:
        result = json.loads((root / "full/result.json").read_bytes())
        native_files = []
        for argument in result.get("original_C_post_VM", {}).get("command", []):
            if argument.endswith("/actual-caller-physics.c"):
                native_root = Path(argument).parent
                native_files = [native_root / name for name in
                                ("actual-caller-physics.c", "actual-caller-physics", "compiler.log", "result.json")]
        with os.fdopen(os.dup(3), "wb") as destination:
            digest = export(root, destination, native_files)
            os.fsync(destination.fileno())
        print(json.dumps({"archiveSha256": digest, "actualExitCode": code,
                          "status": result.get("status"), "runs": list(result.get("runs", {})),
                          "completeControls": result.get("complete_compiled_control_closure_executed")},
                         indent=2), flush=True)
    except Exception as error:
        print("GATE_EXPORT_FAILED: " + repr(error) + "; preserved namespace PID=" + str(os.getpid()),
              flush=True)
        while True:
            signal.pause()
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
