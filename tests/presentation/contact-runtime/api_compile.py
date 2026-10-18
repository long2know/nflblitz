#!/usr/bin/env python3
"""Compile all actual current Unity runtime and separate Editor sources, without Unity execution."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("all_actual_Unity_API", ROOT / "tests/presentation/movement-stage/api_compile.py")
compiler = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(compiler)
compiler.ARTIFACTS = Path(__file__).resolve().parent / ".artifacts/api"

if __name__ == "__main__":
    raise SystemExit(compiler.main())
