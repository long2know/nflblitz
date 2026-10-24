"""Additive static/Python-only diagnostic gates. Never invokes compiler or Game."""
from contextlib import contextmanager, redirect_stdout
import ast
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import types
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUTER_WORKFLOW_SHA256 = "ccf82aba97e84aa168d60fbcebe40402d5efbb1aeabc40b473c93a55addb0697"
INHERITED_WORKFLOW_SHA256 = "94cd73144c84814a79e1ec923b7f0c494009f11f322985b1ecee79d70962faa8"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def workflow_context(projection):
    """Authenticate the live caller bytes, not just Git HEAD or a view hint."""
    def require(value, message):
        if not value:
            raise AssertionError("Default diagnostic workflow context: " + message)
    def read(path):
        require(path.is_file() and path.resolve() == path, "canonical working workflow required")
        return path.read_bytes()
    working_path = ROOT / projection.GAME
    working = read(working_path)
    digest = hashlib.sha256(working).hexdigest()
    if digest == OUTER_WORKFLOW_SHA256:
        outer_root, outer, kind = ROOT, working, "outer"
        inherited = projection.inherited_game(working.decode()).encode()
        require(hashlib.sha256(inherited).hexdigest() == INHERITED_WORKFLOW_SHA256,
                "declared outer projection changed")
    else:
        require(digest == INHERITED_WORKFLOW_SHA256, "unknown or changed working workflow")
        receipt = json.loads((ROOT / ".squad/.scratch/ci-workspace.json").read_bytes())
        outer_root = Path(receipt["source_git_root"])
        git_dir = Path(receipt["source_git_dir"])
        require(outer_root.is_absolute() and outer_root.resolve() == outer_root and outer_root != ROOT,
                "actual outer source root required")
        require(receipt["schema"] == "defensive-switch-ci-workspace/v1"
                and receipt["status"] == "passed" and receipt["inherited_view"] is True
                and receipt["production_and_harness_bytes_unchanged"] is True
                and receipt["input_game_sha256"] == OUTER_WORKFLOW_SHA256
                and receipt["inherited_game_sha256"] == INHERITED_WORKFLOW_SHA256
                and receipt["source_sha256"][projection.GAME] == OUTER_WORKFLOW_SHA256,
                "existing inherited bootstrap receipt required")
        require(receipt["runtime_git_root"] == str(ROOT)
                and receipt["runtime_git_dir"] == str(git_dir)
                and receipt["environment"]["GIT_DIR"] == str(git_dir)
                and receipt["environment"]["GIT_WORK_TREE"] == str(ROOT)
                and os.environ.get("GIT_DIR") == str(git_dir)
                and os.environ.get("GIT_WORK_TREE") == str(ROOT),
                "actual inherited caller exports must match its receipt")
        view = subprocess.run(["git", "rev-parse", "--show-toplevel", "--absolute-git-dir"],
                              cwd=ROOT, capture_output=True, text=True, check=True)
        require(view.stdout.splitlines() == [str(ROOT), str(git_dir)], "actual runtime Git view changed")
        source_view = subprocess.run(["git", "rev-parse", "--show-toplevel", "--absolute-git-dir"],
            cwd=outer_root, env=dict(os.environ, GIT_DIR=str(git_dir), GIT_WORK_TREE=str(outer_root)),
            capture_output=True, text=True, check=True)
        require(source_view.stdout.splitlines() == [str(outer_root), str(git_dir)],
                "actual outer source Git view changed")
        outer = read(outer_root / projection.GAME)
        require(hashlib.sha256(outer).hexdigest() == OUTER_WORKFLOW_SHA256,
                "outer working source bytes changed")
        inherited, kind = working, "inherited"
    require(projection.INHERITED_GAME_SHA256 == INHERITED_WORKFLOW_SHA256,
            "existing inherited workflow pin changed")
    return dict(kind=kind, outer=outer.decode(), inherited=inherited.decode(),
                outerRoot=outer_root, workingPath=working_path)


diagnostic = load("default_diagnostic_static_tests", HERE / "default_diagnostic.py")
compatibility = load("default_compatibility_static_tests", HERE / "compatibility.py")
pe = load("default_pe_static_tests", HERE / "default_pe.py")
REQUIRED_CASES = (
    "test_all_exact_byte_pairs_precede_field_explanation",
    "test_actual_pe_pdb_token_owner_and_source_checksum_seals",
    "test_bad_plan_seed_or_constructor_refuses_before_callback",
    "test_c_matches_a_never_bypasses_final_ab_acceptance",
    "test_client_counter_removal_and_provenance_mutations_are_rejected",
    "test_compiler_diagnostics_are_metadata_only_and_both_streams_bound",
    "test_driver_wrapper_is_exact_reversible_counting_and_io_only",
    "test_existing_normal_core_projection_preserves_frozen_workflow_guard",
    "test_existing_workflow_timeouts_and_commands_are_unchanged",
    "test_full_shared_graph_bounds_and_no_fourth_or_retry",
    "test_host_and_raw_overrun_refuse_before_consumption",
    "test_lexical_unfiltered_json_and_nested_first_field",
    "test_loader_or_production_seal_mismatch_precedes_any_native_work",
    "test_one_historical_fraction_body_only_and_all_other_bytes_current",
    "test_pe_main_requires_real_cli_metadata_not_name_presence",
    "test_real_python_pipeline_compiles_all_three_before_reused_windows",
    "test_real_python_pipeline_failure_stops_before_third_compile_or_any_driver",
)


def production():
    names = set()
    for item in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        names.update(path.relative_to(ROOT).as_posix() for path in
                     ROOT.glob(item.attrib["Include"].replace("$(BlitzSourceRoot)", "")))
    return {name: (ROOT / name).read_bytes() for name in sorted(names)}


def historical_fixture(current):
    old = (b"        private float Fraction()\n        {\n"
           b"            return (Mode & SourceAnimationModes.Streaming) == 0 ? 0f\n"
           b"                : Math.Max(0f, floatingFrame - FrameOrdinal);\n        }\n")
    start, end = diagnostic.method_span(current, b"private float Fraction()")
    return current[:start] + old + current[end:]


def pe_fixture(owner="DefaultCabinetChecks"):
    """Synthetic metadata bytes only; no compiler and no original fixture."""
    import uuid
    def metadata(streams):
        version = b"v4.0.30319\0\0"
        header = b"BSJB" + struct.pack("<HHII", 1, 1, 0, len(version)) + version + struct.pack("<HH", 0, len(streams))
        names = [name.encode() + b"\0" for name in streams]
        size = len(header) + sum(8 + ((len(name) + 3) & ~3) for name in names)
        body = bytearray()
        for (_, data), name in zip(streams.items(), names):
            while (size + len(body)) % 4:
                body.append(0)
            header += struct.pack("<II", size + len(body), len(data)) + name + b"\0" * (-len(name) % 4)
            body.extend(data)
        return header + body
    def tables(rows):
        header = struct.pack("<IBBBBQQ", 0, 2, 0, 0, 1, sum(1 << i for i in rows), 0)
        return header + b"".join(struct.pack("<I", len(rows[i])) for i in sorted(rows)) + \
            b"".join(row for i in sorted(rows) for row in rows[i])
    strings = bytearray(b"\0")
    def name(value):
        start = len(strings)
        strings.extend(value.encode() + b"\0")
        return start
    module, module_type, target, main, assembly = [name(value) for value in
        ("Default.Checks.dll", "<Module>", owner, "Main", "Default.Checks")]
    name("DefaultCabinetChecks")
    rows = {
        0: [struct.pack("<HHHHH", 0, module, 1, 0, 0)],
        2: [struct.pack("<IHHHHH", 0x100180, value, 0, 0, 1, 1) for value in (module_type, target)],
        6: [struct.pack("<IHHHHH", 0x2080, 0, 0x91, main, 1, 1)],
        8: [struct.pack("<HHH", 0, 1, 0)],
        32: [struct.pack("<IHHHHIHHH", 0x8004, 1, 0, 0, 0, 0, 0, assembly, 0)],
    }
    md = metadata({"#~": tables(rows), "#Strings": bytes(strings), "#GUID": b"\x11" * 16,
                   "#Blob": b"\0\5\0\1\x08\x1d\x0e"})
    source = "/synthetic/DefaultChecks.cs"
    checksum = hashlib.sha256(b"fixed synthetic source").hexdigest()
    blobs = bytearray(b"\0")
    def blob(value):
        start = len(blobs)
        self_size = len(value)
        assert self_size < 128
        blobs.extend(bytes([self_size]) + value)
        return start
    segments = [blob(value.encode()) for value in source.split("/")]
    document = blob(b"/" + bytes(segments))
    digest = blob(bytes.fromhex(checksum))
    identity = b"\x12" * 16 + struct.pack("<I", 0x12345678)
    pdb = metadata({"#Pdb": identity + struct.pack("<IQI", 0x06000001, 1 << 6, 1),
                    "#~": tables({48: [struct.pack("<HHHH", document, 1, digest, 2)],
                                  49: [struct.pack("<HH", 1, 0)]}),
                    "#Blob": bytes(blobs),
                    "#GUID": uuid.UUID("8829d00f-11b8-4213-878b-770e8597ac16").bytes_le
                    + uuid.UUID("3f5162f8-07c6-11d3-9053-00c04fa302a1").bytes_le})
    pos = pe.Metadata(pdb).locations["#Pdb"][0]
    pdb_hash = hashlib.sha256(pdb[:pos] + b"\0" * 20 + pdb[pos + 20:]).digest()
    data = bytearray(0x1200)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3c, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", data, 0x84, 0x14c, 1, 0, 0, 0, 224, 0x102)
    struct.pack_into("<H", data, 0x98, 0x10b)
    struct.pack_into("<I", data, 0x98 + 92, 16)
    struct.pack_into("<II", data, 0x98 + 96 + 14 * 8, 0x2000, 72)
    struct.pack_into("<II", data, 0x98 + 96 + 6 * 8, 0x20a0, 56)
    data[0x178:0x180] = b".text\0\0\0"
    struct.pack_into("<IIII", data, 0x180, 0x1000, 0x2000, 0x1000, 0x200)
    struct.pack_into("<I", data, 0x178 + 36, 0x60000020)
    struct.pack_into("<IHHIIII", data, 0x200, 72, 2, 5, 0x2200, len(md), 1, 0x06000001)
    data[0x280:0x283] = b"\x0a\x16\x2a"
    codeview = b"RSDS" + identity[:16] + struct.pack("<I", 1) + b"fixed.pdb\0"
    struct.pack_into("<IIHHIIII", data, 0x2a0, 0, 0x12345678, 0x100, 0x504d, 2, len(codeview), 0x20e0, 0x2e0)
    data[0x2e0:0x2e0 + len(codeview)] = codeview
    debug = b"SHA256\0" + pdb_hash
    struct.pack_into("<IIHHIIII", data, 0x2bc, 0, 0, 1, 0, 19, len(debug), 0x2130, 0x330)
    data[0x330:0x330 + len(debug)] = debug
    data[0x400:0x400 + len(md)] = md
    return bytes(data), pdb, {source: checksum}


