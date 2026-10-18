"""Asset-free guards for the separate source-player workflow; no gameplay proof."""

import ast
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
         ".github/workflows/game-build.yml", ".github/workflows/source-player.yml")
PREPARATION_CASES = (
    "test_relative_explicit_input_roots_are_normalized_without_allowing_traversal",
    "test_real_source_artifact_mutation_is_rejected_by_name",
)
SDK_COMMAND = r"""printf '{"sdk":{"version":"10.0.100","rollForward":"disable"}}\n' > global.json"""
PUBLIC_DOTNET = 'SOURCE_PLAYER_DOTNET="$(command -v dotnet)" bash tools/source-player/dotnet.sh'
COMMANDS = {
    "Select the tested SDK": (SDK_COMMAND,),
    "Guard workflow coverage and resource boundaries": ("python3 -B tools/source-player/test_ci.py",),
    "Compile the actual public host without private inputs": (
        "mkdir -p tools/source-player/.runtime/empty-nuget",
        PUBLIC_DOTNET + ' restore tools/source-player/SourcePlayer.Checks.csproj --source "$PWD/tools/source-player/.runtime/empty-nuget" -p:NuGetAudit=false --nologo',
        PUBLIC_DOTNET + " build tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-restore --nologo",
        PUBLIC_DOTNET + " run --project tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-build -- --free-ball-synthetic",
    ),
    "Test public cabinet input and renderer contracts": (
        "node --test tools/source-player/input.test.mjs tools/source-player/renderer.test.mjs tools/source-player/popped-head.renderer.test.mjs tools/source-player/free-ball.renderer.test.mjs",
    ),
    "Test public preparation failure gates": (
        "python3 -B -m unittest " + " ".join("prepare_test.PreparationChecks." + name for name in PREPARATION_CASES),
    ),
    "Test standalone hosting configuration without a daemon": (
        "docker compose version", "python3 -B tools/source-player/hosting_test.py",
    ),
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


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
    require(re.findall(r"^\s*working-directory: (.+)$", text, re.M) == ["tools/source-player"],
            "Only the two named preparation cases use the public tool directory")
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


class SourcePlayerCI(unittest.TestCase):
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
            (PUBLIC_DOTNET + " run --project tools/source-player/SourcePlayer.Checks.csproj --configuration Release --no-build -- --free-ball-synthetic", "true"),
            (COMMANDS["Test public preparation failure gates"][0],
             "python3 -B -m unittest discover -s tools/source-player -p prepare_test.py"),
            ("python3 -B tools/source-player/hosting_test.py", "true"),
            ("python3 -B tools/source-player/test_ci.py", "true"),
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
        self.assertEqual(4, len(re.findall(r"^test\(", (HERE / "input.test.mjs").read_text(), re.M)))
        renderer = (HERE / "renderer.test.mjs").read_text()
        self.assertEqual(5, len(re.findall(r"^test\(", renderer, re.M)))
        self.assertNotIn("artifacts/", renderer)
        head_renderer = (HERE / "popped-head.renderer.test.mjs").read_text()
        self.assertEqual(2, len(re.findall(r"^test\(", head_renderer, re.M)))
        self.assertNotIn("artifacts/", head_renderer)
        ball_renderer = (HERE / "free-ball.renderer.test.mjs").read_text()
        self.assertEqual(3, len(re.findall(r"^test\(", ball_renderer, re.M)))
        self.assertNotIn("artifacts/", ball_renderer)
        self.assertIn('args[0] == "--free-ball-synthetic"', checks)
        self.assertIn('Compile Include="FreeBallChecks.cs"', (HERE / "SourcePlayer.Checks.csproj").read_text())
        sources = ET.parse(HERE / "ProductionSources.props")
        named = {node.attrib["Include"] for node in sources.iter("Compile")}
        self.assertIn("$(BlitzSourceRoot)Assets/Scripts/Runtime/SourceFreeBallBinding.cs", named)
        self.assertIn("$(BlitzSourceRoot)Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs", named)
        tree = ast.parse((HERE / "prepare_test.py").read_text())
        cases = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "PreparationChecks")
        methods = {node.name: node for node in cases.body if isinstance(node, ast.FunctionDef)}
        for name in PREPARATION_CASES:
            self.assertIn(name, methods)
            self.assertFalse(any(isinstance(node, ast.Attribute) and node.attr == "skipTest"
                                 for node in ast.walk(methods[name])))
        private_case = methods["test_actual_prepared_inputs_have_complete_verified_inventory"]
        self.assertTrue(any(isinstance(node, ast.Attribute) and node.attr == "skipTest" for node in ast.walk(private_case)))
        self.assertIn('"config", "--format", "json"', (HERE / "hosting_test.py").read_text())


if __name__ == "__main__":
    unittest.main()
