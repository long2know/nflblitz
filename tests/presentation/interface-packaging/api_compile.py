#!/usr/bin/env python3
"""Compile real protected B1 runtime and a SEPARATE Editor assembly; never start Unity."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = Path(__file__).resolve().parent / ".artifacts" / "api"
PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
EDITORS = ("Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs",
           "Assets/Scripts/Editor/SourceTeamInterfaceChecks.cs")
STAGE_PINS = {
    "Assets/Scripts/Runtime/BlitzRuntime.cs": "2fa24278edc26d8f0888fa59d131f00c96274ecc99efa3eac95b336100441620",
    "Assets/Scripts/Presentation/PlayHud.cs": "a4245a564bad178f4cfadefa85a2ac513254fbd1e17c2d2e3e950ab62678d459",
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(value, message):
    if not value:
        raise AssertionError(message)


def main():
    global ARTIFACTS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor-data", type=Path, required=True)
    parser.add_argument("--b1-root", type=Path, required=True)
    parser.add_argument("--ui-root", type=Path, required=True)
    parser.add_argument("--team-root", type=Path, required=True)
    parser.add_argument("--team-manifest", type=Path, required=True,
                        help="Explicit final Team hash manifest; no hidden authoring-state fallback.")
    parser.add_argument("--webgl-module", type=Path, help="Explicit actual target API when its installed layout differs.")
    parser.add_argument("--dependency-overlay", type=Path, help="Explicit parent four-null serialized-field overlay.")
    parser.add_argument("--dependency-overlay-sha256", help="Trusted content pin for the explicit parent overlay.")
    args = parser.parse_args()
    version = args.unity_editor_data.resolve(strict=True).parent.parent.name
    require(re.fullmatch(r"[a-zA-Z0-9._-]+", version), "Unsafe API report version.")
    ARTIFACTS = ARTIFACTS / version
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    work = ARTIFACTS / "source-inputs"
    report = {"schema": "source-interface-package-api/v1", "status": "failed",
              "language": "C#7.3", "framework": "actual Unity .NET Standard2.1",
              "separate_runtime_editor_assemblies": True, "Primary_M1_used": False,
              "Unity_stubs": False, "Editor_executed": False, "native_assets_saved": False,
              "native_or_browser_game_claimed": False, "profiles": []}
    guarded = {}
    try:
        core, ui, team = (path.resolve(strict=True) for path in (args.b1_root, args.ui_root, args.team_root))
        require(core != ROOT, "Primary hot M1 is not a protected B1 input.")
        helper = ROOT / "tests/interface/stage_followup.py"
        spec = importlib.util.spec_from_file_location("blitz_real_stage_dependencies", helper)
        stage = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(stage)
        sources, origins, guarded, owned_ui = stage.protected_sources(ROOT, core, team, ui, args.team_manifest)
        if args.dependency_overlay is not None or args.dependency_overlay_sha256 is not None:
            require(args.dependency_overlay is not None and args.dependency_overlay_sha256 is not None,
                    "Supply BOTH explicit parent dependency overlay and its trusted SHA256.")
            overlay = args.dependency_overlay.resolve(strict=True)
            content = overlay.read_bytes()
            require(digest(content) == args.dependency_overlay_sha256, "Parent overlay content pin mismatch.")
            stage.check_initializer_only_overlay(sources[stage.DEPENDENCY], content)
            sources[stage.DEPENDENCY] = content
            origins[stage.DEPENDENCY] = "explicit-parent-four-null-initializers-only"
            guarded[overlay] = args.dependency_overlay_sha256
            report["explicit_parent_dependency_overlay"] = {
                "path": str(overlay), "sha256": args.dependency_overlay_sha256,
                "four_null_initializers_only": True}
        for name, expected in STAGE_PINS.items():
            require(digest(sources[name]) == expected, "Authorized UI stage overlay differs: " + name)
        sources[PACKAGE] = (ROOT / PACKAGE).read_bytes()
        origins[PACKAGE] = "new-production-runtime-preflight"
        guarded[ROOT / PACKAGE] = digest(sources[PACKAGE])
        editor_sources = {}
        manifest = json.loads(args.team_manifest.read_bytes())
        for name, expected in manifest["production_sources"].items():
            if "/Editor/" in name:
                data = (team / name).read_bytes()
                require(digest(data) == expected, "Frozen Team Editor dependency changed: " + name)
                guarded[team / name] = expected
                editor_sources[name] = data
        for name in EDITORS:
            editor_sources[name] = (ROOT / name).read_bytes()
            guarded[ROOT / name] = digest(editor_sources[name])
        report.update(runtime_source_sha256={name: digest(data) for name, data in sources.items()},
                      runtime_source_origin=origins,
                      editor_source_sha256={name: digest(data) for name, data in editor_sources.items()},
                      complete_owned_UI_file_sha256=owned_ui)
        require(all(name in editor_sources for name in EDITORS), "Both permanent Editor callers must compile.")
        data = args.unity_editor_data.resolve(strict=True)
        standard = data / "NetStandard/ref/2.1.0/netstandard.dll"
        engine = data / "Managed/UnityEngine"
        require(standard.is_file() and (engine / "UnityEngine.CoreModule.dll").is_file(),
                "Real installed Unity netstandard2.1 and engine assemblies required.")
        editor_refs = sorted(engine.glob("UnityEditor.*.dll")) + sorted(
            (data / "Managed/UnityEditor").glob("*.dll"))
        if not editor_refs and (data / "Managed/UnityEditor.dll").is_file():
            editor_refs = [data / "Managed/UnityEditor.dll"]
        require(editor_refs, "Real installed UnityEditor API assemblies required.")
        work.mkdir(parents=True, exist_ok=False)
        env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        sdks = subprocess.run(["dotnet", "--list-sdks"], env=env, capture_output=True, text=True, check=True)
        found = re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdks.stdout, re.M)
        require(found, "Installed Roslyn compiler required.")
        version, prefix = max(found, key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        require(compiler.is_file(), "Actual Roslyn compiler missing.")
        copied, copied_editor = [], []
        for collection, output in ((sources, copied), (editor_sources, copied_editor)):
            for name, content in collection.items():
                target = work / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
                output.append(target)
        references = [standard] + sorted(engine.glob("UnityEngine*.dll"))

        def compile_profile(label, define, inputs, refs):
            output = ARTIFACTS / (label + ".dll")
            options = ["-nologo", "-nostdlib+", "-target:library", "-langversion:7.3",
                       "-warn:4", "-warnaserror+", "-define:" + define, "-out:" + str(output)]
            options += ["-r:" + str(path) for path in refs] + [str(path) for path in inputs]
            response = ARTIFACTS / (label + ".rsp")
            response.write_text("\n".join(json.dumps(value) for value in options) + "\n")
            command = ["dotnet", str(compiler), "@" + str(response)]
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            log = result.stdout + result.stderr
            (ARTIFACTS / (label + ".log")).write_text(log)
            entry = {"label": label, "defines": define, "status": "passed" if result.returncode == 0 else "failed",
                     "source_count": len(inputs), "diagnostics": log, "command": command,
                     "reference_sha256": {str(path): digest(path.read_bytes()) for path in refs}}
            if result.returncode == 0:
                entry["assembly_sha256"] = digest(output.read_bytes())
            report["profiles"].append(entry)
            print(label + ": " + entry["status"] + " / " + str(len(inputs)) + " actual sources")
            if log:
                print(log, end="")
            return result.returncode == 0, output

        editor_ok, runtime_assembly = compile_profile("Runtime-UNITY_EDITOR", "UNITY_EDITOR", copied, references + editor_refs)
        editor_callers_ok = False
        new_callers_ok = False
        if editor_ok:
            new_inputs = [path for path in copied_editor if path.relative_to(work).as_posix() in
                          EDITORS + ("Assets/Scripts/Editor/PlayerAssetImporter.cs",)]
            require(len(new_inputs) == 3, "Compile both NEW callers with their actual frozen PlayerAssetImporter dependency.")
            new_callers_ok, _ = compile_profile("NewCallers-UNITY_EDITOR", "UNITY_EDITOR",
                new_inputs, references + editor_refs + [runtime_assembly])
            editor_callers_ok, _ = compile_profile("Editor-UNITY_EDITOR", "UNITY_EDITOR",
                copied_editor, references + editor_refs + [runtime_assembly])
        web_module = args.webgl_module.resolve(strict=True) if args.webgl_module else (
            data / "PlaybackEngines/WebGLSupport/Managed/UnityEngine.WebGLModule.dll")
        require(web_module.is_file(), "Actual installed WebGL target API required, not a WebGLInput stub.")
        web_refs = [path for path in references if path.name != web_module.name] + [web_module]
        web_ok, _ = compile_profile("Runtime-UNITY_WEBGL", "UNITY_WEBGL", copied, web_refs)
        report.update(complete_editor_API_passed=editor_ok and editor_callers_ok,
                      new_caller_API_passed=editor_ok and new_callers_ok,
                      complete_webgl_API_passed=web_ok, actual_editor_data=str(data))
        for path, expected in guarded.items():
            require(digest(path.read_bytes()) == expected, "Protected source changed during API compilation: " + str(path))
        report["protected_inputs_unchanged"] = True
        report["status"] = "passed" if editor_ok and editor_callers_ok and web_ok else "failed"
        return 0 if report["status"] == "passed" else 1
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (ARTIFACTS / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
