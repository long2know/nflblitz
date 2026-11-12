"""Asset-free guards for the separate source-player workflow; no gameplay proof."""

import ast
import importlib.util
import json
from pathlib import Path
import re
import shlex
import unittest
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
WORKFLOW = ROOT / ".github/workflows/source-player.yml"
GAME_BUILD = ROOT / ".github/workflows/game-build.yml"
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
NODE = "actions/setup-node@249970729cb0ef3589644e2896645e5dc5ba9c38"
DOTNET = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68"
PATHS = ("Assets/**", "Dockerfile", "tools/source-player/**",
         ".github/workflows/game-build.yml", ".github/workflows/source-player.yml",
         "tests/defensive-presnap-switch/**", "tools/animation/**", "tools/audio/**",
         "tests/parity/**", "tests/movement-callers/**", "tools/rosters/c_source.py",
         "docs/build.md")
PREPARATION_CASES = (
    "test_relative_explicit_input_roots_are_normalized_without_allowing_traversal",
    "test_real_source_artifact_mutation_is_rejected_by_name",
    "test_camera_extension_preserves_every_inherited_pin_without_format_migration",
    "test_camera_extension_rejects_wrong_manifest_camera_and_changed_old_asset",
)
PRESNAP_PREPARATION_CASES = (
    "test_presnap_default_changes_only_existing_station_layout_type",
    "test_build_rejects_historical_data_under_new_default_before_publish",
    "test_bundle_persist_default_remains_historical_and_new_profile_is_explicit",
)
SDK_COMMAND = r"""printf '{"sdk":{"version":"10.0.100","rollForward":"disable"}}\n' > global.json"""
SELECTED_VENUE_RENDERER_CASES = (
    "selected source turf/night/Eagles uses its uploaded field, stadium and endzone bindings, not the historic Bears world",
    "selected material receiver covers every original field/stadium index and active team prefix without a default alias",
    "selected venue rejects a mismatched world packet, material, texture hash and absent resource instead of falling back",
    "authenticated runtime venue groups switch at new-match epochs without uploading replacement resources",
    "actual newly selected managed venue uploads and packet agree at the production renderer boundary",
)
CALLER_DRAIN_INPUT_CASES = (
    "explicit MessageChannel caller drains retained600ms under100ms frames with zero new elapsed and no input or RNG replay",
    "pause focus restart and dispose invalidate stale drain tasks without clearing a newer generation",
    "caller trace timestamps fresh render packet spans and never upgrades a stale or absent packet to live state",
    "drain failure preserves the original error and prevents rescheduling rather than hiding audio failures",
)
HISTORICAL_INPUT_CASES = (
    "observed host clock retains real active intervals, excludes explicit suspension, and rejects rewinds",
    "all four browser cabinets retain the exact production A/B/C keyboard bindings",
    "standard controller sends actual axes and source buttons without JavaScript gameplay",
    "ambiguous gamepads fail explicitly, and releasing keys produces a neutral source packet",
    "all four configured cabinets clear held controls on blur and resume without replaying them",
    "observed capture preserves every complete raw sample and uses measured host time, with legacy fallback unchanged",
)
PUBLIC_DOTNET = 'SOURCE_PLAYER_DOTNET="$(command -v dotnet)" bash tools/source-player/dotnet.sh'
COMMANDS = {
    "Select the tested SDK": (SDK_COMMAND,),
    "Guard workflow coverage and resource boundaries": ("python3 -B tools/source-player/test_ci.py",),
    "Guard the enabled pre-snap profile and CI failure boundaries": (
        "python3 -B tests/defensive-presnap-switch/test_ci.py",
        "python3 -B tests/defensive-presnap-switch/test_runner.py -q",
        "python3 -B tests/defensive-presnap-switch/test_recording.py -q",
        "python3 -B tools/animation/tests/test_prepare_presnap_switch_profile.py MetadataChecks -q",
    ),
    "Compile the actual public host without private inputs": (
        "mkdir -p tools/source-player/.runtime/empty-nuget",
        PUBLIC_DOTNET + ' restore tools/source-player/SourcePlayer.Checks.csproj --source "$PWD/tools/source-player/.runtime/empty-nuget" -p:NuGetAudit=false --nologo',
        PUBLIC_DOTNET + " build tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-restore --nologo",
        PUBLIC_DOTNET + " run --project tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-build -- --free-ball-synthetic",
    ),
    "Test public cabinet input and renderer contracts": (
        "node --test tools/source-player/input.test.mjs tools/source-player/renderer.test.mjs tools/source-player/popped-head.renderer.test.mjs tools/source-player/free-ball.renderer.test.mjs tools/source-player/ball-trails.renderer.test.mjs tools/source-player/dust.renderer.test.mjs tools/source-player/helmet.renderer.test.mjs",
    ),
    "Test original camera GPU pause and four-station evidence guards": (
        "node --test tools/source-player/normal-camera.browser.test.mjs",
    ),
    "Test public preparation failure gates": (
        "python3 -B -m unittest " + " ".join("prepare_test.PreparationChecks." + name for name in PREPARATION_CASES),
    ),
    "Test explicit new profile preparation without original assets": (
        "python3 -B -m unittest " + " ".join("prepare_test.PreparationChecks." + name
                                           for name in PRESNAP_PREPARATION_CASES),
    ),
    "Test standalone hosting configuration without a daemon": (
        "docker compose version", "python3 -B tools/source-player/hosting_test.py",
    ),
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def validate_input_inventory(source):
    names = re.findall(r"^test\('([^'\n]+)',", source, re.M)
    require(len(re.findall(r"^test\(", source, re.M)) == 10 and
            sorted(names) == sorted(HISTORICAL_INPUT_CASES + CALLER_DRAIN_INPUT_CASES),
            "tools/source-player/input.test.mjs: exact historical six plus caller-drain four inventory required")


def step_commands(text):
    steps = {}
    for block in re.split(r"^      - name: ", text, flags=re.M)[1:]:
        name, _, body = block.partition("\n")
        run = re.search(r"^        run: ([^\n]+)$", body, re.M)
        require(run is not None, "Required stage must execute its actual checks: " + name)
        lines = re.findall(r"^          (.*)$", body[run.end():], re.M) if run[1] == "|" else [run[1]]
        require(name not in steps, "Duplicate stage must not hide missing checks: " + name)
        steps[name] = [shlex.split(line, comments=True) for line in lines if line.strip()]
    return steps


def validate_workflow(text):
    for event in ("pull_request", "push"):
        block = re.search(r"^  " + event + r":\n(.*?)(?=^  [a-z_]+:|\Z)", text, re.M | re.S)
        require(block is not None, "Missing event: " + event)
        require(re.findall(r"^      - '([^']+)'$", block[1], re.M) == list(PATHS),
                "Browser-only, workflow-self and dependency changes must run: " + event)
    require("    branches: [main]\n" in text, "Retain bounded main-push coverage")
    permissions = re.search(r"^permissions:\n(.*?)(?=^\S|\Z)", text, re.M | re.S)
    require(permissions is not None and permissions[1].strip() == "contents: read",
            "Only read-only checkout permission is needed")
    require(len(re.findall(r"^\s*permissions:", text, re.M)) == 1, "No elevated job permissions")
    require(re.findall(r"^  ([\w-]+):\n    name:", text, re.M) == ["asset-free"], "One focused job only")
    require("    runs-on: ubuntu-24.04\n" in text and "    timeout-minutes: 10\n" in text,
            "Keep the existing runner pattern and bounded ten-minute budget")
    require(not re.search(r"^\s*(if|continue-on-error|services|container):", text, re.M),
            "Checks cannot be skipped, allowed green on failure, or start services")
    require(re.findall(r"^      - uses: (\S+)", text, re.M) == [CHECKOUT, NODE, DOTNET],
            "Only the three existing official SHA-pinned setup actions are allowed")
    for block in (
        CHECKOUT + " # v7\n        with:\n          persist-credentials: false",
        NODE + " # v6\n        with:\n          node-version: '24.11.1'",
        DOTNET + " # v6\n        with:\n          dotnet-version: '10.0.100'",
    ):
        require(block in text and block in GAME_BUILD.read_text(), "Setup must match game-build: " + block)
    expected = {name: [shlex.split(command) for command in commands] for name, commands in COMMANDS.items()}
    require(step_commands(text) == expected, "Run every actual asset-free command, not echo/skip/substitutes")
    require(len(re.findall(r"^\s*(?:- )?run:", text, re.M)) == len(COMMANDS), "No unguarded extra shell stages")
    require(re.findall(r"^\s*working-directory: (.+)$", text, re.M) == ["tools/source-player"] * 2,
            "Only the named preparation cases use the public tool directory")
    preparation = text.split("      - name: Test public preparation failure gates\n", 1)[1].split("      - name:", 1)[0]
    require("        working-directory: tools/source-player\n" in preparation, "Use the actual preparation test module")
    require("          DOCKER_HOST: unix://${{ github.workspace }}/.source-player-ci-no-daemon.sock\n" in text,
            "Hosting tests must use only the parser, never the live Docker daemon")
    for forbidden in (
        "secrets.", "github.token", "GH_TOKEN", "GITHUB_TOKEN", "token:", "upload-artifact",
        "/data/", "/tmp", ".squad", "8082", "32843", "artifacts/", "tools/source-player/data",
        ".runtime/dotnet", "setup-sdk.py", "SourcePlayer.Browser.csproj", "build.py", "prepare.py",
        "assertions.test.mjs", "browser-smoke.mjs", "loader.browser.test.mjs", "--require-portable",
        "--fetch", "--download", "workload install", "docker pull", "compose up", "apt-get", "pip install",
    ):
        require(forbidden not in text, "Asset-free CI must not consume credentials/private/live inputs: " + forbidden)
    spec = importlib.util.spec_from_file_location("source_player_defensive_ci",
        ROOT / "tests/defensive-presnap-switch/test_ci.py")
    defensive = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(defensive)
    defensive.validate_workflows(GAME_BUILD.read_text(), text)


def validate_source_union_memberships(parity, checks, renderer):
    inventories = {}
    for artifact, text, required in (
        ("tests/parity/ParityChecks.csproj", parity, ("StreamChecks.cs", "SourceAffineChecks.cs")),
        ("tools/source-player/SourcePlayer.Checks.csproj", checks,
         ("ArmRenderingChecks.cs", "AffinePacketChecks.cs", "DefaultAudiblesChecks.cs")),
    ):
        members = [node.attrib["Include"] for node in ET.fromstring(text).iter("Compile")]
        for member in required:
            require(members.count(member) == 1,
                    f"{artifact}: {member} must occur exactly once; actual inventory={members!r}")
        inventories[artifact] = members
    references = [node.attrib["Include"] for node in ET.fromstring(checks).iter("ProjectReference")]
    require(references == ["SourcePlayer.Core.csproj"],
            "tools/source-player/SourcePlayer.Checks.csproj: local Core ProjectReference "
            f"inventory must be ['SourcePlayer.Core.csproj']; actual inventory={references!r}")
    expected = (
        "light observation preserves actual state, omits only camera history/immutable inventories, and cannot mutate live nested objects",
        "original skin draws retain zero-alpha ARMHND2 RGB for every model, team and arm segment",
        "opaque player correction preserves explicit source nodraw exclusions and selected joint matrices",
        "source-affine packet retains full non-TRS matrices through present, uniforms and torso numbers",
        "adapter-only: actual effects receiver preserves selected originals and raw0x400, rejecting substitutes",
        "adapter-only: disabled effects never issue a particle draw",
        "adapter-only: actual fire draw binds selected frames, camera vertices and separate source-alpha blends",
        "adapter-only: original-atlas HUD batches split for additive192 highlight and explicitly restore base",
        "actual linked shader bodies retain source flash/additive HUD and camera-space FX consumers",
        "adapter-only: actual world receiver uploads supplied source raster triangles, holds clocks and reports missing originals without drawing substitutes",
        "adapter-only: world packet rejects wrong source linkage, material, resource substitution, size and invented fog admission",
        "adapter-only: supplied original-shadow resources draw exact raster linkage with source alpha, separate blends and disclosed portable depth conversion",
        "actual world composition retains field/table materials and source field2 pass after actors, without changing old independent profiles",
        "actual managed world envelope is consumed unchanged when an explicit production packet receipt is supplied",
    )
    names = re.findall(r"^test\('([^'\n]+)',", renderer, re.M)
    declarations = len(re.findall(r"^test\(", renderer, re.M))
    extended = expected + SELECTED_VENUE_RENDERER_CASES
    require(declarations == 19 and sorted(names) == sorted(extended),
            "tools/source-player/renderer.test.mjs: expected historical fourteen plus exact selected-venue five inventory="
            f"{extended!r}; actual inventory={names!r}; declarations={declarations}")
    require(len(re.findall(r"^function playerScene\(", renderer, re.M)) == 1,
            "tools/source-player/renderer.test.mjs: playerScene helper inventory must contain exactly one")
    inventories["tools/source-player/renderer.test.mjs"] = names
    inventories["local_core_project_references"] = references
    return inventories


class SourcePlayerCI(unittest.TestCase):
    def test_explicit_caller_drain_cases_cannot_be_removed_or_skipped(self):
        source = (HERE / "input.test.mjs").read_text()
        validate_input_inventory(source)
        for name in CALLER_DRAIN_INPUT_CASES:
            declaration = "test('" + name + "',"
            self.assertEqual(source.count(declaration), 1)
            with self.subTest(case=name), self.assertRaisesRegex(AssertionError, "caller-drain four inventory"):
                validate_input_inventory(source.replace(declaration, "test.skip('" + name + "',", 1))

    def test_explicit_venue_inventory_rejects_skipping_either_actual_case(self):
        parity = (ROOT / "tests/parity/ParityChecks.csproj").read_text()
        checks = (HERE / "SourcePlayer.Checks.csproj").read_text()
        renderer = (HERE / "renderer.test.mjs").read_text()
        for name in SELECTED_VENUE_RENDERER_CASES:
            with self.subTest(case=name):
                original = "test('" + name + "',"
                self.assertEqual(renderer.count(original), 1)
                mutated = renderer.replace(original, "test.skip('" + name + "',", 1)
                with self.assertRaisesRegex(AssertionError, "exact selected-venue five inventory"):
                    validate_source_union_memberships(parity, checks, mutated)

    def test_source_union_memberships(self):
        parity = (ROOT / "tests/parity/ParityChecks.csproj").read_text()
        checks = (HERE / "SourcePlayer.Checks.csproj").read_text()
        renderer = (HERE / "renderer.test.mjs").read_text()
        inventories = validate_source_union_memberships(parity, checks, renderer)
        affine = "source-affine packet retains full non-TRS matrices through present, uniforms and torso numbers"
        affine_start = renderer.index("test('" + affine + "',")
        affine_end = renderer.find("\ntest(", affine_start + 1)
        self.assertGreater(affine_end, affine_start)
        mutations = (
            ("tests/parity/ParityChecks.csproj", "StreamChecks.cs", parity,
             parity.replace('    <Compile Include="StreamChecks.cs" />\n', "", 1), checks, renderer),
            ("tools/source-player/SourcePlayer.Checks.csproj", "ArmRenderingChecks.cs", checks,
             parity, checks.replace('    <Compile Include="ArmRenderingChecks.cs" />\n', "", 1), renderer),
            ("tools/source-player/renderer.test.mjs", affine, renderer,
             parity, checks, renderer[:affine_start] + renderer[affine_end + 1:]),
        )
        negative_witnesses = []
        for artifact, member, original, mutated_parity, mutated_checks, mutated_renderer in mutations:
            with self.subTest(artifact=artifact, missing_member=member):
                mutated = {"tests/parity/ParityChecks.csproj": mutated_parity,
                           "tools/source-player/SourcePlayer.Checks.csproj": mutated_checks,
                           "tools/source-player/renderer.test.mjs": mutated_renderer}[artifact]
                self.assertNotEqual(original, mutated, f"{artifact}: mutation must change actual source bytes")
                with self.assertRaises(AssertionError) as rejected:
                    validate_source_union_memberships(mutated_parity, mutated_checks, mutated_renderer)
                diagnostic = str(rejected.exception)
                for required in (artifact, member, "inventory"):
                    self.assertIn(required, diagnostic)
                negative_witnesses.append({"artifact": artifact, "missing_member": member,
                                           "diagnostic": diagnostic})
        self.assertEqual(3, len(negative_witnesses))
        print(json.dumps({"schema": "source-union-membership-regression/v1", "status": "passed",
                          "inventories": inventories, "negative_witnesses": negative_witnesses},
                         sort_keys=True))

    def test_default_audibles_adapters_are_a_separate_unqualified_opt_in(self):
        checks = (HERE / "Checks.cs").read_text()
        self.assertIn('args.Contains("--default-audibles-adapters-only")', checks)
        project = ET.parse(HERE / "SourcePlayer.Checks.csproj")
        self.assertIn("DefaultAudiblesChecks.cs", {node.attrib["Include"] for node in project.iter("Compile")})
        proof = (HERE / "DefaultAudiblesChecks.cs").read_text()
        for actual in ("new PlayerHost(data, sink)", "catalog.RequireAllInputsConsumed()",
                       "new SourceDirectSoundRecordingConsumer()", "ComposeAudibles(normal, viewport)",
                       "coreAudibleProducerOrWorldChangeProven = false", "actualHostDirectDeliveries"):
            self.assertIn(actual, proof)
        for forbidden in ("Game.Model", "BindingFlags", "SourceAction.Audible"):
            self.assertNotIn(forbidden, proof)
        module = ast.parse((HERE / "build.py").read_text())
        profile = next(node for node in ast.walk(module) if isinstance(node, ast.Call) and
                       isinstance(node.func, ast.Attribute) and node.func.attr == "add_argument" and
                       node.args and isinstance(node.args[0], ast.Constant) and node.args[0].value == "--profile")
        options = {keyword.arg: keyword.value for keyword in profile.keywords}
        self.assertEqual(ast.unparse(options["default"]), "PROFILE")
        self.assertEqual(ast.unparse(options["choices"]),
                         "(OLD_PROFILE, PROFILE, AUDIBLES_PROFILE, COMPLETE_PROFILE, "
                         "SOURCE_BANKS_PROFILE, RECORDS_PROFILE, FINAL_PROFILE, PREMATCH_PROFILE, SELECTED_WORLD_PROFILE)")

    def test_actual_workflow_covers_public_commands_sdk_and_triggers(self):
        validate_workflow(WORKFLOW.read_text())

    def test_mutating_actual_filters_commands_sdk_or_resource_boundary_turns_guard_red(self):
        original = WORKFLOW.read_text()
        mutations = (
            ("      - 'tools/source-player/**'\n", ""),
            ("      - '.github/workflows/source-player.yml'\n", ""),
            ("timeout-minutes: 10", "timeout-minutes: 60"),
            ("contents: read", "contents: write"),
            ("persist-credentials: false", "persist-credentials: true"),
            (CHECKOUT, "actions/checkout@v7"),
            ("node-version: '24.11.1'", "node-version: '22'"),
            ("dotnet-version: '10.0.100'", "dotnet-version: '9.0.100'"),
            ('"rollForward":"disable"', '"rollForward":"latestFeature"'),
            (PUBLIC_DOTNET + " build", PUBLIC_DOTNET + " run"),
            ("--no-restore", "--source-root /data/private"),
            ("node --test tools/source-player/input.test.mjs", "echo node --test tools/source-player/input.test.mjs"),
            (" tools/source-player/renderer.test.mjs", ""),
            (" tools/source-player/popped-head.renderer.test.mjs", ""),
            (" tools/source-player/free-ball.renderer.test.mjs", ""),
            (" tools/source-player/ball-trails.renderer.test.mjs", ""),
            (" tools/source-player/dust.renderer.test.mjs", ""),
            (" tools/source-player/helmet.renderer.test.mjs", ""),
            ("node --test tools/source-player/normal-camera.browser.test.mjs", "true"),
            (PUBLIC_DOTNET + " run --project tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-build -- --free-ball-synthetic", "true"),
            (COMMANDS["Test public preparation failure gates"][0],
             "python3 -B -m unittest discover -s tools/source-player -p prepare_test.py"),
            ("python3 -B tools/source-player/hosting_test.py", "true"),
            ("python3 -B tools/source-player/test_ci.py", "true"),
            ("python3 -B tests/defensive-presnap-switch/test_runner.py -q", "true"),
            ("python3 -B tests/defensive-presnap-switch/test_recording.py -q", "true"),
            ("python3 -B tools/animation/tests/test_prepare_presnap_switch_profile.py MetadataChecks -q", "true"),
            (COMMANDS["Test explicit new profile preparation without original assets"][0], "true"),
            ("      - 'tests/defensive-presnap-switch/**'\n", ""),
            ("      - 'tools/rosters/c_source.py'\n", ""),
            (".source-player-ci-no-daemon.sock", "live-docker.sock"),
            ("    steps:\n", "    continue-on-error: true\n    steps:\n"),
            ("    steps:\n", "    env:\n      GH_TOKEN: ${{ github.token }}\n    steps:\n"),
            ("    steps:\n", "    steps:\n      - run: docker compose up -d\n"),
        )
        for old, new in mutations:
            with self.subTest(mutation=old):
                self.assertIn(old, original)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))

    def test_native_compile_and_selected_tests_do_not_relabel_private_coverage(self):
        for name in ("SourcePlayer.Checks.csproj", "SourcePlayer.Core.csproj"):
            project = ET.parse(HERE / name)
            self.assertEqual(["net10.0"], [node.text for node in project.iter("TargetFramework")])
            self.assertFalse(list(project.iter("RuntimeIdentifier")))
            self.assertFalse(list(project.iter("PackageReference")))
        checks = (HERE / "Checks.cs").read_text()
        self.assertIn('throw new ArgumentException("Explicit prepared private data root required.")', checks)
        self.assertIn('File.ReadAllBytes(Path.Combine(root, "manifest.json"))', checks)
        self.assertIn("new PlayerData(catalog)", checks)
        self.assertIn("./artifacts/portable-player/evidence.json", (HERE / "assertions.test.mjs").read_text())
        validate_input_inventory((HERE / "input.test.mjs").read_text())
        renderer = (HERE / "renderer.test.mjs").read_text()
        self.assertEqual(19, len(re.findall(r"^test\(", renderer, re.M)))
        self.assertNotIn("artifacts/", renderer)
        head_renderer = (HERE / "popped-head.renderer.test.mjs").read_text()
        self.assertEqual(2, len(re.findall(r"^test\(", head_renderer, re.M)))
        self.assertNotIn("artifacts/", head_renderer)
        ball_renderer = (HERE / "free-ball.renderer.test.mjs").read_text()
        self.assertEqual(3, len(re.findall(r"^test\(", ball_renderer, re.M)))
        self.assertNotIn("artifacts/", ball_renderer)
        dust_renderer = (HERE / "dust.renderer.test.mjs").read_text()
        self.assertEqual(3, len(re.findall(r"^test\(", dust_renderer, re.M)))
        self.assertNotIn("artifacts/", dust_renderer)
        helmet_renderer = (HERE / "helmet.renderer.test.mjs").read_text()
        self.assertEqual(7, len(re.findall(r"^test\(", helmet_renderer, re.M)))
        self.assertNotIn("artifacts/", helmet_renderer)
        self.assertIn('args[0] == "--free-ball-synthetic"', checks)
        self.assertIn('Compile Include="FreeBallChecks.cs"', (HERE / "SourcePlayer.Checks.csproj").read_text())
        sources = ET.parse(HERE / "ProductionSources.props")
        named = {node.attrib["Include"] for node in sources.iter("Compile")}
        self.assertIn("$(BlitzSourceRoot)Assets/Scripts/Runtime/SourceFreeBallBinding.cs", named)
        self.assertIn("$(BlitzSourceRoot)Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs", named)
        tree = ast.parse((HERE / "prepare_test.py").read_text())
        cases = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "PreparationChecks")
        methods = {node.name: node for node in cases.body if isinstance(node, ast.FunctionDef)}
        for name in PREPARATION_CASES + PRESNAP_PREPARATION_CASES:
            self.assertIn(name, methods)
            self.assertFalse(any(isinstance(node, ast.Attribute) and node.attr == "skipTest"
                                 for node in ast.walk(methods[name])))
        private_case = methods["test_actual_prepared_inputs_have_complete_verified_inventory"]
        self.assertTrue(any(isinstance(node, ast.Attribute) and node.attr == "skipTest" for node in ast.walk(private_case)))
        self.assertIn('"config", "--format", "json"', (HERE / "hosting_test.py").read_text())
        self.assertIn('Compile Include="PreSnapSwitchChecks.cs"', (HERE / "SourcePlayer.Checks.csproj").read_text())
        self.assertIn('args.Contains("--defensive-presnap-switch-only")', checks)


if __name__ == "__main__":
    unittest.main()
