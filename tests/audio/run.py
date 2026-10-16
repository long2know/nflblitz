#!/usr/bin/env python3
"""Owned audio checks. SDK execution and actual Unity API compilation are separate."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUTPUT = HERE / ".artifacts"
PROJECT = HERE / "SourceAudioChecks.csproj"
READER = ROOT / "Assets/Scripts/AssetImport/SourceAudioBankReader.cs"
ADAPTER = ROOT / "Assets/Scripts/Presentation/SourceAudioIsolatedPlayer.cs"
PREPARE = ROOT / "tools/audio/prepare.py"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def environment():
    scratch = OUTPUT / "build-scratch"
    scratch.mkdir(parents=True, exist_ok=True)
    return dict(os.environ, TMPDIR=str(scratch), DOTNET_NOLOGO="1",
                DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
                DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")


def command(args, log, check=True, extra_environment=None):
    env = environment()
    if extra_environment:
        env.update(extra_environment)
    result = subprocess.run([str(x) for x in args], cwd=ROOT, env=env,
                            text=True, capture_output=True)
    text = result.stdout + result.stderr
    (OUTPUT / (log + ".log")).write_text(text, encoding="utf-8")
    if check and result.returncode:
        raise RuntimeError(f"{log} failed:\n{text[-5000:]}")
    return result, text


def load_prepare():
    spec = importlib.util.spec_from_file_location("original_audio_prepare", PREPARE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build(reader=None, log="pure-build"):
    args = ["dotnet", "build", PROJECT, "--no-restore", "--verbosity", "quiet"]
    if reader:
        args.append("-p:SourceAudioReaderPath=" + str(reader))
    result, text = command(args, log, check=False)
    if result.returncode and "NETSDK1004" in text:
        empty = OUTPUT / "empty-package-feed"
        empty.mkdir(exist_ok=True)
        command(["dotnet", "restore", PROJECT, "--source", empty,
                 "-p:NuGetAudit=false", "--verbosity", "quiet"], "restore-local-sdk")
        result, text = command(args, log, check=False)
    if result.returncode:
        raise RuntimeError(f"{log} failed:\n{text[-4000:]}")


def pure(manifest=None, log="pure-tests", check=True):
    args = ["dotnet", HERE / "bin/Debug/net10.0/SourceAudioChecks.dll"]
    if manifest:
        args.append(manifest)
    result, text = command(args, log, check=check)
    return result, json.loads(text.strip()) if result.returncode == 0 else text


def unity_compile(editor_data):
    editor_data = editor_data.resolve()
    standard = editor_data / "NetStandard/ref/2.1.0"
    engine = editor_data / "Managed/UnityEngine"
    references = sorted(standard.glob("*.dll")) + [
        engine / "UnityEngine.CoreModule.dll", engine / "UnityEngine.AudioModule.dll"]
    if not references or not all(path.is_file() for path in references):
        raise RuntimeError("Actual Unity netstandard/AudioModule references are unavailable: " + str(editor_data))
    sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1]
    match = re.fullmatch(r"(\S+)\s+\[(.+)\]", sdk)
    compiler = Path(match[2]) / match[1] / "Roslyn/bincore/csc.dll"
    results = []
    for define in ["UNITY_EDITOR", "UNITY_WEBGL"]:
        output = OUTPUT / ("SourceAudio." + define + ".dll")
        args = ["dotnet", compiler, "-nologo", "-noconfig", "-nostdlib+",
                "-langversion:7.3", "-target:library", "-warnaserror+", "-deterministic+",
                "-define:" + define, "-out:" + str(output)]
        args += ["-reference:" + str(path) for path in references]
        args += [READER, ADAPTER]
        command(args, "actual-api-" + define.lower())
        results.append({"define": define, "sha256": digest(output), "errors": 0, "warnings": 0})
    return {
        "status": "passed",
        "profile": "C#7.3 / actual Unity netstandard2.1 references",
        "reference_root": str(editor_data),
        "reference_sha256": {str(path.relative_to(editor_data)): digest(path) for path in references},
        "compilations": results, "Editor_launched": False, "runtime_executed": False,
    }


def reference_comparison(audio, manifest_path):
    binary, build_receipt = audio.build_decoder(reference=True)
    manifest = json.loads(manifest_path.read_text())
    root = manifest_path.parent
    records = []
    for bank in manifest["banks"]:
        for entry in bank["entries"]:
            if not entry["prepared"]:
                continue
            name = bank["name"]
            out = OUTPUT / "reference" / f"{name}-{entry['id']}"
            out.mkdir(parents=True, exist_ok=True)
            base = audio.SOURCE / "nflblitz2000/sounds"
            result, text = command([
                binary, base / (name.upper() + ".BNK"), base / (name.upper() + ".LST"),
                "--outdir", out, "--track", f"{entry['id']:04x}"],
                f"reference-{name}-{entry['id']}")
            files = list(out.glob("*.wav"))
            if len(files) != 1 or "1 written, 0 errors" not in text:
                raise RuntimeError(f"reference did not extract exactly {name}:{entry['id']}")
            expected = root / name / entry["sample"]["wav_file"]
            if files[0].read_bytes() != expected.read_bytes():
                raise RuntimeError(f"reference PCM/WAV mismatch {name}:{entry['id']}")
            records.append({"bank": name, "id": entry["id"], "wav_sha256": digest(files[0])})
    return {
        "entrypoint": "Asure/DCSExplorer BnkExtract at pinned 1b7d0126cabb79c4c1365a90542806a60c9436f8",
        "build": build_receipt, "matched_playlists": len(records), "records": records,
        "independent_decoder": False,
        "limitation": "Independent upstream BNK traversal/WAV entrypoint, SAME native decoder core. Not an independent DSP, audible, or game-audio oracle.",
    }


def native_failure_checks(audio):
    binary = audio.ARTIFACTS / "bin/decode"
    directory = OUTPUT / "native-fixtures"
    directory.mkdir(exist_ok=True)
    short = directory / "short.dcs"
    zero = directory / "zero-frames.dcs"
    reserved = directory / "reserved-frames.dcs"
    large = directory / "too-large.dcs"
    short.write_bytes(bytes(17))
    zero.write_bytes(bytes(18))
    reserved.write_bytes(b"\xff\xff" + bytes(16))
    large.write_bytes(bytes(1024 * 1024 + 1))
    output = directory / "must-not-change.pcm"
    sentinel = b"synthetic output preservation sentinel"
    cases = [
        ([], "usage:"),
        ([short, "abc", output], "unsupported mixing level"),
        ([short, "-1", output], "unsupported mixing level"),
        ([short, "128", output], "unsupported mixing level"),
        ([short, "", output], "unsupported mixing level"),
        ([short, "90", output], "stream size"),
        ([zero, "90", output], "invalid frame count"),
        ([reserved, "90", output], "invalid frame count"),
        ([large, "90", output], "stream size"),
        ([directory / "does-not-exist.dcs", "90", output], "cannot open stream"),
    ]
    for i, (args, diagnostic) in enumerate(cases):
        output.write_bytes(sentinel)
        result, text = command([binary, *args], f"native-negative-{i}", check=False)
        if result.returncode == 0 or diagnostic not in text or output.read_bytes() != sentinel:
            raise RuntimeError("Native decoder input guard failed: " + diagnostic)
    for path in [short, zero, reserved, large, output]:
        path.unlink()
    return {
        "cases": len(cases), "all_rejected_before_decode": True,
        "output_preserved": True,
        "limitation": "Structural CLI/header guards only. Arbitrary compressed inputs are not sandboxed; bank payload mutations are rejected by Git pins before native execution.",
    }


def primary_listing_checks(audio, manifest_path):
    manifest = json.loads(manifest_path.read_text())
    header = (audio.SOURCE / "nflblitz2000/include/sndcalls.h").read_text()
    constants_checked = 0
    for group in ["volume_bytes", "priority_words"]:
        for name, expected in manifest["host_parameter_values"][group].items():
            match = re.search(r"^#define\s+" + re.escape(name) + r"\s+([^\r\n]+)", header, re.M)
            expression = match[1].split("//")[0].strip() if match else ""
            values = re.fullmatch(r"(\d+)(?:\s*-\s*(\d+))?", expression)
            if not values or int(values[1]) - int(values[2] or 0) != expected:
                raise RuntimeError("Host parameter metadata contradicts pinned source: " + name)
            constants_checked += 1
    records = []
    for bank in manifest["banks"]:
        path = audio.SOURCE / "nflblitz2000/sounds" / (bank["name"].upper() + ".LST")
        text = path.read_text()
        rows = re.findall(
            r"^([A-Z0-9_]+)\s+(\d+)\s+(\d+)\s+(\d+):(\d+\.\d+)\s+[\d.]+\s+HENRY[^\r\n]*",
            text, re.M)
        if not rows:
            raise RuntimeError("Missing primary compressed-region listing")
        maximum_error = 0
        frame_counts = set()
        for name, size, frames, minutes, seconds in rows:
            frame_counts.add(int(frames))
            error = abs(int(minutes) * 60 + float(seconds) - int(frames) * 240 / 31250)
            maximum_error = max(maximum_error, error)
            if error > 0.000500001:
                raise RuntimeError("Primary listing contradicts decoder frame rate: " + name)
        if any(entry["stream_frames"] not in frame_counts
               for entry in bank["entries"] if entry["prepared"]):
            raise RuntimeError("A prepared original frame count is absent from PLBUILD regions")
        records.append({
            "bank": bank["name"], "regions_checked": len(rows),
            "maximum_printed_duration_error_seconds": maximum_error,
            "listing_sha256": digest(path),
        })
    return {
        "records": records,
        "original_volume_priority_constants_checked": constants_checked,
        "claim": "Original PLBUILD region frame/duration pairs independently agree with 240 samples at 31250Hz (excluding the decoder's explicit two tail frames). Not a PCM/audio oracle or region-name binding.",
    }


def mutations():
    directory = OUTPUT / "mutations"
    directory.mkdir(exist_ok=True)
    python_source = PREPARE.read_text()
    csharp_source = READER.read_text()
    python_mutations = {
        "blob-pin-bypass": (
            'if actual != expected:\n        raise AudioError(f"{name}: Git blob mismatch:',
            'if expected == \'\':\n        raise AudioError(f"{name}: Git blob mismatch:'),
        "bank-size-bypass": ("data_size != len(data) - base", "data_size < 0"),
        "relative-address-off-by-one": ("absolute + 11 + relative", "absolute + 12 + relative"),
        "repeat-zero-treated-as-once": ("elif raw[14] != 1:", "elif raw[14] > 255:"),
        "end-wait-discarded": ('if entry["end_wait_frames"] != frames + 1:', 'if entry["end_wait_frames"] < 0:'),
        "pcm-count-bypass": ("if len(pcm) != (frames + TAIL_FRAMES) * FRAME_SAMPLES * 2:",
                             "if len(pcm) < 0:"),
        "silent-substitution": ("if not any(samples):", "if len(samples) < 0:"),
        "guessed-32000-rate": ("RATE = 31250", "RATE = 32000"),
    }
    cs_mutations = {
        "package-pin-bypass": (
            "!String.Equals(packageHash, expectedPackageSha256, StringComparison.Ordinal)",
            "!String.Equals(packageHash, packageHash, StringComparison.Ordinal)"),
        "pcm-integrity-bypass": (
            "!String.Equals(Hash(pcm), pcmHash, StringComparison.Ordinal)",
            "!String.Equals(pcmHash, pcmHash, StringComparison.Ordinal)"),
        "repeat-accepted": ("p[14] != 1", "p[14] > 2"),
        "nonstandard-end-qualified": (
            "EndWaitFrames == OriginalStreamFrames + 1", "EndWaitFrames >= OriginalStreamFrames"),
    }
    records = []
    for name, (before, after) in python_mutations.items():
        if python_source.count(before) != 1:
            raise RuntimeError("mutation anchor changed: " + name)
        path = directory / (name + ".py")
        path.write_text(python_source.replace(before, after))
        result, text = command(
            [sys.executable, "-m", "unittest", "discover", "-s", HERE, "-p", "test_prepare.py"],
            "mutant-" + name, check=False, extra_environment={"SOURCE_AUDIO_MODULE": str(path)})
        if result.returncode == 0 or "FAILED (" not in text:
            raise RuntimeError("Python source mutant not caught by assertions: " + name)
        records.append({"language": "Python", "mutation": name, "rejected": True,
                        "mutated_source_sha256": digest(path)})
    for name, (before, after) in cs_mutations.items():
        if csharp_source.count(before) != 1:
            raise RuntimeError("mutation anchor changed: " + name)
        path = directory / (name + ".cs")
        path.write_text(csharp_source.replace(before, after))
        build(path, "mutant-build-" + name)
        result, text = pure(log="mutant-" + name, check=False)
        if result.returncode == 0 or "AUDIO_ASSERTION:" not in text:
            raise RuntimeError("C# source mutant not caught by assertions: " + name)
        records.append({"language": "C#", "mutation": name, "rejected": True,
                        "mutated_source_sha256": digest(path)})
    build(log="post-mutation-baseline-build")
    pure(log="post-mutation-baseline")
    # Keep the red-test logs and hashes, not alternative source implementations.
    for path in directory.iterdir():
        if path.suffix in (".py", ".cs"):
            path.unlink()
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, help="ignored manifest.json from prepare.py")
    parser.add_argument("--mutate", action="store_true")
    parser.add_argument("--reproduce", help="fresh ignored extraction run name")
    parser.add_argument("--unity-editor-data", type=Path,
                        help="Optional actual Unity Editor/Data directory for explicit API compilation; never starts Editor")
    args = parser.parse_args()
    OUTPUT.mkdir(exist_ok=True)
    owned = [PREPARE, ROOT / "tools/audio/decode.cpp", ROOT / "tools/audio/inputs.json",
             ROOT / "tools/audio/.gitignore", ROOT / "tools/audio/README.md", READER, ADAPTER,
             HERE / ".gitignore", HERE / "Program.cs", PROJECT, HERE / "test_prepare.py", Path(__file__).resolve()]
    before = {str(path.relative_to(ROOT)): digest(path) for path in owned}
    result, text = command([sys.executable, "-m", "unittest", "discover", "-s", HERE,
                            "-p", "test_prepare.py"], "parser-tests")
    count = int(re.search(r"Ran (\d+) tests", text)[1])
    build()
    _, pure_receipt = pure(args.original.resolve() if args.original else None)
    report = {
        "schema": "source-audio-validation/v1", "parser_tests": count,
        "pure_reader": pure_receipt,
        "actual_unity_api": unity_compile(args.unity_editor_data) if args.unity_editor_data else {
            "status": "not_requested", "Editor_launched": False, "runtime_executed": False,
        },
    }
    audio = load_prepare()
    if args.original:
        manifest = args.original.resolve()
        original_inputs = audio.verify_sources()
        report["native_negative_checks"] = native_failure_checks(audio)
        report["primary_listing_checks"] = primary_listing_checks(audio, manifest)
        report["reference_comparison"] = reference_comparison(audio, manifest)
        if args.reproduce:
            reproduced = audio.extract(args.reproduce)
            names = ["manifest.json", "cursors.saud", "players.saud"]
            equal = {name: digest(reproduced / name) == digest(manifest.parent / name) for name in names}
            if not all(equal.values()):
                raise RuntimeError("Original extraction is not deterministic: " + str(equal))
            report["deterministic_reproduction"] = {
                "products_identical": equal,
                "products_sha256": {name: digest(reproduced / name) for name in names},
            }
        if audio.verify_sources() != original_inputs:
            raise RuntimeError("Pinned original inputs changed during validation")
        report["original_inputs_preserved"] = True
    if args.mutate:
        report["mutations"] = mutations()
        if args.original:
            _, report["post_mutation_original_reader"] = pure(args.original.resolve(), "post-mutation-original")
    if args.original and audio.verify_sources() != original_inputs:
        raise RuntimeError("Pinned originals changed during mutation checks")
    after = {str(path.relative_to(ROOT)): digest(path) for path in owned}
    if before != after:
        raise RuntimeError("Production source changed during tests")
    report["production_sha256"] = after
    report["production_sources_preserved"] = True
    output = OUTPUT / "result.json"
    output.write_bytes(audio.canonical(report))
    print(json.dumps({
        "report": str(output.relative_to(ROOT)), "sha256": digest(output),
        "parser_tests": count, "reader_checks": pure_receipt["checks"],
        "original_playlists": pure_receipt["originalPlaylists"],
        "rejected_mutants": len(report.get("mutations", [])),
        "unity_api": "API compilation only" if args.unity_editor_data else "not requested; SDK-only execution",
    }, sort_keys=True))


if __name__ == "__main__":
    main()
