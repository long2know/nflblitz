#!/usr/bin/env python3
"""All current runtime/separate Editor sources against actual Unity APIs; never launch Unity."""
import importlib.util
from pathlib import Path
import argparse
import sys

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("current_C2_Unity_API", ROOT / "tests/presentation/movement-stage/api_compile.py")
compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compiler)
if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent / ".artifacts/contact281-m1-api")
    args, rest = parser.parse_known_args()
    output = args.output.resolve()
    if not output.is_relative_to(Path(__file__).resolve().parent / ".artifacts") or output.exists():
        raise SystemExit("Choose a NEW ignored C2-suite API output; never overwrite prior evidence.")
    compiler.ARTIFACTS = output
    sys.argv = [sys.argv[0], *rest]
    raise SystemExit(compiler.main())
