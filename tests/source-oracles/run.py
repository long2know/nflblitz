#!/usr/bin/env python3
"""Generate local source-pinned numeric cases by running the actual original C."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

from cases import command, inputs
from oracle import Oracle, SUITE
from test_oracle import run_tests


def generate(source_root, full_matrix=False, compiler=None, output=None):
    artifacts = Path(output).absolute() if output is not None else SUITE / ".artifacts"
    if output is not None and (not artifacts.is_relative_to(SUITE / ".artifacts") or artifacts.exists()):
        raise ValueError("Use a new ignored original-C output child; preserve historical receipts.")
    artifacts.mkdir(parents=True, exist_ok=True)
    oracle = Oracle(source_root, artifacts=artifacts / "run", compiler=compiler)
    groups = run_tests(oracle)
    rows = inputs(full_matrix)
    if not rows or len({row["id"] for row in rows}) != len(rows):
        raise RuntimeError("Original C case inventory must be non-empty and uniquely identified.")
    stderr = []
    for start in range(0, len(rows), 4096):
        batch = rows[start:start + 4096]
        output = oracle.execute([command(row["kind"], row["input"]) for row in batch])
        stderr.append(oracle.last_stderr)
        for row, expected in zip(batch, output):
            row["expected"] = expected
    corpus = {
        "schema": "original-c-oracle-cases/v1",
        "manifest": oracle.manifest(),
        "inventory": dict(sorted(Counter(row["kind"] for row in rows).items())),
        "generation_order": "cases.py fixed iteration order; raw onside rand values explicitly prescribed and recorded; no implicit RNG; one fresh reset per row",
        "full_input_matrix": full_matrix,
        "cases": rows,
    }
    case_path = artifacts / "cases.json"
    case_path.write_text(json.dumps(corpus, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8")
    (artifacts / "original-stderr.log").write_text("".join(stderr), encoding="utf-8")
    evidence = {
        "schema": "original-c-oracle-run/v1", "status": "passed",
        "original_c_test_groups": groups, "skipped": 0, "executed_original_c_cases": len(rows),
        "case_counts": corpus["inventory"], "full_input_matrix": full_matrix,
        "cases_path": str(case_path.relative_to(SUITE.parents[1])),
        "cases_sha256": hashlib.sha256(case_path.read_bytes()).hexdigest(),
        "compiler": oracle.compiler_record,
        "source_blobs": corpus["manifest"]["source"]["blobs"],
        "production_csharp_compared": False, "browser_executed": False,
        "full_gameplay_parity_claimed": False,
    }
    (artifacts / "result.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {groups} original-C test groups; {len(rows)} executed numeric cases; {case_path}")
    return evidence


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--full-matrix", action="store_true")
    parser.add_argument("--compiler")
    parser.add_argument("--output", type=Path, help="New ignored child output; preserve historical original-C receipts.")
    args = parser.parse_args()
    generate(args.source_root, args.full_matrix, args.compiler, args.output)
