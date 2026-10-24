"""Source-correct Fraction checks plus an authenticated legacy counterfactual projection."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
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
SCHEMA = "source-correct-default-legacy-projection/v2"
POLICY = "source-correct-Fraction/legacy-counterfactual/v1"


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


def validate_receipt(receipt):
    """The same complete obligation set is used by generation and every caller."""
    try:
        _validate_receipt(receipt)
    except AssertionError:
        raise
    except (KeyError, TypeError, ValueError, IndexError, AttributeError):
        require(False, "Missing/corrupt source-correct default projection receipt.")


def _validate_receipt(receipt):
    d = diagnostic_module()
    hex64 = lambda value: isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None
    require(receipt["schema"] == SCHEMA and receipt["status"] == "passed"
            and receipt["base"] == BASE and receipt["tree"] == TREE
            and receipt["originalTargetRandomQualified"] is False
            and receipt["fullPublicReplayRegenerated"] is False
            and receipt["hostedOrFullM1Campaign"] is False, "Pinned historical source/reference scope required.")
    require(receipt["fixtureLoaderPath"] == FIXTURE_LOADER
            and receipt["harnessSha256"] == {"DefaultChecks.cs": d.DRIVER_SHA},
            "Actual unchanged default driver and own-loader provenance required.")
    rows = receipt["runs"]
    require([row["name"] for row in rows] == list(d.NAMES), "All three actual variants required.")
    report, obligations = receipt["threeWayDiagnostic"], receipt["compatibilityObligations"]
    require(obligations["policy"] == POLICY and obligations["currentHostGoldenUsed"] is False,
            "Explicit source-correct legacy-counterfactual policy required.")
    pure = report["compiledPureContract"]
    require(pure == dict(id=d.PURE_CONTRACT, cases=list(d.PURE_CASES), casesPerVariant=8,
                         totalInvocations=24, currentVariant="B", noGameStepsWakesRandomOrExtraCompiler=True),
            "Independent actual compiled Fraction case inventory required.")
    control = report["sourceControl"]
    require(control["path"] == d.BODY_PATH and control["historicalCommit"] == d.HISTORICAL
            and control["historicalBlob"] == "0f10e77c5ba2240e641464d027339f1250b9cc54"
            and control["oldBodySha256"] == d.OLD_BODY and control["currentBodySha256"] == d.CURRENT_BODY
            and control["currentFileSha256"] == "0866f29da7f9287742a93476dee787165baa23f94cb58e08f6f87584739893f4"
            and control["controlFileSha256"] == "e1a7c82ff278bd9c5b997f3c895756fd32dc242282e05d508ee80e31fdfce223"
            and control["onlyFractionBodyChanged"] is True and control["StreamFrameRemainsCurrent"] is True
            and control["mustNeverShip"] is True, "Exactly one authenticated historical Fraction body required.")
    next_int_sha = d.sha(d.method((ROOT / "Assets/Scripts/Gameplay/GameSimulation.cs").read_bytes(),
                                 b"private int NextInt(int maximum)"))
    require(len(control["randomSourceSeals"]) == 2 and all(
        seal["defaultSeed"] == 1 and seal["concreteIdentityPreserved"] is True
        and seal["clientSha256"] == d.sha((HERE / "DefaultBudget.cs").read_bytes())
        and seal["nextIntSha256"] == next_int_sha
        and seal["provenance"] == "OriginalRelease57Hz/preview; ExternalUnverified"
        for seal in control["randomSourceSeals"]),
        "Source-derived seed/provider and injected concrete identity seals required.")
    source_maps = [row["sources"] for row in rows]
    require(all(len(source) == 94 and all(hex64(value) for value in source.values()) for source in source_maps)
            and [d.sha(d.canonical(source)) for source in source_maps[:2]] == list(d.PRODUCTION_MAPS),
            "Exact unchanged historical/current production maps required.")
    expected_control = dict(source_maps[1])
    expected_control[d.BODY_PATH] = control["controlFileSha256"]
    require(source_maps[1][d.BODY_PATH] == control["currentFileSha256"]
            and source_maps[2] == expected_control, "No second production/control change permitted.")
    helper_hashes = {name: d.sha((HERE / name).read_bytes()) for name in
                     ("compatibility.py", "default_diagnostic.py", "default_pe.py", "DefaultBudget.cs")}
    require(report["helperSha256"] == helper_hashes, "Actual helper/client source bindings required.")
    driver = d.sha(d.guarded_driver((HERE / "DefaultChecks.cs").read_bytes()))
    require(report["commonGuardedDriverSha256"] == driver
            and report["commonClientSha256"] == helper_hashes["DefaultBudget.cs"],
            "Common actual counting/I/O driver/client required.")
    require(set(report["windows"]) == set(d.LABELS)
            and set(report["images"]) == set(d.LABELS), "Actual B window and actual Main image required.")
    for label, row in zip(d.LABELS, rows):
        summary, window, image = row["summary"], report["windows"][label], report["images"][label]
        require(type(row["compile"]) is type(row["exit"]) is int and row["compile"] == row["exit"] == 0
                and all(type(summary[key]) is int for key in
                        ("hosts", "SourceTick", "rawCalls", "selections", "defaultSeed"))
                and row["diagnostics"] == ""
                and summary["status"] == "passed" and summary["hosts"] == 1200 and summary["SourceTick"] == 1140
                and summary["rawCalls"] == 491 and summary["selections"] == 4 and summary["defaultSeed"] == 1
                and summary["rawVectorFiltered"] is False and summary["originalTargetRandomQualified"] is False,
                "All actual default491/four-selection/unfiltered seed1 windows required.")
        require(all(type(window[key]) is int for key in
                    ("reservation", "constructors", "advances", "hosts", "wakes", "requests", "callbacks", "values",
                     "pure_reserved", "pure_invocations", "pure_cases", "source_matches", "legacy_matches", "unrounded"))
                and window["reservation"] == window["constructors"] == 1 and window["constructed"] is True
                and window["advances"] == window["hosts"] == 1200 and window["wakes"] == 1140
                and window["requests"] == window["callbacks"] == window["values"] == 491
                and window["finished"] is True and window["advance_open"] is False
                and window["host_open"] is False, "Full actual shared counter/transport window required.")
        expected_pure = (8, 8, 6, 8) if label == "B" else (8, 6, 8, 8)
        require(window["pure_reserved"] == window["pure_invocations"] == 8
                and tuple(window[key] for key in ("pure_cases", "source_matches", "legacy_matches", "unrounded")) ==
                expected_pure, "Actual B source correctness and deliberate old-predicate contrast required.")
        require(image["declaringType"] == "DefaultCabinetChecks" and image["method"] == "Main"
                and image["signature"] == "static int32 Main(string[])" and image["documents"] == 97
                and re.fullmatch(r"0x06[0-9a-f]{6}", image["entryPointToken"]) is not None
                and int(image["entryPointToken"][4:], 16) > 0
                and all(hex64(image[key]) for key in ("peSha256", "pdbSha256", "sourceMapSha256")),
                "Actual CLI Main/PDB97-source-document seals required before Game.")
        harness = dict(DefaultChecks=driver, DefaultBudget=helper_hashes["DefaultBudget.cs"],
                       FullKernelInputs=LOADER_SHA256["main40" if label == "A" else "current"])
        require(row["harnessSha256"] == {key + ".cs": value for key, value in harness.items()}
                and report["ownLoaderSha256"][label] == harness["FullKernelInputs"], "Each tree's own loader required.")
        compiled = report["completeCompiledSourceSeals"][label]
        require(len(compiled) == 97 and d.sha(d.canonical(compiled)) == image["sourceMapSha256"],
                "Actual compiled-source/PDB map binding required.")
        relative = {Path(name).relative_to(Path(row["buildRoot"])).as_posix(): value for name, value in compiled.items()}
        require(relative == dict(row["sources"], **row["harnessSha256"]), "No missing/extra/substituted compiled source.")
    tools, inputs = report["completeToolAndReferenceSeals"], report["completeInputSeals"]
    require(tools and inputs and len(inputs) == report["inputFiles"]
            and d.sha(d.canonical(inputs)) == report["inputsSha256"]
            and d.sha(d.canonical(tools)) == report["toolsAndReferencesSha256"],
            "Complete actual tool/reference/input seals required.")
    for path, seal in {**tools, **inputs}.items():
        require(seal["path"] == path and Path(path).is_absolute()
                and type(seal["bytes"]) is int and seal["bytes"] >= 0 and hex64(seal["sha256"]),
                "Canonical actual file seals required.")
    refs = report["referenceFiles"]
    require(len(refs) == len(set(refs)) == 167 and set(refs) <= set(tools)
            and report["compilerPath"] in tools and report["dotnetPath"] in tools,
            "Exact common compiler and167 references required.")
    commands = receipt["commands"]
    require(commands == report["exactCommands"] and len(commands) == 6
            and all(command[:3] == [report["dotnetPath"], report["compilerPath"], "-noconfig"]
                    and len(command) == 4 for command in commands[:3])
            and all(len(command) == 10 and command[0] == report["dotnetPath"] for command in commands[3:]),
            "Exactly3 serial compilations then3 serial actual windows, not a second campaign.")
    require(len({row["buildRoot"] for row in rows}) == 3 and all(
        Path(row["buildRoot"]).is_absolute() and row["runtimeRoot"] == row["buildRoot"]
        and commands[index][3] == "@" + str(Path(row["buildRoot"]) / "compile.rsp")
        and commands[3 + index][1] == str(Path(row["buildRoot"]) / "Default.Checks.dll")
        and commands[3 + index][-1] == row["runtimeRoot"] for index, row in enumerate(rows)),
        "Each actual image must execute once, in its own sealed source/window root.")
    private = Path(rows[0]["buildRoot"]).parent
    require([row["buildRoot"] for row in rows] == [str(private / label) for label in d.LABELS],
            "Single shared source/image/window graph root required.")
    environment = report["baseEnvironment"]
    require(isinstance(environment["PATH"], str) and environment == dict(
        PATH=environment["PATH"], HOME=str(private / "home"), DOTNET_CLI_HOME=str(private / "home"),
        TMPDIR=str(private / "tmp"), TMP=str(private / "tmp"), TEMP=str(private / "tmp"),
        DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
        DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
        MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0"),
        "Exact normal hosted compiler/runtime environment required; no unreviewed policy toggles.")
    require([item["name"] for item in report["inputArgv"]] ==
            ["fixture", "sound", "m1", "impact", "normal", "speech", "hud"], "Exact explicit7 input argv required.")
    input_paths = [item["path"] for item in report["inputArgv"]]
    require(all(command[2:9] == input_paths for command in commands[3:]), "Common original input bytes/argv required.")
    for item in report["inputArgv"]:
        if item["name"] == "m1":
            selected = {path: seal for path, seal in inputs.items() if Path(item["path"]) in Path(path).parents}
            require(selected and item["sha256"] == d.sha(d.canonical(selected)), "M1 generation directory binding required.")
        else:
            require(item == dict(name=item["name"], **inputs[item["path"]]), "Actual input file binding required.")
    comparisons = report["comparisons"]
    require(set(comparisons) == {"AB", "AC", "BC"}
            and all(set(files) == set(d.OUTPUTS) for files in comparisons.values()), "All9 exact comparisons required.")
    require(report["schema"] == "default-three-way-source-contract-comparisons/v2"
            and report["status"] == "comparisons-not-acceptance"
            and report["comparisonBytesUnfiltered"] is True
            and report["originalTargetNumericOrNativeQualification"] is False
            and report["originalWindowsReusedNotRepeated"] is True
            and report["incremental"] == dict(compilations=1, constructors=1, hosts=1200, wakes=1140, rawMaximum=1024)
            and report["prospectiveTotal"] == dict(compilations=3, constructors=3, hosts=3600, wakes=3420, rawMaximum=3072),
            "Explicit unfiltered comparison scope and unchanged finite graph required.")
    variants = {}
    comparison_fields = {"byteIdentical", "leftBytes", "rightBytes", "leftSha256", "rightSha256"}
    for pair, files in comparisons.items():
        for name, result in files.items():
            require(type(result["byteIdentical"]) is bool
                    and all(type(result[side + "Bytes"]) is int
                            and 0 < result[side + "Bytes"] <= d.OUTPUT_CAPS[d.OUTPUTS.index(name)]
                            and hex64(result[side + "Sha256"]) for side in ("left", "right")),
                    "Complete nonempty unfiltered comparisons required.")
            same_seal = (result["leftBytes"], result["leftSha256"]) == (result["rightBytes"], result["rightSha256"])
            require(result["byteIdentical"] == same_seal, "Dishonest byte equality metadata.")
            expected_fields = set(comparison_fields)
            if not result["byteIdentical"]:
                expected_fields.update(("firstByteOffsetZeroBased", "firstField"))
                if name == "default-host.jsonl":
                    expected_fields.update(("firstRowZeroBased", "firstHostOrdinalOneBased",
                                            "leftRowSha256", "rightRowSha256"))
            require(set(result) == expected_fields, "First-difference explanation schema must match byte equality.")
            if not result["byteIdentical"]:
                offset, field = result["firstByteOffsetZeroBased"], result["firstField"]
                require(type(offset) is int and 0 <= offset <= min(result["leftBytes"], result["rightBytes"])
                        and offset < max(result["leftBytes"], result["rightBytes"]),
                        "First-difference byte offset must be within the complete compared files.")
                require(type(field) is str and re.fullmatch(r"\$(?:/(?:[^~]|~[01])*)?", field) is not None,
                        "First-difference field must be a valid rooted, escaped JSON path.")
                if name == "default-host.jsonl":
                    row, host = result["firstRowZeroBased"], result["firstHostOrdinalOneBased"]
                    require(type(row) is type(host) is int and 0 <= row < 1200
                            and host == row + 1 and row <= offset,
                            "First-difference host/row coordinates must describe the complete1200-host window.")
                    require(hex64(result["leftRowSha256"]) and hex64(result["rightRowSha256"])
                            and result["leftRowSha256"] != result["rightRowSha256"],
                            "First-difference row SHA256 seals must identify both distinct rows.")
            for label, side in zip(pair, ("left", "right")):
                seal = (result[side + "Bytes"], result[side + "Sha256"])
                require((label, name) not in variants or variants[label, name] == seal,
                        "Comparison arms must identify the same complete actual files.")
                variants[label, name] = seal
    require(all(item["byteIdentical"] for item in comparisons["AC"].values()),
            "All3 complete historical counterfactual files must match.")
    require(all(comparisons["AB"][name]["byteIdentical"] for name in d.OUTPUTS[1:]),
            "Complete actual current ordered RNG and menu must match unchanged A.")
    ab, bc = (comparisons[pair]["default-host.jsonl"] for pair in ("AB", "BC"))
    if not ab["byteIdentical"]:
        left_field, right_field = ab["firstField"], bc["firstField"]
        # Reversed objects with missing keys on both sides can name different
        # first missing siblings, but cannot disagree about their parent path.
        fields_coherent = left_field == right_field or (
            left_field.rpartition("/")[0] and
            left_field.rpartition("/")[0] == right_field.rpartition("/")[0])
        require(all(ab[key] == bc[key] for key in
                    ("firstByteOffsetZeroBased", "firstRowZeroBased", "firstHostOrdinalOneBased"))
                and ab["leftRowSha256"] == bc["rightRowSha256"]
                and ab["rightRowSha256"] == bc["leftRowSha256"] and fields_coherent,
                "First-difference reversed AB/BC host evidence must be coherent.")
    require(report["controlMatchesBaselineAllThree"] is True
            and report["baselineDifferenceReproduced"] == any(not item["byteIdentical"]
                                                            for item in comparisons["AB"].values()),
            "Truthful complete historical/current outcomes required.")
    require(obligations["legacyCounterfactualProjection"] == comparisons["AC"]
            and obligations["actualCurrentOrderedRandomAndMenu"] ==
            {name: comparisons["AB"][name] for name in d.OUTPUTS[1:]}
            and obligations["actualHistoricalVsCurrentHost"] == comparisons["AB"]["default-host.jsonl"],
            "Honest legacy/current outcomes required; AB host is not asserted equal.")
    for label in d.LABELS:
        shape = obligations["outputShape"][label]
        require(shape["hostRows"] == 1200 and shape["rngRecords"] == 491 and shape["menuRecords"] > 0,
                "Partial or filtered default output is not qualification.")
        require(report["windows"][label]["bytes"] == [variants[label, name][0] for name in d.OUTPUTS],
                "Counter-granted bytes must equal complete files.")


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
    receipt = dict(schema=SCHEMA, base=BASE, tree=TREE,
                   commands=[], runs=[], originalTargetRandomQualified=False,
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
        receipt["runs"] = [evidence["rows"][label] for label in diagnostic.LABELS]
        receipt["threeWayDiagnostic"] = evidence["report"]
        # Every comparison is visible before applying the explicit projection policy.
        (output / "diagnostic.json").write_text(json.dumps(evidence["report"], indent=2) + "\n")
        public = {key: value for key, value in evidence["report"].items()
                  if key not in ("completeInputSeals", "completeToolAndReferenceSeals", "completeCompiledSourceSeals")}
        public["completeMetadataSha256"] = diagnostic.sha(diagnostic.canonical(evidence["report"]))
        print("DEFAULT COMPATIBILITY DIAGNOSTIC " + json.dumps(public, sort_keys=True), flush=True)
        receipt["compatibilityObligations"] = diagnostic.source_correct_projection(
            evidence["blobs"], evidence["report"])
        receipt["status"] = "passed"
        validate_receipt(receipt)
    receipt["seconds"] = round(time.monotonic() - started, 3)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt
