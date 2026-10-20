"""Byte-compare real untriggered Core/cabinet/bridge/HUD and every raw RNG call with pinned main40."""
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import time

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
BASE = "e3157c42df9a5f09d534f689339495429e778768"
TREE = "7793a6257537fb165e97f7316cc8e6d97b2e72fe"


def require(value, message):
    if not value:
        raise AssertionError(message)


def source_baseline(names):
    def git(*args):
        return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, check=True).stdout
    try:
        git("cat-file", "-e", BASE + "^{commit}")
    except subprocess.CalledProcessError:
        git("fetch", "--no-tags", "--depth=1", "origin", BASE)
    require(git("rev-parse", BASE + "^{tree}").decode().strip() == TREE, "Exact main40 baseline commit/tree required.")
    entries = {}
    for row in git("ls-tree", "-r", "-z", BASE, "--", *names).split(b"\0"):
        if row:
            meta, name = row.decode().split("\t")
            mode, kind, blob = meta.split()
            require(kind == "blob" and mode in ("100644", "100755") and name in names,
                    "Only selected regular original-main production sources are permitted.")
            entries[name] = blob
    require(set(entries) == set(names), "Baseline production compile closure differs.")
    files = {}
    with tarfile.open(fileobj=io.BytesIO(git("archive", "--format=tar", BASE, "--", *names))) as archive:
        for member in archive:
            if member.isdir():
                continue
            require(member.isfile() and member.name in entries and member.name not in files,
                    "Unsafe/duplicate baseline source archive member.")
            data = archive.extractfile(member).read()
            require(hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() == entries[member.name],
                    "Baseline selected Git blob differs.")
            files[member.name] = data
    require(set(files) == set(names), "Incomplete authenticated baseline production archive.")
    return files


def generate(args, output, production, compiler, references):
    started = time.monotonic()
    output.mkdir()
    baseline = source_baseline(sorted(production))
    common = {"DefaultChecks.cs": (HERE / "DefaultChecks.cs").read_bytes(),
              "FullKernelInputs.cs": (ROOT / "tests/parity/FullKernelInputs.cs").read_bytes()}
    receipt = dict(schema="untriggered-cabinet-byte-compatibility/v1", base=BASE, tree=TREE,
                   commands=[], runs=[], compared={}, originalTargetRandomQualified=False,
                   fullPublicReplayRegenerated=False, hostedOrFullM1Campaign=False,
                   harnessSha256={n: hashlib.sha256(b).hexdigest() for n, b in common.items()})
    for label, source in (("main40", baseline), ("current", production)):
        destination = output / label
        destination.mkdir()
        paths = []
        for name, data in dict(source, **common).items():
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            paths.append(path)
        assembly = destination / "Default.Checks.dll"
        options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+", "-deterministic+",
                   "-optimize+", "-out:" + str(assembly), "-main:DefaultCabinetChecks"]
        options += ["-r:" + str(p) for p in references] + [str(p) for p in paths]
        response = destination / "compile.rsp"
        response.write_text("\n".join(json.dumps(v) for v in options) + "\n")
        env = dict(os.environ, TMPDIR=str(destination), TMP=str(destination), TEMP=str(destination),
                   DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
        command = ["dotnet", str(compiler), "-noconfig", "@" + str(response)]
        compile_start = time.monotonic()
        build = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
        compile_seconds = round(time.monotonic() - compile_start, 3)
        receipt["commands"].append(command)
        (destination / "compile.log").write_text(build.stdout + build.stderr)
        require(build.returncode == 0 and not build.stdout + build.stderr, "Default compatibility compilation must have zero diagnostics.")
        assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
            "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
        command = ["dotnet", str(assembly)] + [str(getattr(args, n).absolute()) for n in
            ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")] + [str(destination)]
        run_start = time.monotonic()
        run = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        receipt["commands"].append(command)
        (destination / "run.log").write_text(run.stdout + run.stderr)
        require(run.returncode == 0 and not run.stderr,
                "Actual default Core/cabinet compatibility execution failed: " + run.stdout + run.stderr)
        receipt["runs"].append(dict(name=label, compile=build.returncode, diagnostics=build.stdout + build.stderr,
            exit=run.returncode, summary=json.loads(run.stdout),
            compileSeconds=compile_seconds, executeSeconds=round(time.monotonic() - run_start, 3),
            buildRoot=str(destination), runtimeRoot=str(destination),
            sources={n: hashlib.sha256(b).hexdigest() for n, b in source.items()}))
    for name in ("default-host.jsonl", "default-rng.json", "default-menu.json"):
        a, b = [(output / label / name).read_bytes() for label in ("main40", "current")]
        require(a == b, "Untriggered actual Core/ordered RNG/HUD bytes changed: " + name)
        receipt["compared"][name] = dict(bytes=len(a), sha256=hashlib.sha256(a).hexdigest(), byteIdentical=True)
    receipt["status"] = "passed"
    receipt["seconds"] = round(time.monotonic() - started, 3)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt
