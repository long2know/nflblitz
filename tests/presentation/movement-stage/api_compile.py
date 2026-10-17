#!/usr/bin/env python3
"""Compile ALL current-checkout runtime/Editor sources against real Unity APIs, never launch Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = Path(__file__).resolve().parent / ".artifacts" / "api"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor-data", type=Path, required=True)
    args = parser.parse_args()
    data = args.unity_editor_data.resolve(strict=True)
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    source = ARTIFACTS / "source"
    source.mkdir(exist_ok=False)
    report = {"schema": "movement-presentation-api/v1", "status": "failed", "profiles": [],
              "source_mode": "explicit-current-checkout", "root_path_is_provenance": False,
              "implicit_frozen_authoring_roots": False, "Editor_executed": False, "native_game_proof": False}
    try:
        runtime, editor = {}, {}
        for path in sorted((ROOT / "Assets/Scripts").rglob("*.cs")):
            relative = path.relative_to(ROOT).as_posix()
            collection = editor if "Editor" in path.relative_to(ROOT / "Assets/Scripts").parts else runtime
            collection[relative] = path.read_bytes()
            target = source / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(collection[relative])
        expected = {name: digest(value) for name, value in dict(runtime, **editor).items()}
        report["source_inventory_sha256"] = expected
        report.update(runtime_sources=len(runtime), ALL_Editor_sources=len(editor),
                      language="C#7.3", framework="actual Unity .NET Standard2.1", warnings_as_errors=True)
        env = dict(os.environ, TMPDIR=str(source), TMP=str(source), TEMP=str(source),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        sdk = subprocess.run(["dotnet", "--list-sdks"], env=env, capture_output=True, text=True, check=True)
        versions = re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdk.stdout, re.M)
        version, prefix = max(versions, key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        standard = data / "NetStandard/ref/2.1.0/netstandard.dll"
        engine = data / "Managed/UnityEngine"
        web = data / "PlaybackEngines/WebGLSupport/Managed/UnityEngine.WebGLModule.dll"
        if not all(path.is_file() for path in [compiler, standard, web]):
            raise AssertionError("Actual compiler/project-compatible Unity/WebGL APIs required; no SDK/stub substitutions.")
        references = [standard] + sorted(engine.glob("UnityEngine*.dll"))
        editor_refs = sorted(engine.glob("UnityEditor.*.dll")) + sorted((data / "Managed/UnityEditor").glob("*.dll"))
        if not editor_refs:
            raise AssertionError("Actual modular UnityEditor APIs missing.")
        web_refs = [path for path in references if path.name != web.name] + [web]

        def compile_one(label, define, sources, refs, filename):
            output_dir = ARTIFACTS / label
            output_dir.mkdir(exist_ok=True)
            output = output_dir / filename
            options = ["-nologo", "-nostdlib+", "-target:library", "-langversion:7.3", "-deterministic+",
                       "-warn:4", "-warnaserror+", "-define:" + define, "-out:" + str(output)]
            options += ["-r:" + str(path) for path in refs] + [str(source / name) for name in sources]
            response = ARTIFACTS / (label + ".rsp")
            response.write_text("\n".join(json.dumps(flag) for flag in options) + "\n")
            command = ["dotnet", str(compiler), "@" + str(response)]
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            log = result.stdout + result.stderr
            (ARTIFACTS / (label + ".log")).write_text(log)
            row = {"profile": label, "defines": define, "exit": result.returncode, "diagnostics": log,
                   "sources_sha256": {name: digest(raw) for name, raw in sources.items()},
                   "references_sha256": {str(path): digest(path.read_bytes()) for path in refs}, "command": command}
            if result.returncode == 0:
                row["assembly_sha256"] = digest(output.read_bytes())
            report["profiles"].append(row)
            print(label + ": " + ("PASS" if result.returncode == 0 else "FAIL") + " / " + str(len(sources)) + " actual sources")
            if log:
                print(log, end="")
            return result.returncode == 0, output

        editor_ok, editor_runtime = compile_one("Runtime-Editor", "UNITY_EDITOR", runtime,
                                               references + editor_refs, "Assembly-CSharp.dll")
        if editor_ok:
            compile_one("Editor-ALL", "UNITY_EDITOR", editor, references + editor_refs + [editor_runtime], "Assembly-CSharp-Editor.dll")
        target_ok, target_runtime = compile_one("Runtime-WebGL-Editor", "UNITY_EDITOR,UNITY_WEBGL",
                                               runtime, web_refs + editor_refs, "Assembly-CSharp.dll")
        if target_ok:
            compile_one("Editor-ALL-WebGL", "UNITY_EDITOR,UNITY_WEBGL", editor,
                        web_refs + editor_refs + [target_runtime], "Assembly-CSharp-Editor.dll")
        compile_one("Runtime-WebGL-Player", "UNITY_WEBGL", runtime, web_refs, "Assembly-CSharp.dll")
        current = {path.relative_to(ROOT).as_posix(): digest(path.read_bytes())
                   for path in sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
        if current != expected:
            raise AssertionError("Current source inventory changed during compile; rerun after all scoped owners freeze.")
        report["source_inventory_unchanged"] = True
        report["status"] = "passed" if len(report["profiles"]) == 5 and all(
            row["exit"] == 0 and not row["diagnostics"] for row in report["profiles"]) else "failed"
        return 0 if report["status"] == "passed" else 1
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error))
        return 1
    finally:
        (ARTIFACTS / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        shutil.rmtree(source)


if __name__ == "__main__":
    raise SystemExit(main())
