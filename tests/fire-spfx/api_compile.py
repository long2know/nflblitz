#!/usr/bin/env python3
"""Actual Unity C#7.3/netstandard2.1 API check of every current runtime/Editor source."""
import argparse
import importlib.util
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("fire_actual_Unity_API", ROOT / "tests/presentation/movement-stage/api_compile.py")
compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compiler)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--output", type=Path, required=True)
    args, rest = parser.parse_known_args()
    output = args.output.resolve()
    if not output.is_relative_to(Path(__file__).parent / ".artifacts") or output.exists():
        raise SystemExit("Choose a new ignored fire-spfx API output directory.")
    compiler.ARTIFACTS = output
    sys.argv = [sys.argv[0], *rest]
    raise SystemExit(compiler.main())
