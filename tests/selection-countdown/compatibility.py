"""Byte-compare real untriggered Core/cabinet/bridge/HUD and every raw RNG call with pinned main40."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import time

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
BASE = "e3157c42df9a5f09d534f689339495429e778768"
TREE = "7793a6257537fb165e97f7316cc8e6d97b2e72fe"
FIXTURE_LOADER = "tests/parity/FullKernelInputs.cs"
LOADER_SHA256 = {
    "main40": "c0c6b4dcd84e8780dfffc66b29671af3d656ee59f01efd33e6746cf58e08c745",
    "current": "2f6ed6c7bd4bc844f7c31ae9c9e2a249f274edcbd665857eff216adfba5a0ea6",
}


def diagnostic_module():
    spec = importlib.util.spec_from_file_location("source_default_diagnostic", HERE / "default_diagnostic.py")
    module = importlib.util.module_from_spec(spec)
    # Dataclass metadata must resolve the actual, uniquely named imported module.
    import sys
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


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
    require(set(entries) == set(names), "Baseline selected compile closure differs.")
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
    baseline = source_baseline(sorted([*production, FIXTURE_LOADER]))
    loaders = {"main40": baseline.pop(FIXTURE_LOADER),
               "current": (ROOT / FIXTURE_LOADER).read_bytes()}
    require({name: hashlib.sha256(raw).hexdigest() for name, raw in loaders.items()} == LOADER_SHA256,
        "Each exact source tree must retain its own authenticated fixture loader.")
    common = {"DefaultChecks.cs": (HERE / "DefaultChecks.cs").read_bytes()}
    receipt = dict(schema="untriggered-cabinet-byte-compatibility/v1", base=BASE, tree=TREE,
                   commands=[], runs=[], compared={}, originalTargetRandomQualified=False,
                   fullPublicReplayRegenerated=False, hostedOrFullM1Campaign=False,
                   harnessSha256={n: hashlib.sha256(b).hexdigest() for n, b in common.items()},
                   fixtureLoaderPath=FIXTURE_LOADER)
    diagnostic = diagnostic_module()
    historical, historical_blob = diagnostic.historical_fraction()
    current = dict(production)
    require(len(baseline) == len(current) == 94, "Exact94-file default production closure required.")
    require([diagnostic.sha(diagnostic.canonical({name: diagnostic.sha(raw) for name, raw in source.items()}))
             for source in (baseline, current)] == list(diagnostic.PRODUCTION_MAPS),
            "Pinned main40/current production behavior must remain unchanged for this diagnostic.")
    control = dict(current)
    control[diagnostic.BODY_PATH] = diagnostic.one_body_control(current[diagnostic.BODY_PATH], historical)
    client = (HERE / "DefaultBudget.cs").read_bytes()
    rng = [diagnostic.verify_random_source(source, client) for source in (baseline, current)]
    wrapped = diagnostic.guarded_driver(common["DefaultChecks.cs"])
    provenance = dict(path=diagnostic.BODY_PATH, historicalCommit=diagnostic.HISTORICAL,
                      historicalBlob=historical_blob, oldBodySha256=diagnostic.OLD_BODY,
                      currentBodySha256=diagnostic.CURRENT_BODY,
                      currentFileSha256=diagnostic.sha(current[diagnostic.BODY_PATH]),
                      controlFileSha256=diagnostic.sha(control[diagnostic.BODY_PATH]),
                      onlyFractionBodyChanged=True, StreamFrameRemainsCurrent=True, mustNeverShip=True,
                      randomSourceSeals=rng)
    with diagnostic.execute_three(args, dict(A=baseline, B=current, C=control),
            dict(A=loaders["main40"], B=loaders["current"], C=loaders["current"]),
            compiler, references, wrapped, client, provenance) as evidence:
        receipt["commands"] = evidence["commands"]
        receipt["runs"] = [evidence["rows"]["A"], evidence["rows"]["B"]]
        receipt["diagnosticControl"] = evidence["rows"]["C"]
        receipt["threeWayDiagnostic"] = evidence["report"]
        # Metadata is durable and visible BEFORE the existing final acceptance guard.
        (output / "diagnostic.json").write_text(json.dumps(evidence["report"], indent=2) + "\n")
        public = {key: value for key, value in evidence["report"].items()
                  if key not in ("completeInputSeals", "completeToolAndReferenceSeals", "completeCompiledSourceSeals")}
        public["completeMetadataSha256"] = diagnostic.sha(diagnostic.canonical(evidence["report"]))
        print("DEFAULT COMPATIBILITY DIAGNOSTIC " + json.dumps(public, sort_keys=True), flush=True)
        for name in ("default-host.jsonl", "default-rng.json", "default-menu.json"):
            a, b = [evidence["blobs"][label][name] for label in ("A", "B")]
            require(a == b, "Untriggered actual Core/ordered RNG/HUD bytes changed: " + name)
            receipt["compared"][name] = dict(bytes=len(a), sha256=hashlib.sha256(a).hexdigest(), byteIdentical=True)
    receipt["status"] = "passed"
    receipt["seconds"] = round(time.monotonic() - started, 3)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt
