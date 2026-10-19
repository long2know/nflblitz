#!/usr/bin/env python3
"""Execute the real host/renderer C# with managed fault-injection collaborators; never run Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
HOST = "Assets/Scripts/Runtime/BlitzRuntime.cs"
RENDERER = "Assets/Scripts/Presentation/SourceFreeBallRenderer.cs"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def replace_once(source, before, after):
    require(source.count(before) == 1, "Mutation anchor must identify one real host statement: " + before)
    return source.replace(before, after, 1)


def controls(source):
    return [
        ("incomplete-restart-allowed",
         replace_once(source, "if (bridge == null || !initializationComplete)", "if (bridge == null)"),
         "failed-bootstrap/R retains visible SourceFailure before any restart work"),
        ("ready-before-required-resource",
         replace_once(source, "SourceFreeBallAsset freeBall = Resources.Load<SourceFreeBallAsset>",
                      "initializationComplete = true;\n                    SourceFreeBallAsset freeBall = Resources.Load<SourceFreeBallAsset>"),
         "failed-bootstrap/R retains visible SourceFailure before any restart work"),
        ("successful-bootstrap-never-ready",
         replace_once(source, "initializationComplete = true;", "initializationComplete = false;"),
         "ready source OnEnable permits activation"),
        ("focus-enable-bypass-failstop",
         source.replace("bridge.SetActive(initializationComplete && sourceFailure == null && !InputSuspended);",
                        "bridge.SetActive(!InputSuspended);"),
         "failed-bootstrap/focus-pause-enable never reactivates an incomplete bridge"),
        ("restart-does-not-rewind-bridge",
         replace_once(source, "bridge.Restart();", ";"),
         "ready source public R restarts"),
        ("ball-trail-capture-omitted",
         replace_once(source, "SourceFreeBallBinding.CaptureTrails(simulation)", "null"),
         "ready source presents its required ball and trail at startup"),
    ]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New directory below this suite's ignored .artifacts.")
    parser.add_argument("--source-root", type=Path, default=ROOT, help="Explicit production checkout/snapshot; defaults to this checkout.")
    parser.add_argument("--controls", action="store_true", help="Run baseline, compiled real-source mutants, then final baseline.")
    args = parser.parse_args()
    output = args.output.resolve()
    require(output.is_relative_to(HERE / ".artifacts") and not output.exists(), "Choose a new owned ignored output directory.")
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output),
               DOTNET_EnableDiagnostics="0", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    source_root = args.source_root.resolve(strict=True)
    inputs = {name: (source_root / name).read_bytes() for name in (HOST, RENDERER)}
    tests = {name: (HERE / name).read_bytes() for name in ("HostDoubles.cs", "Program.cs")}
    report = {
        "schema": "source-host-lifecycle-managed/v1", "status": "failed",
        "command": [sys.executable, *sys.argv], "language": "C#7.3",
        "production_input_root": str(source_root),
        "production_sources_sha256": {name: digest(raw) for name, raw in inputs.items()},
        "test_sources_sha256": {name: digest(raw) for name, raw in tests.items()},
        "runner_sha256": digest(Path(__file__).read_bytes()),
        "boundary": "Entire unextracted production BlitzRuntime and SourceFreeBallRenderer; managed collaborators inject faults and record calls.",
        "Unity_engine_executed": False, "Editor_executed": False, "native_player_executed": False,
        "real_asset_import_or_material_shader_execution": False,
        "simulation_or_RNG_parity_claimed": False, "runs": [],
    }
    try:
        sdk = subprocess.run(["dotnet", "--list-sdks"], cwd=ROOT, env=env, capture_output=True, text=True, check=True)
        versions = re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdk.stdout, re.M)
        require(versions, "An existing SDK/Roslyn compiler is required; no installation is attempted.")
        version, prefix = max(versions, key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        pack_root = Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref"
        packs = sorted(pack_root.glob("10.0.*/ref/net10.0"))
        require(compiler.is_file() and packs, "Existing .NET10 SDK reference pack required, no restore/package fallback.")
        references = sorted(packs[-1].glob("*.dll"))
        require(references, "SDK reference assembly inventory is empty.")
        report["compiler"] = {"path": str(compiler), "sha256": digest(compiler.read_bytes()), "sdk": version}
        report["managed_references_sha256"] = {str(path): digest(path.read_bytes()) for path in references}
        report["managed_framework"] = "SDK .NET10; execution is distinct from the archived Unity netstandard2.1 API compile"

        def run(label, host):
            directory = output / label
            directory.mkdir()
            sources = dict(inputs, **tests)
            sources[HOST] = host.encode("utf-8")
            copied = []
            for name, raw in sources.items():
                path = directory / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                copied.append(path)
            assembly = directory / "HostLifecycle.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3",
                       "-warn:4", "-warnaserror+", "-deterministic+", "-out:" + str(assembly)]
            options += ["-r:" + str(path) for path in references] + [str(path) for path in copied]
            response = directory / "compile.rsp"
            response.write_text("\n".join(json.dumps(value) for value in options) + "\n")
            command = ["dotnet", str(compiler), "-noconfig", "@" + str(response)]
            compiled = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
            diagnostics = compiled.stdout + compiled.stderr
            (directory / "build.log").write_text(diagnostics)
            row = {"name": label, "compile_command": command, "compile_exit": compiled.returncode,
                   "compile_diagnostics": diagnostics, "actual_host_sha256": digest(sources[HOST]),
                   "all_sources_sha256": {name: digest(raw) for name, raw in sources.items()}}
            report["runs"].append(row)
            require(compiled.returncode == 0 and not diagnostics,
                    label + ": compile must succeed with zero diagnostics; compile failure is never a killed control:\n" + diagnostics)
            row["assembly_sha256"] = digest(assembly.read_bytes())
            runtime = {"runtimeOptions": {"tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps(runtime) + "\n")
            command = ["dotnet", str(assembly)]
            executed = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
            log = executed.stdout + executed.stderr
            (directory / "run.log").write_text(log)
            row.update(run_command=command, run_exit=executed.returncode, output=log)
            summary = re.search(r"RESULT cases=(\d+) assertions=(\d+) failures=(\d+)", log)
            require(summary is not None, label + ": actual managed checks did not reach their named case summary.")
            row.update(cases=int(summary[1]), assertions=int(summary[2]), failures=int(summary[3]))
            print(label + ": compile=0 diagnostics=0 run=" + str(executed.returncode))
            print(log, end="")
            return row

        original = inputs[HOST].decode("utf-8")
        baseline = run("baseline", original)
        require(baseline["run_exit"] == 0 and baseline["failures"] == 0, "Managed production-boundary baseline failed.")
        if args.controls:
            mutations = controls(original)
            require(len(set(mutant for _, mutant, _ in mutations)) == len(mutations) and
                    all(mutant != original for _, mutant, _ in mutations), "Every causal control must change actual production source.")
            for label, mutant, expected in mutations:
                row = run(label, mutant)
                row["required_named_behavior_failure"] = expected
                row["killed_by_named_behavior"] = row["run_exit"] == 1 and row["failures"] > 0 and expected in row["output"]
                require(row["killed_by_named_behavior"], "Compiled causal control survived or failed for the wrong behavior: " + label)
            final = run("final-baseline", original)
            require(final["run_exit"] == 0 and final["failures"] == 0, "Final unchanged production baseline failed.")
            report["compiled_causal_controls_killed"] = len(mutations)
        require(all((source_root / name).read_bytes() == raw for name, raw in inputs.items()),
                "Production sources changed during execution; no valid source binding.")
        require(all((HERE / name).read_bytes() == raw for name, raw in tests.items()),
                "Test sources changed during execution; rerun on the final test bytes.")
        report["production_and_test_sources_unchanged"] = True
        report["status"] = "passed"
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
