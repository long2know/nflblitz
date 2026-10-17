#!/usr/bin/env python3
"""Reuse full current-checkout Unity runtime/Editor compiler, never execute an Editor."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("actual_current_Unity_API_compiler",
    ROOT / "tests/presentation/movement-stage/api_compile.py")
COMPILER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPILER)
COMPILER.ARTIFACTS = Path(__file__).resolve().parent / ".artifacts/api"

if __name__ == "__main__":
    raise SystemExit(COMPILER.main())