def mock_generate(case, output, different=False, bad_loader=False, bad_production=False):
    source = production()
    historical = historical_fixture(source[diagnostic.BODY_PATH])
    original_loader = b"static-test-own-historical-loader"
    current_loader = (ROOT / compatibility.FIXTURE_LOADER).read_bytes()
    expected_loaders = {"main40": diagnostic.sha(original_loader), "current": diagnostic.sha(current_loader)}
    baseline = {**source, compatibility.FIXTURE_LOADER: original_loader + (b"changed" if bad_loader else b"")}
    source_hash = diagnostic.sha(diagnostic.canonical({name: diagnostic.sha(data) for name, data in source.items()}))
    args = type("Args", (), {name: output / name for name in
                            ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")})()
    calls = []
    summary = dict(status="passed", hosts=1200, SourceTick=1140, rawCalls=491, selections=4,
                   defaultSeed=1, rawVectorFiltered=False, originalTargetRandomQualified=False)
    common = (HERE / "DefaultChecks.cs").read_bytes()
    @contextmanager
    def execute(args, variants, loaders, compiler, references, driver, client, provenance):
        case.assertTrue(loaders == {"A": original_loader, "B": current_loader, "C": current_loader})
        case.assertTrue(driver == diagnostic.guarded_driver(common))
        case.assertTrue(variants["A"] == variants["B"] == source)
        changed = [name for name in source if variants["C"][name] != source[name]]
        case.assertTrue(changed == [diagnostic.BODY_PATH])
        case.assertTrue(provenance["oldBodySha256"] == diagnostic.OLD_BODY)
        calls.extend(("compile-A", "compile-B", "compile-C", "execute-A", "execute-B", "execute-C"))
        blobs = {label: {name: b'{"fixed":"baseline"}\n' for name in diagnostic.OUTPUTS}
                 for label in diagnostic.LABELS}
        if different:
            blobs["B"]["default-host.jsonl"] = b'{"fixed":"do-not-log-this-state-value"}\n'
        rows = {label: dict(name=name, compile=0, diagnostics="", exit=0, summary=dict(summary),
                           harnessSha256={"DefaultChecks.cs": diagnostic.sha(driver),
                                          "DefaultBudget.cs": diagnostic.sha(client),
                                          "FullKernelInputs.cs": diagnostic.sha(loaders[label])},
                           sources={name: diagnostic.sha(data) for name, data in variants[label].items()})
                for label, name in zip(diagnostic.LABELS, diagnostic.NAMES)}
        yield dict(rows=rows, commands=[], report=diagnostic.all_comparisons(blobs), blobs=blobs)
    buffer = io.StringIO()
    with patch.object(compatibility, "source_baseline", return_value=baseline) as selected, \
         patch.object(compatibility, "LOADER_SHA256", expected_loaders), \
         patch.object(compatibility, "diagnostic_module", return_value=diagnostic), \
         patch.object(diagnostic, "PRODUCTION_MAPS", (source_hash, "0" * 64 if bad_production else source_hash)), \
         patch.object(diagnostic, "historical_fraction", return_value=(historical, "fixed-public-blob")), \
         patch.object(diagnostic, "execute_three", side_effect=execute), redirect_stdout(buffer):
        if different:
            with case.assertRaisesRegex(AssertionError, "^Untriggered actual Core/ordered RNG/HUD bytes changed: default-host.jsonl$"):
                compatibility.generate(args, output, source, output / "compiler.dll", [])
            receipt = None
        else:
            receipt = compatibility.generate(args, output, source, output / "compiler.dll", [])
    selected.assert_called_once_with(sorted([*source, compatibility.FIXTURE_LOADER]))
    return receipt, calls, buffer.getvalue()


class DefaultDiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.output = ROOT / ".squad/.scratch/default-diagnostic-static-tests" / uuid.uuid4().hex
        self.output.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.output)

    def graph(self):
        graph = diagnostic.Graph()
        for label in diagnostic.LABELS:
            graph.compile_claim(label)
        return graph

    def initialized(self, graph, label):
        graph.driver_claim(label)
        for operation in ("construct", "stream 1", "constructed 0 0"):
            graph.line(label + " " + operation + "\n")

    def host(self, graph, label, index):
        before = str(index) + " " + str(index * 57 // 60)
        after = str(index + 1) + " " + str((index + 1) * 57 // 60)
        for operation, clocks in (("advance", before), ("host", before), ("advanced", after)):
            graph.line(label + " " + operation + " " + clocks + "\n")

    def test_one_historical_fraction_body_only_and_all_other_bytes_current(self):
        current = (ROOT / diagnostic.BODY_PATH).read_bytes()
        historical = historical_fixture(current)
        control = diagnostic.one_body_control(current, historical)
        self.assertTrue(diagnostic.sha(control) == "e1a7c82ff278bd9c5b997f3c895756fd32dc242282e05d508ee80e31fdfce223")
        with self.assertRaisesRegex(AssertionError, "Fraction body pins"):
            diagnostic.one_body_control(current, historical.replace(b"Math.Max(0f,", b"Math.Max(1f,", 1))
        with self.assertRaisesRegex(AssertionError, "Fraction body pins"):
            diagnostic.one_body_control(current.replace(b"Math.Abs(fraction) < 0.05f", b"Math.Abs(fraction) < 0.06f", 1), historical)

    def test_driver_wrapper_is_exact_reversible_counting_and_io_only(self):
        original = (HERE / "DefaultChecks.cs").read_bytes()
        result = diagnostic.guarded_driver(original)
        self.assertTrue(result.count(b"inputs.Create(") == result.count(b"budget.Construct(") == 1)
        self.assertTrue(result.count(b"bridge.AdvanceSource(") == result.count(b"budget.BeforeAdvance(") == 1)
        self.assertTrue(b"host < 1200" in result and b"randomFactory: budget.CreateRandom" in result)
        with self.assertRaisesRegex(AssertionError, "exact actual DefaultChecks"):
            diagnostic.guarded_driver(original.replace(b"host < 1200", b"host < 1201", 1))

    def test_client_counter_removal_and_provenance_mutations_are_rejected(self):
        client = (HERE / "DefaultBudget.cs").read_bytes()
        diagnostic.verify_random_source(production(), client)
        for before, after in ((b'"OriginalRelease57Hz/preview"', b'"changed"'),
                              (b"1664525u", b"1664526u"), (b"0x9E3779B9u", b"0x9E3779B8u"),
                              (b'Call("raw");', b""), (b'Call("construct");', b"")):
            with self.assertRaises(AssertionError):
                diagnostic.verify_random_source(production(), client.replace(before, after, 1))
        for before, after in ((b'Call("construct");', b"return factory();"),
                              (b'Call("raw");', b"int value = NextInt(16777216);"),
                              (b'owner.Call("write", id, count);', b"inner.Write(buffer, offset, count);")):
            self.assertTrue(client.count(before) == 1 and client.index(before) < client.index(after))

    def test_full_shared_graph_bounds_and_no_fourth_or_retry(self):
        graph = self.graph()
        for label in diagnostic.LABELS:
            self.initialized(graph, label)
            for index in range(1200):
                self.host(graph, label, index)
            graph.line(label + " finish 0 0 0\n")
        self.assertTrue(graph.drivers == ["A", "B", "C"])
        self.assertTrue(sum(w.constructors for w in graph.windows.values()) == 3)
        self.assertTrue(sum(w.hosts for w in graph.windows.values()) == 3600)
        self.assertTrue(sum(w.wakes for w in graph.windows.values()) == 3420)
        with self.assertRaises(AssertionError):
            graph.driver_claim("D")
        with self.assertRaises(AssertionError):
            graph.driver_claim("A")

    def test_bad_plan_seed_or_constructor_refuses_before_callback(self):
        for labels in (("A", "B"), ("A", "B", "C", "D"), ("A", "A", "C")):
            with self.assertRaises(AssertionError):
                diagnostic.Graph(labels)
        graph = diagnostic.Graph()
        with self.assertRaises(AssertionError):
            graph.driver_claim("A")
        graph = self.graph()
        graph.driver_claim("A")
        graph.line("A construct\n")
        with self.assertRaises(AssertionError):
            graph.line("A stream 2\n")
        graph = self.graph()
        self.initialized(graph, "A")
        with self.assertRaises(AssertionError):
            graph.line("A construct\n")

    def test_host_and_raw_overrun_refuse_before_consumption(self):
        graph = self.graph()
        graph.driver_claim("A")
        graph.line("A construct\n")
        graph.line("A stream 1\n")
        consumed = []
        for index in range(1024):
            graph.line("A raw\n")
            consumed.append("fixed-non-RNG-marker")
            graph.line("A value\n")
        with self.assertRaises(AssertionError):
            graph.line("A raw\n")
            consumed.append("must-not-consume")
        self.assertTrue(len(consumed) == 1024)
        graph = self.graph()
        self.initialized(graph, "A")
        for index in range(1200):
            self.host(graph, "A", index)
        with self.assertRaises(AssertionError):
            graph.line("A advance 1200 1140\n")
        graph = self.graph()
        self.initialized(graph, "A")
        w = graph.windows["A"]
        w.hosts, w.advances, w.wakes, w.advance_open = 1201, 1202, 1140, True
        with self.assertRaisesRegex(AssertionError, "source-wake cap"):
            graph.line("A host 1201 1140\n")

    def test_all_exact_byte_pairs_precede_field_explanation(self):
        blobs = {label: {name: b'{"same":1}\n' for name in diagnostic.OUTPUTS} for label in diagnostic.LABELS}
        observed = []
        actual = diagnostic.comparison
        def compare(left, right, jsonl, explain=True):
            if explain:
                self.assertTrue(observed.count(False) == 9)
            observed.append(explain)
            return actual(left, right, jsonl, explain)
        with patch.object(diagnostic, "comparison", side_effect=compare):
            diagnostic.all_comparisons(blobs)
        self.assertTrue(observed == [False] * 9 + [True] * 9)

    def test_actual_pe_pdb_token_owner_and_source_checksum_seals(self):
        image, symbols, sources = pe_fixture()
        proof = pe.verify(image, symbols, sources)
        self.assertTrue(proof["entryPointToken"] == "0x06000001" and proof["documents"] == 1)
        with self.assertRaisesRegex(ValueError, "entry-type"):
            pe.verify(*pe_fixture("WrongProgram"))
        with self.assertRaisesRegex(ValueError, "pdb-source-documents"):
            pe.verify(image, symbols, {name: "00" * 32 for name in sources})
    def test_lexical_unfiltered_json_and_nested_first_field(self):
        for left, right, field in ((b'{"x":1}', b'{"x":1.0}', "$/x"),
                                   (b'{"x":1}', b'{"x":"1"}', "$/x"),
                                   (b'{"x":1}', b'{ "x":1}', "$/@json-encoding")):
            result = diagnostic.comparison(left, right, False)
            self.assertFalse(result["byteIdentical"])
            self.assertTrue(result["firstField"] == field)
        result = diagnostic.comparison(b'{"Players":[{"X":"old-value"}]}\n',
                                       b'{"Players":[{"X":"do-not-log-this-state-value"}]}\n', True)
        self.assertTrue(result["firstField"] == "$/Players/0/X")
        self.assertNotIn("do-not-log-this-state-value", json.dumps(result))
        self.assertTrue(result["firstHostOrdinalOneBased"] == 1)

    def test_c_matches_a_never_bypasses_final_ab_acceptance(self):
        receipt, calls, text = mock_generate(self, self.output / "different", True)
        self.assertIsNone(receipt)
        self.assertTrue(calls == ["compile-A", "compile-B", "compile-C", "execute-A", "execute-B", "execute-C"])
        report = json.loads((self.output / "different/diagnostic.json").read_text())
        self.assertTrue(report["controlMatchesBaselineAllThree"] and report["baselineDifferenceReproduced"])
        self.assertIn("DEFAULT COMPATIBILITY DIAGNOSTIC ", text)
        self.assertNotIn("do-not-log-this-state-value", text)

    def test_loader_or_production_seal_mismatch_precedes_any_native_work(self):
        with self.assertRaisesRegex(AssertionError, "own authenticated fixture loader"):
            mock_generate(self, self.output / "bad-loader", bad_loader=True)
        with self.assertRaisesRegex(AssertionError, "production behavior must remain unchanged"):
            mock_generate(self, self.output / "bad-production", bad_production=True)
        self.assertTrue(compatibility.BASE == "e3157c42df9a5f09d534f689339495429e778768")
        self.assertTrue(compatibility.TREE == "7793a6257537fb165e97f7316cc8e6d97b2e72fe")
        prepared = self.output / "prepared"
        prepared.mkdir()
        args = types.SimpleNamespace(fixture=prepared / "fixture.json", m1=self.output / "m1")
        with patch.object(diagnostic.os, "walk", return_value=[(str(prepared), [], [".env"])]), \
             patch.object(diagnostic, "file_seal", side_effect=AssertionError("secret must not be read")):
            with self.assertRaisesRegex(AssertionError, "secret-shaped input path refused"):
                diagnostic.input_seals(args)

    def test_compiler_diagnostics_are_metadata_only_and_both_streams_bound(self):
        for exit_code, stdout, stderr in ((1, b"", b""), (1, b"private compiler value", b"private stderr"),
                                           (0, b"unexpected stdout", b""), (0, b"", b"unexpected stderr")):
            result = dict(exit=exit_code, diagnostics={
                "stdout": dict(path="owned/stdout", bytes=len(stdout), sha256=diagnostic.sha(stdout)),
                "stderr": dict(path="owned/stderr", bytes=len(stderr), sha256=diagnostic.sha(stderr))})
            with self.assertRaises(AssertionError) as error:
                diagnostic.child_succeeded(result, "A", True)
            self.assertTrue(str(exit_code) in str(error.exception) and "sha256" in str(error.exception))
            self.assertNotIn("private compiler value", str(error.exception))
            self.assertNotIn("private stderr", str(error.exception))

    def test_pe_main_requires_real_cli_metadata_not_name_presence(self):
        with self.assertRaises(ValueError):
            pe.verify(b"DefaultCabinetChecks.Main", b"portable PDB", {})

    def pipeline(self, fail=None):
        case = self
        events = []
        parent = self.output / ("pipeline-" + (fail or "success"))
        class SyntheticWorkspace(diagnostic.Workspace):
            def __init__(self):
                self.root = parent
                self.root.mkdir()
                self.initial, self.static_bytes = self.root.lstat(), 0
            def verify_dir(self, path):
                # Synthetic files only, not a claim that local FUSE is private.
                case.assertFalse(path.is_symlink())
        class SyntheticCounter:
            def __init__(self, workspace, graph, label):
                self.graph, self.label, self.path = graph, label, workspace.root / "authority.sock"
        args = types.SimpleNamespace(**{name: self.output / name for name in
                                      ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")})
        input_map = {str(getattr(args, name).absolute()): dict(path=str(getattr(args, name).absolute()),
                                                             bytes=1, sha256="0" * 64)
                     for name in ("fixture", "sound", "impact", "normal", "speech", "hud")}
        sources = {"Assets/Fixed" + str(i) + ".cs": b"// synthetic public test source\n" for i in range(94)}
        def verify(a, b, source_map):
            self.assertTrue(len(source_map) == 97)
            return dict(peSha256=diagnostic.sha(a), pdbSha256=diagnostic.sha(b), documents=97,
                        declaringType="DefaultCabinetChecks", method="Main")
        def native(command, directory, env, timeout, counter=None):
            label = counter.label if counter else directory.name
            phase = "execute" if counter else "compile"
            events.append(phase + "-" + label)
            if phase == "compile":
                self.assertTrue(not any(item.startswith("execute") for item in events))
                if label == fail:
                    return dict(exit=1, diagnostics={name: dict(bytes=0, path="synthetic", sha256="0" * 64)
                                                     for name in ("stdout", "stderr")})
                (directory / "Default.Checks.dll").write_bytes(b"synthetic PE bytes, never executed")
                (directory / "Default.Checks.pdb").write_bytes(b"synthetic PDB bytes")
                return dict(exit=0, diagnostics={name: dict(bytes=0, path="synthetic", sha256="0" * 64)
                                                 for name in ("stdout", "stderr")})
            self.assertTrue(events[:3] == ["compile-A", "compile-B", "compile-C"])
            w = counter.graph.windows[label]
            w.reservation = w.constructors = 1
            w.constructed = w.finished = True
            w.advances = w.hosts = 1200
            w.wakes, w.callbacks, w.values, w.requests = 1140, 491, 491, 491
            counter.graph.active = None
            target = Path(command[-1])
            for index, name in enumerate(diagnostic.OUTPUTS):
                data = b'{"synthetic":"same"}\n'
                (target / name).write_bytes(data)
                w.bytes[index] = len(data)
            summary = dict(status="passed", hosts=1200, SourceTick=1140, rawCalls=491, selections=4,
                           defaultSeed=1, rawVectorFiltered=False, originalTargetRandomQualified=False)
            log = directory / "child-stdout.log"
            log.write_text(json.dumps(summary))
            return dict(exit=0, diagnostics={
                "stdout": dict(path=str(log), bytes=log.stat().st_size, sha256=diagnostic.sha(log.read_bytes())),
                "stderr": dict(path="synthetic", bytes=0, sha256="0" * 64)})
        with patch.object(diagnostic, "Workspace", SyntheticWorkspace), \
             patch.object(diagnostic, "Counter", SyntheticCounter), \
             patch.object(diagnostic, "native", side_effect=native), \
             patch.object(diagnostic, "pe_module", return_value=types.SimpleNamespace(verify=verify)), \
             patch.object(diagnostic.shutil, "which", return_value=str(self.output / "synthetic-dotnet")), \
             patch.object(diagnostic, "tool_seals", return_value={}), \
             patch.object(diagnostic, "input_seals", return_value=input_map), \
             patch.object(diagnostic.stat, "S_IMODE", return_value=0o600):
            if fail:
                with self.assertRaisesRegex(AssertionError, "compiler exit/diagnostics"):
                    with diagnostic.execute_three(args, {label: sources for label in diagnostic.LABELS},
                            {label: b"synthetic loader" for label in diagnostic.LABELS}, self.output / "compiler.dll",
                            [self.output / (str(i) + ".dll") for i in range(167)],
                            b"synthetic driver", b"synthetic client", {}):
                        self.fail("failed compile yielded evidence")
            else:
                with diagnostic.execute_three(args, {label: sources for label in diagnostic.LABELS},
                        {label: b"synthetic loader" for label in diagnostic.LABELS}, self.output / "compiler.dll",
                        [self.output / (str(i) + ".dll") for i in range(167)],
                        b"synthetic driver", b"synthetic client", {}) as result:
                    self.assertTrue(result["report"]["prospectiveTotal"]["constructors"] == 3)
                    self.assertTrue(len(result["rows"]) == 3)
        self.assertFalse(parent.exists())
        return events

    def test_real_python_pipeline_compiles_all_three_before_reused_windows(self):
        self.assertTrue(self.pipeline() ==
                        ["compile-A", "compile-B", "compile-C", "execute-A", "execute-B", "execute-C"])

    def test_real_python_pipeline_failure_stops_before_third_compile_or_any_driver(self):
        self.assertTrue(self.pipeline("B") == ["compile-A", "compile-B"])

    def test_existing_workflow_timeouts_and_commands_are_unchanged(self):
        projection = load("default_diagnostic_budget_projection",
                          ROOT / "tests/defensive-presnap-switch/test_ci.py")
        context = workflow_context(projection)
        workflow = context["outer"]
        self.assertTrue(workflow.count("timeout-minutes: 90") == 1 and workflow.count("timeout-minutes: 40") == 1)
        player = (context["outerRoot"] / projection.PLAYER).read_text()
        projection.inherited_player(player)
        self.assertTrue(player.count("timeout-minutes: 10") == 1)
        self.assertTrue("tests/selection-countdown/test_ci.py --authority" in workflow)
        self.assertTrue("tests/selection-countdown/run.py --fixture" in workflow)
        self.assertNotIn("default_diagnostic.py", workflow)
        tree = ast.parse((HERE / "default_diagnostic.py").read_text())
        calls = [node for node in ast.walk(tree) if isinstance(node, ast.Call)
                 and isinstance(node.func, ast.Attribute) and node.func.attr == "Popen"]
        self.assertTrue(len(calls) == 1)
        options = {item.arg for item in calls[0].keywords}
        self.assertTrue("umask" in options and "preexec_fn" not in options)
        self.assertNotIn("RLIMIT_FSIZE", (HERE / "default_diagnostic.py").read_text().replace(
            'hostBoundary="ordinary pinned hosted-job permissions; no new Landlock/RLIMIT_FSIZE/W^X policy"', ""))
        actual = sorted(unittest.defaultTestLoader.getTestCaseNames(DefaultDiagnosticTests))
        self.assertTrue(actual == sorted(REQUIRED_CASES))

    def test_existing_normal_core_projection_preserves_frozen_workflow_guard(self):
        projection = load("default_diagnostic_existing_projection",
                          ROOT / "tests/defensive-presnap-switch/test_ci.py")
        selection = load("default_diagnostic_inherited_selection", HERE / "test_ci.py")
        with patch.object(projection, "inherited_game", wraps=projection.inherited_game) as transform:
            context = workflow_context(projection)
        self.assertEqual(transform.call_count, 1 if context["kind"] == "outer" else 0)
        text = context["inherited"]
        selection.validate_workflow(text)
        self.assertTrue(diagnostic.sha(text.encode()) == projection.INHERITED_GAME_SHA256)
        self.assertTrue(selection.BASE_WORKFLOW_SHA256 ==
                        "1016fb32a3a24b2f020c1cd75bb8e348dd92c789bf70378f85afffd992681e06")
        # Mutate actual complete input bytes; no hand-authored workflow or repin.
        real_read = Path.read_bytes
        for label, path, before, after in (
            ("working", context["workingPath"], b"timeout-minutes:", b"timeout-minutes: "),
            ("outer", context["outerRoot"] / projection.GAME,
             b"timeout-minutes: 90", b"timeout-minutes: 91"),
        ):
            original = real_read(path)
            self.assertIn(before, original)
            changed = original.replace(before, after, 1)
            def mutated_read(candidate):
                return changed if candidate == path else real_read(candidate)
            with self.subTest(tampered=label), patch.object(Path, "read_bytes", mutated_read):
                with self.assertRaisesRegex(AssertionError, "workflow context"):
                    workflow_context(projection)
        if context["kind"] == "outer":
            bad = text.replace("timeout-minutes: 40", "timeout-minutes: 41", 1)
            with patch.object(projection, "inherited_game", return_value=bad):
                with self.assertRaisesRegex(AssertionError, "projection changed"):
                    workflow_context(projection)
        else:
            with patch.object(projection, "inherited_game", side_effect=AssertionError("projected twice")):
                self.assertEqual(workflow_context(projection)["inherited"], text)
            with patch.dict(os.environ, GIT_WORK_TREE=str(context["outerRoot"])):
                with self.assertRaisesRegex(AssertionError, "caller exports"):
                    workflow_context(projection)
            receipt_path = ROOT / ".squad/.scratch/ci-workspace.json"
            receipt = json.loads(real_read(receipt_path))
            receipt["input_game_sha256"] = "0" * 64
            changed_receipt = json.dumps(receipt).encode()
            with patch.object(Path, "read_bytes", lambda path:
                              changed_receipt if path == receipt_path else real_read(path)):
                with self.assertRaisesRegex(AssertionError, "bootstrap receipt"):
                    workflow_context(projection)


def required_suite():
    actual = sorted(unittest.defaultTestLoader.getTestCaseNames(DefaultDiagnosticTests))
    if actual != sorted(REQUIRED_CASES):
        raise AssertionError("Exact additive default-diagnostic test inventory changed.")
    return unittest.defaultTestLoader.loadTestsFromTestCase(DefaultDiagnosticTests)


if __name__ == "__main__":
    result = unittest.TextTestRunner(verbosity=1).run(required_suite())
    raise SystemExit(not result.wasSuccessful())
