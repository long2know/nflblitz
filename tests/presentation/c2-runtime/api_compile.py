#!/usr/bin/env python3
"""All current runtime/separate Editor sources against actual Unity APIs; never launch Unity."""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("current_C2_Unity_API", ROOT / "tests/presentation/movement-stage/api_compile.py")
compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compiler)
compiler.ARTIFACTS = Path(__file__).resolve().parent / ".artifacts/api"

if __name__ == "__main__":
    raise SystemExit(compiler.main())
