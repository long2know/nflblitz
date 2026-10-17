#!/usr/bin/env python3
"""Reproduce canonical C2 data with the existing producer and exact authored original-C oracles."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import canonical_hashes

ORIGINAL_COUNTS = {"callbacks": 2146, "speech": 8000, "banks": 16, "whistles": 16,
                   "player_down": 15, "end_epochs": 3, "immediate_whistles": 6}


def prepare_original_oracles(source_root, process_root, output):
    output = Path(output).absolute()
    if (not output.is_relative_to(ROOT) or output.is_symlink()
            or not (output.is_relative_to(HERE / ".artifacts") or output.is_relative_to(ROOT / ".squad/.scratch"))):
        raise ValueError("Original C2 generated bodies/data must remain in explicit ignored local storage.")
    commands = [
        ["python3", "-B", str(HERE / "c2_oracle.py"), "--original-root", str(Path(source_root).absolute()),
         "--process-root", str(Path(process_root).absolute()), "--output", str(output)],
        ["python3", "-B", str(HERE / "c2_negative_target_oracle.py"), "--original-root", str(Path(source_root).absolute()),
         "--oracle-root", str(output), "--output", str(output / "negative-target")],
    ]
    logs = []
    for index, command in enumerate(commands):
        process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180,
                                 env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"))
        output.mkdir(parents=True, exist_ok=True)
        log = output / ("prepare-" + str(index) + ".log")
        log.write_text(process.stdout + process.stderr)
        logs.append(hashlib.sha256(log.read_bytes()).hexdigest())
        if process.returncode:
            raise RuntimeError("Tracked original-C C2 preparation failed; no cached cases or synthetic fallback.")
    receipt = json.loads((output / "receipt.json").read_bytes())
    if (receipt.get("schema") != "source-contact-c2-original-C/v1"
            or receipt.get("proper_original_void_functions_no_undefined_scalar_returns") is not True
            or receipt.get("case_counts") != ORIGINAL_COUNTS or receipt.get("source_programs") != 9
            or receipt.get("exit_code") != 0):
        raise RuntimeError("Original C2 native case/program/source semantics inventory differs.")
    for name, field in (("oracle.c", "oracle_c_sha256"), ("cases.json", "cases_sha256"),
                        ("source-programs.json", "programs_sha256")):
        if hashlib.sha256((output / name).read_bytes()).hexdigest() != receipt.get(field):
            raise RuntimeError("Original C2 detached output hash differs: " + name)
    trap = json.loads((output / "negative-target/trap-receipt.json").read_bytes())
    if (trap.get("bearingObservedMe") != 4 or trap.get("bearingObservedHim") != -1
            or trap.get("stoppedBeforeAnyBearingRead") is not True
            or trap.get("postBearingNoncandidateCases") != 2548
            or trap.get("postBearingChanceDraws") != 0 or trap.get("postBearingAnimationChanges") != 0
            or trap.get("postBearingLatchWrites") != 0
            or trap.get("shippedTargetMemoryTrapOrABIOutcomeQualified") is not False
            or trap.get("portabilityDecisionExplicitUserApproved") is not False):
        raise RuntimeError("Defined negative-target observation cannot become an unsafe target-load/ABI golden.")
    return commands, {"case_counts": receipt["case_counts"], "programs": 9, "defined_tail_cases": 2548,
                      "receipt_sha256": hashlib.sha256((output / "receipt.json").read_bytes()).hexdigest(),
                      "trap_receipt_sha256": hashlib.sha256((output / "negative-target/trap-receipt.json").read_bytes()).hexdigest(),
                      "prepare_logs_sha256": logs}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--process-root", type=Path, required=True)
    parser.add_argument("--base272", type=Path, required=True)
    parser.add_argument("--extra9", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if output.exists() or not output.is_relative_to(ROOT / ".squad/.scratch") or output == ROOT / ".squad/.scratch":
        parser.error("The existing producer requires a new ignored .squad/.scratch child.")
    output.mkdir(parents=True)
    command = ["python3", "-B", str(ROOT / "tools/animation/prepare_c2_profile.py"),
               "--source-root", str(args.source_root.absolute()), "--base-bundle", str(args.base272.absolute()),
               "--extra-bundle", str(args.extra9.absolute()), "--destination", str(output / "inputs")]
    process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180)
    (output / "producer.log").write_text(process.stdout + process.stderr)
    if process.returncode:
        raise RuntimeError("Existing canonical C2 producer failed; no duplicate encoder or invented input schema.")
    inputs = canonical_hashes(output / "inputs")
    commands, original = prepare_original_oracles(args.source_root, args.process_root, output / "native")
    receipt = {"schema": "source-canonical281-QA-preparation/v1", "status": "passed",
               "commands": [command, *commands], "canonical288_sha256": inputs, "original_C": original,
               "no_ignored_script_dependencies": True, "source_data_profile_pins_changed": False,
               "full_M1_game_native_or_DSP_qualified": False,
               "portability_policy": "PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE; autonomous, NOT user-approved, NOT target-memory/ABI parity"}
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("PASS canonical288 and exact original C2 oracle preparation")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
