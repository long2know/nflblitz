#!/usr/bin/env python3
"""Closed CI wiring, private reconstruction and artifact guards; never author public input."""
import argparse
import ast
import base64
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import stat
import subprocess
import sys
import time
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.extend(str(ROOT / name) for name in ("tests/parity", "tools/rosters", "tools/assets"))
GAME = ".github/workflows/game-build.yml"
PLAYER = ".github/workflows/source-player.yml"
INHERITED_GAME_SHA256 = "b84fe34092371010178c74be46d8d7081eb4f1c89823ae64d293169e4c051241"
INHERITED_PLAYER_SHA256 = "4c36ba39d722b1395d61d2be55140faeec4db7dfe17d361a94e7c7f846ebaa3c"
SDK_COMMAND = r"""printf '{"sdk":{"version":"10.0.100","rollForward":"disable"}}\n' > global.json"""
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
NODE = "actions/setup-node@249970729cb0ef3589644e2896645e5dc5ba9c38"
DOTNET = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68"
CI = "python3 -B tests/defensive-presnap-switch/test_ci.py"
WORKSPACE = ".squad/.scratch/defensive-switch-ci/new-workspace"
LEGACY_WORKSPACE = ".squad/.scratch/defensive-switch-ci/inherited-workspace"
D = ".squad/.scratch/SourceData/defensive-presnap-switch-ci"
O = ".squad/.scratch/defensive-presnap-switch-ci"
CASE = "portable-cabinet-wide-pass-clearance-presnap-switch-12000"
OLD_CASE = "portable-cabinet-wide-pass-clearance-12000"
POSSESSION_CASE = "portable-cabinet-wide-pass-clearance-possession-12000"
PROFILE = "original-teams-contact282-c2-m1-presnap-switch"
BASE558_SHA256 = "d7f8324d73bdf01c686c9b09b7bca2defe502b7accf58ab8335e59e0fb210134"
PLAYER563_SHA256 = "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6"
PUBLIC_SHA256 = "556ba823b68767e8c6a302d9d59a231fbb457720723a372bf830d57fc8bdd975"
GZIP_SHA256 = "9a4a17c7fb13907c04889202e80a91416d576c9dc1ac5abf744d3d205a88bf40"
RECIPE_SHA256 = "33ad526af04fe567f3f9523510c6f394b939a6496f03167e82a87bedd5416fbf"
FOCUSED_CONTROLS = (
    "strict-counter-boundary", "cyclic-not-nearest", "occupied-skip",
    "ten-loop-unconditional-assignment", "fake-B-external-edge-reinjection",
    "immediate-entry-double-advance", "missing-ordered-randrng10",
    "wrong-binary32-stream-timing", "wrong-authentic-clip",
    "missing-synchronous-resolved-callback",
    "stale-root-origin", "completion-root-refresh", "completion-without-hold",
    "completion-loses-anchor", "interrupted-source-header",
)
GAME_PATH_ADDITIONS = ("tools/source-player/**", ".github/workflows/source-player.yml")
PLAYER_PATH_ADDITIONS = (
    "tests/defensive-presnap-switch/**", "tools/animation/**", "tools/audio/**",
    "tests/parity/**", "tests/movement-callers/**", "tools/rosters/c_source.py",
    "docs/build.md",
)
PLAYER_STEPS = {
    "Guard the enabled pre-snap profile and CI failure boundaries": (
        CI,
        "python3 -B tests/defensive-presnap-switch/test_runner.py -q",
        "python3 -B tests/defensive-presnap-switch/test_recording.py -q",
        "python3 -B tools/animation/tests/test_prepare_presnap_switch_profile.py MetadataChecks -q",
    ),
    "Test explicit new profile preparation without original assets": (
        "python3 -B -m unittest "
        "prepare_test.PreparationChecks.test_presnap_default_changes_only_existing_station_layout_type "
        "prepare_test.PreparationChecks.test_build_rejects_historical_data_under_new_default_before_publish "
        "prepare_test.PreparationChecks.test_bundle_persist_default_remains_historical_and_new_profile_is_explicit",
    ),
}
LEGACY_DEFAULTS = (
    "    defaults:\n      run:\n        working-directory: " + LEGACY_WORKSPACE + "\n"
)
LEGACY_ENV = (
    "      GIT_DIR: ${{ github.workspace }}/.git\n"
    "      GIT_WORK_TREE: ${{ github.workspace }}/" + LEGACY_WORKSPACE + "\n"
)
LEGACY_BOOTSTRAP = (
    "      - name: Verify additive wiring and isolate the inherited guard view\n"
    "        working-directory: .\n"
    "        run: " + CI + " workspace --output " + LEGACY_WORKSPACE + " --inherited\n"
)


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def load(name, relative):
    path = ROOT / relative
    sys.path.insert(0, str(path.parent))
    try:
        spec = importlib.util.spec_from_file_location(name, path)
        module = importlib.util.module_from_spec(spec)
        sys.modules[name] = module
        spec.loader.exec_module(module)
        return module
    finally:
        sys.path.pop(0)


def history_command(root, output):
    return (
        "python3 -B tests/movement-callers/check_replay_generations.py "
        "--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02 "
        f"--fixture-manifest {root}/contact272/fixture.json "
        f"--sound-functions {root}/contact272/audio-functions/sound-functions.safn "
        f"--c2-m1-inputs {root}/m1 --replay-commands {root}/{OLD_CASE}.jsonl "
        f"--output {output} --previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e "
        f"--possession-replay-commands {root}/{POSSESSION_CASE}.jsonl --partition u2-current-fifth"
    )


COMMANDS = {
    "Select the tested SDK": (SDK_COMMAND,),
    "Guard the actual new job and the complete inherited coverage": (
        CI, "python3 -B tools/source-player/test_ci.py",
    ),
    "Resolve the exact SDK API targeting pack only after actual missing assets": (
        f"{CI} ensure-sdk --output {O}/sdk-preflight",
    ),
    "Acquire only the authenticated named original and asset union": (
        f"{CI} acquire --output {D}/acquired",
        f"python3 -B tests/parity/fetch_inputs.py --profile defensive-presnap-switch --output {D}/original --cache {D}/acquired/nfl --download",
    ),
    "Reconstruct the exact legacy and Contact272 fixtures and C2 generations": (
        f"{CI} prepare-kernel --root {D}",
    ),
    "Reconstruct the unchanged complete558 player and explicit563 successor": (
        f"{CI} prepare-player --root {D}",
        f"python3 -B tools/animation/prepare_player_sequences.py --source-root {D}/original --catalog tools/animation/presnap_switch_sequences.json --destination {D}/hop",
        f"python3 -B tools/animation/prepare_presnap_switch_profile.py profile --source-root {D}/original --base-m1-inputs {D}/m1 --hop-bundle {D}/hop --movement-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs --destination {D}/profile",
        f"python3 -B tools/animation/prepare_presnap_switch_profile.py impact --base-catalog {D}/impact-m1/impact-presentation.json --callback-data {D}/profile/c2-callback-data.json --animation-manifest {D}/profile/animation282/export-inventory.json --movement-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs --destination {D}/impact",
        f"python3 -B tools/source-player/prepare_presnap_switch.py --input {D}/player558 --input-manifest-sha256 {BASE558_SHA256} --profile-inputs {D}/profile --impact {D}/impact/impact-presentation.json --settings tools/source-player/match-settings.presnap-switch.json --output {D}/player563",
        f"python3 -B tools/animation/tests/test_prepare_presnap_switch_profile.py --systems-output {D} ActualProducerChecks -q",
    ),
    "Execute actual original C focused VM and raw host with all15 controls": (
        f"python3 -B tests/defensive-presnap-switch/run.py --source-root {D}/original --fixture-manifest {D}/contact272/fixture.json --sound-functions {D}/contact272/audio-functions/sound-functions.safn --old-c2-inputs {D}/old9 --c2-m1-inputs {D}/m1 --presnap-switch-inputs {D}/profile --player-data {D}/player563 --output {O}/focused --sdk-netstandard21 --controls",
    ),
    "Prepare closed saved public inputs without authoring or simulation": tuple(
        f"python3 -B tests/movement-callers/prepare_public_replay.py prepare --case {case} --fixture tests/movement-callers/fixtures/{case}.jsonl.gz --recipe tests/movement-callers/fixtures/{case}.recipe.json --output {D}/{case}.jsonl"
        for case in (OLD_CASE, POSSESSION_CASE, CASE)
    ),
    "Execute only the fresh current U2 fifth legacy recording probe": (
        history_command(D, O + "/current-history"),
    ),
    "Execute normal full enabled-profile replay and all11 causal controls": (
        f"python3 -B tests/movement-callers/run.py --source-root {D}/original --input-profile contact272 --fixture-manifest {D}/contact272/fixture.json --sound-functions {D}/contact272/audio-functions/sound-functions.safn --c2-presnap-switch-inputs {D}/profile --replay-commands {D}/{CASE}.jsonl --output {O}/new-full --source-compatibility portable",
    ),
    "Reject real corrupted result source and saved-recording artifacts": (
        f"{CI} failure-guards --root {D} --results {O} --output {O}/failure-guards.json",
    ),
    "Check actual results immutable inputs and closed recording privacy": (
        f"{CI} summarize --root {D} --results {O} --output {O}/privacy-summary.json",
    ),
}


def render_steps(steps, preparation=False):
    text = ""
    for name, commands in steps.items():
        text += "      - name: " + name + "\n"
        if preparation:
            text += "        working-directory: tools/source-player\n"
        if name in ("Acquire only the authenticated named original and asset union",
                    "Reconstruct the exact legacy and Contact272 fixtures and C2 generations"):
            text += "        env:\n          GH_TOKEN: ${{ github.token }}\n"
        text += "        run: |\n" + "".join("          " + command + "\n" for command in commands)
    return text


def new_job():
    return (
        "  defensive-presnap-switch:\n"
        "    name: Defensive pre-snap original C and full enabled-profile replay\n"
        "    runs-on: ubuntu-24.04\n"
        "    timeout-minutes: 40\n"
        "    defaults:\n      run:\n        working-directory: " + WORKSPACE + "\n"
        "    env:\n"
        "      DOTNET_CLI_TELEMETRY_OPTOUT: '1'\n"
        "      DOTNET_NOLOGO: '1'\n"
        "      PYTHONDONTWRITEBYTECODE: '1'\n"
        "      BLITZ_CI_MUTATION_WORKERS: '1'\n"
        "      GIT_DIR: ${{ github.workspace }}/.git\n"
        "      GIT_WORK_TREE: ${{ github.workspace }}/" + WORKSPACE + "\n"
        "    steps:\n"
        "      - uses: " + CHECKOUT + " # v7\n"
        "        with:\n          persist-credentials: false\n"
        "      - uses: " + NODE + " # v6\n"
        "        with:\n          node-version: '24.11.1'\n"
        "      - uses: " + DOTNET + " # v6\n"
        "        with:\n          dotnet-version: '10.0.100'\n"
        "      - name: Create the frozen private new-job workspace\n"
        "        working-directory: .\n"
        "        run: " + CI + " workspace --output " + WORKSPACE + "\n" +
        render_steps(COMMANDS)
    )


def remove_once(text, addition, replacement=""):
    require(text.count(addition) == 1, "Declared CI wiring missing, duplicated or changed: " + addition[:160])
    return text.replace(addition, replacement, 1)


def inherited_game(text):
    text = remove_once(text, new_job())
    text = remove_once(text, LEGACY_DEFAULTS)
    text = remove_once(text, LEGACY_ENV)
    text = remove_once(text, LEGACY_BOOTSTRAP)
    text = remove_once(text, " --partition archived-four\n", "\n")
    for path in GAME_PATH_ADDITIONS:
        addition = "      - '" + path + "'\n"
        require(text.count(addition) == 2, "Both game path triggers must cover: " + path)
        text = text.replace(addition, "")
    require(sha(text.encode()) == INHERITED_GAME_SHA256,
            "Every inherited Game command/pin/guard/budget must remain byte-identical after ONLY declared CI removal")
    return text


def inherited_player(text):
    for path in PLAYER_PATH_ADDITIONS:
        addition = "      - '" + path + "'\n"
        require(text.count(addition) == 2, "Both player triggers must cover: " + path)
        text = text.replace(addition, "")
    text = remove_once(text, render_steps({next(iter(PLAYER_STEPS)): next(iter(PLAYER_STEPS.values()))}))
    name = tuple(PLAYER_STEPS)[1]
    text = remove_once(text, render_steps({name: PLAYER_STEPS[name]}, preparation=True))
    require(sha(text.encode()) == INHERITED_PLAYER_SHA256,
            "Keep all original player commands/pins and the exact ten-minute budget unchanged")
    return text


def validate_workflows(game, player):
    inherited = inherited_game(game)
    inherited_player(player)
    require(re.findall(r"^  ([\w-]+):\n    name:", game, re.M) ==
            ["core-and-tooling", "defensive-presnap-switch"], "Both independent required Game jobs must exist exactly once")
    require(not re.search(r"^\s*(?:needs|if|continue-on-error|services|container):", game, re.M),
            "Required jobs must be concurrent, unconditional and fail closed")
    require(re.findall(r"^    timeout-minutes: (\d+)$", game, re.M) == ["40", "40"],
            "Both exact40 budgets, never an eighty-minute dependency chain")
    require(re.findall(r"^    timeout-minutes: (\d+)$", player, re.M) == ["10"],
            "The asset-free job retains its exact10 budget")
    for forbidden in ("upload-artifact", "gh release upload", "--author-presnap-switch",
                      "--single-public-attempt", "--no-mutations", "|| true", "/data/", "/tmp"):
        require(forbidden not in game + player, "No bypass, authoring, private path or data upload: " + forbidden)
    generations = load("defensive_ci_generations", "tests/movement-callers/check_replay_generations.py")
    archived = generations.probe_inventory("archived-four")
    current = generations.probe_inventory("u2-current-fifth")
    require(archived + current == generations.probe_inventory() and not set(archived).intersection(current),
            "The two required job partitions must prove the complete nonduplicated five-probe union")
    # Existing guards still authenticate their original literal14 bindings and historical hashes.
    selection = load("defensive_ci_selection", "tests/selection-countdown/test_ci.py")
    selection.validate_workflow(inherited)
    sound = load("defensive_ci_sound", "tests/audio/runtime/test_ci.py")
    sound.validate_workflow(inherited)
    possession = load("defensive_ci_possession", "tests/presentation/possession-services/test_ci.py")
    possession.validate_workflow(inherited)
    return inherited


def new_output(path):
    path = Path(path)
    require(".." not in path.parts, "Output dot-segment aliases are forbidden")
    path = path.absolute()
    require(path.resolve() == path and ROOT / ".squad/.scratch" in path.parents and not path.exists(),
            "A NEW regular ignored worktree-local output is required")
    require(subprocess.run(["git", "check-ignore", "-q", str(path / "receipt.json")],
                           cwd=ROOT, env=dict(os.environ, GIT_WORK_TREE=str(ROOT))).returncode == 0,
            "Private inputs and evidence must remain ignored")
    return path


def code_paths():
    prefixes = ("Assets", "Assets.meta", "tests", "tools", ".github/workflows",
                ".gitignore", ".gitattributes", "Directory.Build.props", "Directory.Build.targets",
                "NuGet.Config", "global.json", "scripts", "hosting", "Dockerfile", "compose.yaml", ".dockerignore",
                "README.md", "docs")
    raw = subprocess.run(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", *prefixes],
                         cwd=ROOT, env=dict(os.environ, GIT_WORK_TREE=str(ROOT)),
                         capture_output=True, check=True).stdout
    names = sorted({name.decode() for name in raw.split(b"\0") if name} | {
        "tests/defensive-presnap-switch/PreSnapSwitch.Checks.csproj",
        "tests/defensive-presnap-switch/PreSnapSwitch.Production.csproj",
    })
    require(names and GAME in names and PLAYER in names and
            "tests/defensive-presnap-switch/test_ci.py" in names, "Actual current code and new CI helper must be copied")
    for name in names:
        path = Path(name)
        require(not path.is_absolute() and ".." not in path.parts and
                not any(part == ".env" or part.startswith(".env.") for part in path.parts),
                "Only explicit nonsecret project code is allowed in the CI workspace")
        target = ROOT / name
        require(target.resolve() == target and stat.S_ISREG(target.stat().st_mode),
                "Workspace code must be actual regular files, never aliases: " + name)
    return names


def workspace(output, inherited=False):
    started = time.monotonic()
    actual_game, actual_player = (ROOT / GAME).read_text(), (ROOT / PLAYER).read_text()
    old_game = validate_workflows(actual_game, actual_player)
    output = new_output(output)
    output.mkdir(parents=True)
    records = {}
    for name in code_paths():
        source = ROOT / name
        raw = source.read_bytes()
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
        target.chmod(stat.S_IMODE(source.stat().st_mode))
        records[name] = sha(raw)
    require(all(sha((ROOT / name).read_bytes()) == pin and sha((output / name).read_bytes()) == pin
                for name, pin in records.items()), "Freeze/copy requires every actual current code byte unchanged")
    if inherited:
        (output / GAME).write_text(old_game)
        require(sha((output / GAME).read_bytes()) == INHERITED_GAME_SHA256,
                "Only the exactly reversible inherited workflow view may differ")
    runtime = output / ".squad/.scratch/ci-runtime"
    for name in ("temp", "home", "nuget"):
        (runtime / name).mkdir(parents=True)
    environment = {
        "TMPDIR": str(runtime / "temp"), "TMP": str(runtime / "temp"), "TEMP": str(runtime / "temp"),
        "DOTNET_CLI_HOME": str(runtime / "home"), "NUGET_PACKAGES": str(runtime / "nuget"),
        "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
        "GIT_WORK_TREE": str(output),
    }
    if not inherited:
        environment["UseSharedCompilation"] = "false"
    if "GITHUB_ENV" in os.environ:
        with Path(os.environ["GITHUB_ENV"]).open("a") as stream:
            stream.write("".join(key + "=" + value + "\n" for key, value in environment.items()))
    receipt = dict(schema="defensive-switch-ci-workspace/v1", status="passed",
                   input_game_sha256=sha(actual_game.encode()), input_player_sha256=sha(actual_player.encode()),
                   inherited_game_sha256=INHERITED_GAME_SHA256, inherited_view=inherited,
                   source_sha256=records, environment=environment,
                   production_and_harness_bytes_unchanged=True, seconds=round(time.monotonic() - started, 3))
    (output / ".squad/.scratch/ci-workspace.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(dict(status="passed", files=len(records), inherited_view=inherited, seconds=receipt["seconds"])))
    return receipt


def run_commands(commands, output):
    output = new_output(output)
    output.mkdir(parents=True)
    rows = []
    for index, command in enumerate(commands):
        started = time.monotonic()
        process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=600)
        raw = (process.stdout + process.stderr).encode()
        (output / (str(index) + ".log")).write_bytes(raw)
        rows.append(dict(command=command, exit=process.returncode, seconds=round(time.monotonic() - started, 3),
                         log_sha256=sha(raw)))
        (output / "commands.json").write_text(json.dumps(rows, indent=2) + "\n")
        require(process.returncode == 0, "Actual pinned producer failed: " + shlex.join(command) +
                "; see " + str(output / (str(index) + ".log")))
    return rows


def validate_targeting_assets(document, online=False):
    frameworks = document.get("project", {}).get("frameworks", {})
    require(set(frameworks) == {"netstandard2.1"}, "The SDK preflight must compile actual netstandard2.1 APIs")
    dependencies = frameworks["netstandard2.1"].get("downloadDependencies", [])
    expected = [{"name": "NETStandard.Library.Ref", "version": "[2.1.0, 2.1.0]"}]
    require(dependencies == expected or not online and dependencies == [],
            "Only the exact implicit NETStandard.Library.Ref2.1.0 targeting pack may be restored")
    require(set(frameworks["netstandard2.1"].get("frameworkReferences", {})) == {"NETStandard.Library"},
            "The real production reference contract must not be relabelled as another framework")


def ensure_sdk(output):
    output = new_output(output)
    output.mkdir(parents=True)
    (output / "empty-feed").mkdir()
    props = ["-p:BaseIntermediateOutputPath=" + str(output.absolute() / "obj") + "/",
             "-p:BaseOutputPath=" + str(output.absolute() / "bin") + "/"]
    project = str(HERE / "PreSnapSwitch.Production.csproj")
    build = ["dotnet", "build", project, "--no-restore", "-t:Rebuild", "-c", "Release", "-v:q", "--nologo",
             "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true", *props]
    commands = []

    def execute(command, label):
        started = time.monotonic()
        process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=240)
        raw = (process.stdout + process.stderr).encode()
        (output / (label + ".log")).write_bytes(raw)
        commands.append(dict(command=command, exit=process.returncode, seconds=round(time.monotonic() - started, 3),
                             log=label + ".log", log_sha256=sha(raw)))
        return process.returncode, raw.decode()

    code, text = execute(build, "first-actual-build")
    online = False
    if code:
        require("error NETSDK1004:" in text and
                all("error NETSDK1004:" in line for line in text.splitlines()
                    if re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+", line)),
                "Only an actual missing-assets build failure permits SDK restore")
        restore = ["dotnet", "restore", project, "--source", str(output.absolute() / "empty-feed"),
                   "-v:q", "-p:NuGetAudit=false", *props]
        code, text = execute(restore, "offline-after-actual-missing-assets")
        if code:
            diagnostics = [line for line in text.splitlines()
                           if re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+", line)]
            require(diagnostics and all("error NU1101: Unable to find package NETStandard.Library.Ref." in line
                                       for line in diagnostics),
                    "Only the actual absent netstandard targeting pack permits the official-feed restore")
            validate_targeting_assets(checked_json(output / "obj/project.assets.json"), True)
            restore[restore.index("--source") + 1] = "https://api.nuget.org/v3/index.json"
            code, text = execute(restore, "official-exact-targeting-pack-after-actual-NU1101")
            online = True
        require(code == 0, "The exact required SDK targeting pack restore failed")
        validate_targeting_assets(checked_json(output / "obj/project.assets.json"), online)
        code, text = execute(build, "identical-fresh-build-after-required-restore")
    require(code == 0 and "0 Warning(s)" in text and "0 Error(s)" in text and
            not re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+", text),
            "The real C#7.3/netstandard2.1 production closure must freshly compile with zero diagnostics")
    receipt = dict(schema="defensive-switch-ci-SDK-preflight/v1", status="passed",
                   target_framework="netstandard2.1", language_version="7.3",
                   exact_implicit_targeting_pack="NETStandard.Library.Ref/[2.1.0, 2.1.0]",
                   official_restore_only_after_actual_missing_pack=online,
                   fresh_final_build_zero_diagnostics=True, commands=commands,
                   production_project_sha256=sha((HERE / "PreSnapSwitch.Production.csproj").read_bytes()),
                   qualifying_assembly_sha256=sha(
                       (output / "bin/Release/netstandard2.1/PreSnapSwitch.Production.dll").read_bytes()))
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({key: value for key, value in receipt.items() if key != "commands"}))
    return receipt


def kernel_commands(root):
    p = lambda value: str(root / value)
    return [
        ["python3", "-B", "tests/parity/prepare_inputs.py", "--source-root", p("original"), "--output", p("kernel")],
        ["python3", "-B", "tests/contact/prepare.py", "--source-root", p("original"), "--operand-root", p("original"),
         "--base-bundle", p("kernel/movement-animation"), "--download"],
        ["python3", "-B", "tools/animation/prepare_contact_profile.py", "prepare", "--source-root", p("original"),
         "--base-bundle", p("kernel/movement-animation"), "--destination", p("animation272")],
        ["python3", "-B", "tools/assets/prepare_cinematics.py", "--source-root", p("original"),
         "--destination", p("impact-base")],
        ["python3", "-B", "tools/animation/prepare_contact_impact.py", "--base-catalog", p("impact-base/impact-presentation.json"),
         "--plyrseq", p("original/plyrseq.c"), "--contact-programs", "Assets/Scripts/Gameplay/SourceAnimationPrograms.Contact.cs",
         "--animation-manifest", p("animation272/export-inventory.json"), "--destination", p("impact272")],
        ["python3", "-B", "tests/parity/prepare_contact_inputs.py", "--profile", "contact272",
         "--legacy-fixture", p("kernel/fixture.json"), "--production-bundle", p("animation272"),
         "--supplemental-bundle", "tests/contact/.artifacts/contact-extra",
         "--impact", p("impact272/impact-presentation.json"), "--output", p("contact272")],
        ["python3", "-B", "tools/animation/prepare_player_sequences.py", "--source-root", p("original"),
         "--catalog", "tools/animation/c2_sequences.json", "--destination", p("extra9")],
        ["python3", "-B", "tools/animation/prepare_c2_profile.py", "--source-root", p("original"),
         "--base-bundle", p("contact272/contact-animation"), "--extra-bundle", p("extra9"), "--destination", p("old9")],
        ["python3", "-B", "tools/animation/prepare_c2_impact.py", "--base-catalog", p("impact272/impact-presentation.json"),
         "--callback-data", p("old9/c2-callback-data.json"), "--plyrseq", p("original/plyrseq.c"),
         "--programs", "Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs", "--destination", p("impact-old9")],
        ["python3", "-B", "tools/animation/prepare_c2_profile.py", "--source-root", p("original"),
         "--base-bundle", p("contact272/contact-animation"), "--extra-bundle", p("extra9"),
         "--profile", "contact281-c2-m1", "--destination", p("m1")],
        ["python3", "-B", "tools/animation/prepare_c2_impact.py", "--base-catalog", p("impact272/impact-presentation.json"),
         "--callback-data", p("m1/c2-callback-data.json"), "--plyrseq", p("original/plyrseq.c"),
         "--programs", "Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs", "--profile", "contact281-c2-m1",
         "--destination", p("impact-m1")],
    ]


def prepare_kernel(root):
    run_commands(kernel_commands(root), root / "kernel-commands")
    inputs = load("defensive_ci_kernel_inputs", "tests/parity/prepare_inputs.py")
    pins = inputs.fixture_hashes(root / "contact272/fixture.json", root / "contact272/audio-functions/sound-functions.safn")
    require(len(pins) == 834 and sha((root / "contact272/fixture.json").read_bytes()) ==
            "bc4e0fefc0f86ced01c733cf208abb8471b77ad35815c4d31c685e0de1538d75",
            "The exact actual834 Contact272 fixture must be reconstructed, not relabelled")


def prepare_player(root):
    p = lambda name: str(root / name)
    commands = [
        ["python3", "-B", "tools/environments/prepare_environment.py", "--source-root", p("acquired/nfl"),
         "--source-commit", "89f840d58d152a2a9f230cb8d0cdffbd554c8ded", "--destination", p("world"),
         "--confirm-local-use", "--authorization-reference", "owner-directed-source-world-2026-10-16"],
        ["python3", "-B", "tools/assets/prepare_teams.py", "--source-root", p("acquired/nfl"),
         "--rosters", p("kernel/rosters.json"), "--destination", p("teams"), "--confirm-local-use",
         "--authorization-reference", "owner-directed-original-team-presentation-2026-10-16",
         "--original-tools-root", p("acquired/midway")],
        ["python3", "-B", "tests/presentation/possession-services/prepare.py", "--original",
         p("acquired/possession"), "--output", p("services")],
        ["python3", "-B", "tools/assets/prepare_popped_head.py", "--original-sdl", p("acquired/nfl/models/phead.sdl"),
         "--calibration-models", p("acquired/nfl/models"), "--output", p("head")],
        ["python3", "-B", "tools/assets/prepare_free_ball.py", "--original-sdl", p("acquired/nfl/models/medball2.sdl"),
         "--calibration-models", p("acquired/nfl/models"), "--output", p("ball")],
        ["python3", "-B", "tools/assets/prepare_normal_camera.py", "--source-root", p("original"),
         "--destination", p("normal-camera.json")],
        ["python3", "-B", "tools/source-player/prepare.py", "--kernel-fixture", p("contact272/fixture.json"),
         "--m1-inputs", p("m1"), "--m1-impact", p("impact-m1/impact-presentation.json"), "--world", p("world"),
         "--world-sha256", "4512de8fddbc8bafcf57befc1f3f8af303cb6d925a966e86ff0d870df1717191",
         "--teams", p("teams"), "--teams-sha256", "c9e875052544c2d7e30d6cdab4bcf9a3b38d552adfa9deef8da5d35ab0ceb62c",
         "--hud", p("services/hud"), "--settings", "tools/source-player/match-settings.four-player.json",
         "--output", p("player518")],
    ]
    rows = run_commands(commands, root / "player-base-commands")
    expected_base = "0089fedfaa78efe48f0d21ad8d75cd9fdbc65dba1e06e31c22075763e1ed486a"
    require(sha((root / "player518/manifest.json").read_bytes()) == expected_base, "Exact original518 base required")
    sequence = [
        ("prepare_head.py", "player518", "player521", ["--head-bundle", p("head"),
         "--head-manifest-sha256", "a0a8293bf77df5974f404f0b96013915b92896058522d4f059b91cd79a50964f"]),
        ("prepare_ball.py", "player521", "player524", ["--ball-bundle", p("ball"),
         "--ball-manifest-sha256", "3d7ab26acab8a968058c6644011e1ac503547bf6ae67705974e3848ee5e00549"]),
    ]
    for index, (script, source, destination, extra) in enumerate(sequence):
        pin = sha((root / source / "manifest.json").read_bytes())
        rows += run_commands([["python3", "-B", "tools/source-player/" + script, "--base-data", p(source),
            "--base-manifest-sha256", pin, *extra, "--output", p(destination)]], root / ("player-extension-" + str(index)))
    append_exact_v3(root / "player524", root / "services/fire", root / "player556")
    require(sha((root / "player556/manifest.json").read_bytes()) ==
            "e39f91af2b3624e2eaf364c129a94bb81f75da3b88c3e3d6138371e54a202f89", "Exact original556 ancestry required")
    run_commands([
        ["python3", "-B", "tools/source-player/prepare_camera.py", "--input", p("player556"),
         "--input-manifest-sha256", "e39f91af2b3624e2eaf364c129a94bb81f75da3b88c3e3d6138371e54a202f89",
         "--camera", p("normal-camera.json"), "--output", p("player557")],
        ["python3", "-B", "tools/source-player/prepare_possession.py", "--input", p("player557"),
         "--input-manifest-sha256", "0a2987fa67792c83b4116aa989ce96ee980eab4b1e30e6354ebf082d5031ac8c",
         "--speech", p("services/speech/possession-speech.bytes"), "--output", p("player558")],
    ], root / "player-final-commands")
    require(sha((root / "player558/manifest.json").read_bytes()) == BASE558_SHA256,
            "The authentic complete558 manifest must match before extending; never change its pin")
    (root / "baseline.json").write_text(json.dumps(dict(producer_source_root=str((root / "original").absolute()),
        base_m1_root=str((root / "m1").absolute()), legacy_base_pins={
            str((root / "player558/kernel/impact-presentation.json").absolute()):
            "2fee43a7a20dad06d8846b1d558a69d8648041368091ab12677c941e19b66cd1"}), indent=2) + "\n")


def append_exact_v3(base, fire, output):
    producer = load("defensive_ci_player_bundle", "tools/source-player/prepare.py")
    source = json.loads(producer.read(base, "manifest.json",
        "678088b79195719e2841d2d3b93599ff4349219b0c27ce5cbbb021d6ff22f380"))
    require(source["profile"] == "original-teams-contact281-c2-m1" and len(source["files"]) == 524 and
            "fire" not in source["entrypoints"], "The exact524 original base is required before adding its v3 files")
    bundle = producer.Bundle()
    for row in source["files"]:
        bundle.add(base, row["path"], row["sha256"])
    definition = bundle.document(fire, "manifest.json",
        "90a134af37fe6d43f13977f7903315ee0d9a7f37f700669b365f45dd7c941d56", "fire/manifest.json")
    require(definition["schema"] == "blitz-source-fire-spfx-portable/v3" and
            len(definition["inputs"]) == 18 and len(definition["dustInputs"]) == 10 and
            definition["helmetInput"]["gitBlobSha1"] == "d086f6944207a0ff2fa906ef7fc9692dcfb289ec" and
            not definition["nativeGeneratedHelmetHeaderEquivalent"], "Only the complete authenticated original v3 input is accepted")
    for row in definition["inputs"] + definition["dustInputs"] + [definition["helmetInput"]]:
        bundle.add(fire, row["payloadPath"], row["sha256"], "fire/" + row["payloadPath"])
    for name in ("mesh", "helmetMesh"):
        bundle.add(fire, definition[name + "Path"], definition[name + "Sha256"], "fire/" + definition[name + "Path"])
    entries, origins = dict(source["entrypoints"]), dict(source["origins"])
    entries["fire"] = "fire/manifest.json"
    origins["fire"] = (definition["inputOrigin"] +
        "; source SDL factor4,17original TRAIL frames,10original DIRT frames,"
        "original PHELM factor1.2/pdecal2 ratio4; native helm.h unqualified; no native TEXUS/WMS claim")
    require(len(bundle.files) == 556, "The original524+32 v3 file union must be complete")
    bundle.persist(output, entries, origins)


# Exact names from the approved558 team catalog. No tree search or asset guessing.
TEAM_SOURCE_PINS = {
    "cambot.c": "00c9c913bc6a697dfd590666bf5597f2c2bc1eb7",
    "img3d/BEAALOD.wms": "7b766d0fbcb5f9c307a2b31e38b8991394854038",
    "img3d/BEAHLOD.wms": "b9b2ad0365c1215de25e8fdbe6329ccd814ebf09",
    "img3d/BENALOD.wms": "0e231ba6e1625d6c0aa2b75d384221ddd9092a94",
    "img3d/BENHLOD.wms": "35cfaaf52c94d33bc64d33abfb728e20924cf870",
    "img3d/BILALOD.wms": "b09b9d296867d34edcf171e654865d24d880d104",
    "img3d/BILHLOD.wms": "4966aedaf283fa22433c837b472e71463ccc521c",
    "img3d/BROALOD.wms": "b365ddea6e12b7e8f18f43728e75cfb820096d58",
    "img3d/BROHLOD.wms": "5ab161c3d48de27a2635512d988ba1492bab31f3",
    "img3d/BRWNUM.TGA": "33a2ad4c7d8c9a19fc4e86a93c8859adb26f983a",
    "img3d/BRWNUM2.TGA": "6a4634ca0e1ee52f024537b078097f51abdd51bf",
    "img3d/BRWjerfa.tga": "dbd0273e7eb0a1a5618111c5ffafe3dc8c95f077",
    "img3d/BRWjerfh.tga": "784ec7bbd3fc7b933e9b299947443c2c6aa1c908",
    "img3d/BRWlghla.tga": "c8607a9eeecf9d8cba3280ee520f9ba2d92dd9cd",
    "img3d/BRWlghlh.tga": "837ff6245e952823b7d15c13770034a7f7109cb4",
    "img3d/BRWsvmka.tga": "6a14ac1765940c8e66ac5f4857956f0e8f2710b3",
    "img3d/BRWsvmkh.tga": "096812057b1755b7e8cb7ef273ad3846bf336e36",
    "img3d/BUCALOD.wms": "2214f927ae2fa5de59cede27e758096835361083",
    "img3d/BUCHLOD.wms": "4757c783250c9a9bc1871f16f646b8d8af218066",
    "img3d/CARALOD.wms": "8764050043f19e1b2d95f79a0063a9be30abcccb",
    "img3d/CARHLOD.wms": "94d01a1e8ae9e3701f13782f2f53308bd4dbeb81",
    "img3d/CHAALOD.wms": "a589c80908dc00d161b84471caca12b1cec39909",
    "img3d/CHAHLOD.wms": "3f86de5ad61bab94c9c09adedafc8225d5498ed7",
    "img3d/CHIALOD.wms": "004d64ef0da44fd7bc7910e319f6de43b3b6ccb0",
    "img3d/CHIHLOD.wms": "7b9594fa30609b2327e9c89770e4cdc64a2144ba",
    "img3d/COLALOD.wms": "79b8859a6e82984ef2a76dbdf0c1a33ef96d51e7",
    "img3d/COLHLOD.wms": "e11ffc1b68560703d88bda9295890fc3b971f3de",
    "img3d/COWALOD.wms": "f07dec6bc71a4cbe416cd299cf7e90b7b911c436",
    "img3d/COWHLOD.wms": "b4420d829c0d8111b3978fd02362a43e5a216285",
    "img3d/DOLALOD.wms": "f937c15b606d44fcf097c24e970caf99ff406c9d",
    "img3d/DOLHLOD.wms": "55c3a03ca49923eeb4c4609de5760ab8553c976e",
    "img3d/EAGALOD.wms": "190f8c286714395dead0f98fe9819a148abca0fb",
    "img3d/EAGHLOD.wms": "6c6001aac5a87fb9218181230f144a43281d7647",
    "img3d/FALALOD.wms": "c8a608d76a537ae30c566b5745aad58b76225def",
    "img3d/FALHLOD.wms": "28e741ac57f24b25d2d44d56bff9e775af13d71d",
    "img3d/FORALOD.wms": "dfb1a808ff65fc082b98a56dd43aa328a3834a3e",
    "img3d/FORHLOD.wms": "73a26c0fdaec2730078928e2c0b8a332eba760d8",
    "img3d/GIAALOD.wms": "c6f14a6b6612ebf02593dfcb35f1b2ebb1be48a3",
    "img3d/GIAHLOD.wms": "4730a09663ebf477ce6cc856ec2be3dfd730f291",
    "img3d/JAGALOD.wms": "9336d5c7fa44158f7b2eaa0e0cabe9bee6787a08",
    "img3d/JAGHLOD.wms": "4d24699b34fc8af87364d7c6a4a0409070783bcd",
    "img3d/JETALOD.wms": "d5d3eb7ea8d214606996b9924fc0aa068de44e51",
    "img3d/JETHLOD.wms": "439a3eae24cca1defbea6f50c13f7745127171ed",
    "img3d/LIOALOD.wms": "7145d1e34dc9be81ff404c9cc05e5ab8469221fc",
    "img3d/LIOHLOD.wms": "e15fbe0dc9e8380790828167d2dd6ce160442342",
    "img3d/PACALOD.wms": "f67ee873f522491d1ed99821566ae97a11afd600",
    "img3d/PACHLOD.wms": "ded7671999ae7020c6371851b5fbb630059af7b1",
    "img3d/PANALOD.wms": "0b63fb1a890488d795da7e47d1ca123b20deb8ce",
    "img3d/PANHLOD.wms": "11f7f8f0bbf3bf631a6e247afe23acd3cc5ab45b",
    "img3d/PATALOD.wms": "408ce0a354ce12a0879be5bdda9c4ac227647d08",
    "img3d/PATHLOD.wms": "56d01bbddad0f5079e6c7cf6b2e423da04998a88",
    "img3d/RAIALOD.wms": "21fe906a50294e44cf06e553d8f4be9ed7d1c811",
    "img3d/RAIHLOD.wms": "624e8fba5a5fd0a31d0bf13ef6f493cea0339815",
    "img3d/RAMALOD.wms": "d1d9ad6903c109f21d1707f89f0b7f5060e115c2",
    "img3d/RAMHLOD.wms": "67ceeff281674912265407d0a64f06e8d61d3cc7",
    "img3d/RAVALOD.wms": "b8948987e15fe7a01a2bc43a6f872fc22a2eadba",
    "img3d/RAVHLOD.wms": "bd18e1a571feae1bd566601dee9a970e3e769b15",
    "img3d/REDALOD.wms": "264d49f701f55442b546a54ac02365c1315c5012",
    "img3d/REDHLOD.wms": "0b68b172d5de693ac4f0b3162f175e9323650359",
    "img3d/SAIALOD.wms": "eab9322629ad48cca3aafd5f2fbe536726afb53c",
    "img3d/SAIHLOD.wms": "9b5aa974e29fe27a297f76b0a9a9c23b1bc40864",
    "img3d/SEAALOD.wms": "3ebc0e0eb4e51ad1be4b0f751f124122b0cd6978",
    "img3d/SEAHLOD.wms": "f16f2086ffbe967cee5941964627b2f54db92ba9",
    "img3d/STEALOD.wms": "c855f67ca6af87e293a8c1f6b26e6754229b68c1",
    "img3d/STEHLOD.wms": "a98c05ee606b236d6f1e51f20adeb0d9c08dd94f",
    "img3d/TITNUM.TGA": "ae1d93aeb3598b37ac037e00d8b8a0f626bb7e71",
    "img3d/TITNUM2.TGA": "c7415af9c5dff142302405a3d94ca1bfa2fa34ce",
    "img3d/TITjerfa.tga": "aeb77ceace636503df947e1a0cc9b9ac4cf95190",
    "img3d/TITjerfh.tga": "c7e873ab90a97f3757564a7194a5734c6f2fce2e",
    "img3d/TITlghla.tga": "8ceb69ffcb63ec7a2f830adfc283387400d7c961",
    "img3d/TITlghlh.tga": "8ceb69ffcb63ec7a2f830adfc283387400d7c961",
    "img3d/TITsvmka.tga": "9b60aa8a2511de1ea2677669937d78b0ca5330af",
    "img3d/TITsvmkh.tga": "575782331565b4ea46aa11c32aa0a6745605e54b",
    "img3d/VIKALOD.wms": "72c79432624e026b9a5c2a148b52f7e6d10fc679",
    "img3d/VIKHLOD.wms": "a170d6ee229c3bcb8169e33d454a15c5b8394458",
    "img3d/armhnd.tga": "0f4af352cb6136519ad499c839b8fb7139d79347",
    "img3d/armhnd2.tga": "a9094eddd3a171f74615fc477617da9f3da60467",
    "img3d/hed_bleb.tga": "eefde949473db3ca1f7206a6a64d80aa49724697",
    "img3d/hed_bran.TGA": "27c0b40314f4427d97ff452e09470e89ffa11396",
    "img3d/hed_dan.TGA": "ff5a1aafcf46918f95145afff34f1c36c24a0e87",
    "img3d/hed_danf.TGA": "014e4777e1c61fae01f54d11c3afca90c5c69247",
    "img3d/hed_dude.TGA": "6419215015e0a2f645da3672c2c6e92572b81175",
    "img3d/hed_jasn.TGA": "1bfd0eb996d02d080e435f10ad827e5ae2612c80",
    "img3d/hed_jeff.TGA": "fb378da9889bc46775bb812531cb43129367e631",
    "img3d/hed_jen.TGA": "2b6f2da7166d049e9340a95ca6057658e7b5cd2f",
    "img3d/hed_jim.TGA": "3138f81fe256703ff438f677452ba91a51cbeb36",
    "img3d/hed_jonc.TGA": "b938de60bc5e7a44a2e45300311b69ea038955b6",
    "img3d/hed_luis.TGA": "8e471b30ead1b66efd9dea86cb9665d2ef484481",
    "img3d/hed_mark.TGA": "bfb33d4e76f9088bdb55c31f4c9e2716778ca605",
    "img3d/hed_mik.TGA": "f08031e55225faf2c33fa32a2890bfb3c84fffa6",
    "img3d/hed_palo.tga": "6c9cefe915094d4c5240be1fae1c984d401be8f8",
    "img3d/hed_radn.TGA": "45cd2e8e8a375d2c00590308743e5743ad767bbc",
    "img3d/hed_root.TGA": "190b40f583a9d28b7a204696a773c34cb3ad8fef",
    "img3d/hed_sal.TGA": "3c265ef341fead4eefd8e3d5a3511e4f16f84132",
    "img3d/hed_shnk.TGA": "e0f0094461d539d52ced4fc2aa3420292588b3f7",
    "img3d/hed_skul.TGA": "fd9f9f83dcd33af605670471c1c827639fe0550a",
    "img3d/hed_thug.TGA": "9ffe8e9a55af960daae2e0f09e0aba2108ada4ce",
    "img3d/hedshu.tga": "f0d83461cc66bc6876fc8d005dae55ec18d07569",
    "img3d/hedshu2.tga": "49397b2648f721bc03184c56328980894b0b637e",
    "img3d/plyr.ld": "e0edf5ba3a672bbfede2029a103b2975ba9768ce",
    "img3d/uni.bat": "ff6230891e9a9968761a898506b243db8f8af52f",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "models/jerseys.h": "be252f3b9da5d8a0ef6b24ee8a2193f94cc7c250",
    "models/plyr001b.h": "2117e607d123061d54e880b0824ccd05546a03ed",
    "models/plyr002b.h": "38e330509c0dad90caa7f9d4a3d9738499242792",
    "models/plyr266.h": "a6f301c3926b12ac20f17c776f5c31a26f22a6e7",
    "models/plyr266f.h": "ef1666378ceb3d834cda1364ee76fca7ad983f47",
    "models/plyr498.h": "bcbc13013ac31b0d60cc42e966a598286b80d95f",
    "models/plyr498f.h": "a2f8bac0cc7890efd885614b5a9b8dea4302ca85",
    "models/skeleton.h": "11250fd1509f09bc5ef7df62bbff6b332f2afc87",
    "models/xheads.h": "17f1daf3f59b7a9a70bb976aeb39f63f4d71b9e4",
    "models/xheads3.h": "c60fb663788f1cd1bff4ae827b6db966f3c46a8c",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
}
WORLD_SOURCE_NAMES = (
    "data2.c", "field.c", "include/id.h", "include/nstadlim.h", "lib/ani3d/pipe3D.c",
    "models/field2.sdl", "models/models.mak", "models/stadpie.sdl",
    "img3d/beabg.TGA", "img3d/beaez1.TGA", "img3d/beaez2.TGA", "img3d/beaez3.TGA",
    "img3d/ads98.TGA", "img3d/blitz98.TGA", "img3d/grass198.TGA", "img3d/grass298.TGA",
    "img3d/grass398.TGA", "img3d/nfl98.TGA", "img3d/nflpa98.TGA", "img3d/nums98.TGA",
    "img3d/post98.TGA", "img3d/roof98.TGA", "img3d/ads_d.TGA", "img3d/blitz_d.TGA",
    "img3d/ceil_d.TGA", "img3d/crowdd_d.TGA", "img3d/crowdl_d.TGA", "img3d/post_d.TGA",
    "img3d/roof_d.TGA", "img3d/skybox_d.TGA", "img3d/trim_d.TGA", "img3d/wall_d.TGA",
)


def acquisition_catalog():
    kernel = load("defensive_ci_fetch", "tests/parity/fetch_inputs.py")
    services = load("defensive_ci_service_fetch", "tests/presentation/possession-services/fetch_inputs.py")
    packer = load("defensive_ci_packer", "tools/assets/original_uniform_packer.py")
    recipe, service_pins = services.catalog()
    pins = {}

    def add(repo, name, expected):
        require(repo in recipe["repositories"] and isinstance(name, str) and name and
                "\\" not in name and ":" not in name and not name.startswith("/") and
                all(part not in ("", ".", "..") for part in name.split("/")), "Closed portable original name required")
        record = pins.setdefault((repo, name), {})
        for field in ("gitBlobSha1", "sha256", "bytes"):
            if field in expected:
                require(field not in record or record[field] == expected[field], "Conflicting source pin: " + name)
                record[field] = expected[field]

    for name, pin in kernel.input_pins("defensive-presnap-switch").items():
        add("nfl", name, dict(gitBlobSha1=pin))
    for (repo, name), pin in service_pins.items():
        add(repo, name, pin)
    for name, pin in TEAM_SOURCE_PINS.items():
        add("nfl", name, dict(gitBlobSha1=pin))
    world = json.loads((ROOT / "tools/environments/environment_inputs.json").read_bytes())
    selected = {record["path"]: record for record in world["inputs"] + world["textures"]}
    for name in WORLD_SOURCE_NAMES:
        add("nfl", name, selected[name])
    for record in world["converterEvidence"]:
        add("midway", record["path"], record)
    for name, pin in packer.PINS.items():
        add("midway", "tools/c_hack/" + name, dict(gitBlobSha1=pin))
    for name, expected in {
        "models/phead.sdl": dict(gitBlobSha1="23410e01bb6db9e5fd9bacdd9440691147046798",
            sha256="3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257"),
        "models/medball2.sdl": dict(gitBlobSha1="ede5c3590475f55011340f2b6bcfb4b51304e1f0",
            sha256="d54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704"),
        "models/plyr266.sdl": dict(sha256="37dfe2afcef46c9ebfa21298486454a6112aaaf40cf38b979925449e53fe9294"),
        "models/plyr498.sdl": dict(sha256="194152a05820fb4f134ff47ca48898e4fc217d0729d74fdbee456fb12ceaf2a3"),
    }.items():
        add("nfl", name, expected)
    require(len(TEAM_SOURCE_PINS) == 114 and len(WORLD_SOURCE_NAMES) == 32,
            "Every approved team and world original must remain named explicitly")
    return recipe, pins, service_pins


def verify_tree(document, expected):
    require(type(document) is dict and document.get("sha") == expected and document.get("truncated") is False and
            type(document.get("tree")) is list, "A complete pinned original Git tree is required")
    entries = {}
    for entry in document["tree"]:
        name = entry["path"]
        require(type(name) is str and name and not name.startswith("/") and
                all(p not in ("", ".", "..") for p in name.split("/")) and name not in entries,
                "Unique portable original tree paths required")
        entries[name] = entry
    directories = {"": expected, **{name: row["sha"] for name, row in entries.items() if row["type"] == "tree"}}
    for directory, digest in directories.items():
        children = []
        for name, row in entries.items():
            parent, _, leaf = name.rpartition("/")
            if parent == directory:
                require(row["type"] in ("blob", "tree", "commit"), "Unsupported original Git entry kind")
                key = leaf.encode() + (b"/" if row["type"] == "tree" else b"")
                children.append((key, str(int(row["mode"])).encode() + b" " + leaf.encode() + b"\0" +
                                 bytes.fromhex(row["sha"])))
        raw = b"".join(payload for _, payload in sorted(children))
        actual = hashlib.sha1(b"tree " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        require(actual == digest, "Reconstructed complete Git tree differs: " + directory)
    return entries


def verify_original(raw, expected, entry, name):
    require(entry.get("type") == "blob" and entry.get("mode") in ("100644", "100755") and
            entry.get("sha") == expected.get("gitBlobSha1", entry.get("sha")),
            "Named original must be the pinned regular source blob: " + name)
    actual = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
    require(actual == entry["sha"] and len(raw) == entry["size"] and
            ("bytes" not in expected or len(raw) == expected["bytes"]) and
            ("sha256" not in expected or sha(raw) == expected["sha256"]),
            "Actual original byte/blob/content identity differs: " + name)
    return dict(bytes=len(raw), gitBlobSha1=actual, sha256=sha(raw))


def acquire(output):
    started = time.monotonic()
    output = new_output(output)
    recipe, pins, service_pins = acquisition_catalog()
    fetch = load("defensive_ci_github", "tests/presentation/possession-services/fetch_inputs.py")
    trees, tree_raw = {}, {}
    for repo, identity in recipe["repositories"].items():
        prefix = "repos/" + identity["name"] + "/git/"
        commit = fetch.github_json(prefix + "commits/" + identity["revision"])
        require(commit["sha"] == identity["revision"] and commit["tree"]["sha"] == identity["tree"],
                "Exact original repository/commit/tree relationship required")
        tree = fetch.github_json(prefix + "trees/" + identity["tree"] + "?recursive=1")
        trees[repo] = verify_tree(tree, identity["tree"])
        tree_raw[repo] = (json.dumps(tree, indent=2) + "\n").encode()
    output.mkdir(parents=True)
    records, downloaded = {}, {}
    for (repo, name), expected in sorted(pins.items()):
        entry = trees[repo].get(name)
        require(entry is not None and entry["type"] == "blob" and entry["mode"] in ("100644", "100755") and
                entry["sha"] == expected.get("gitBlobSha1", entry["sha"]), "Named regular original missing: " + repo + "/" + name)
        key = (repo, entry["sha"])
        if key not in downloaded:
            response = fetch.github_json("repos/" + recipe["repositories"][repo]["name"] + "/git/blobs/" + entry["sha"])
            require(response["sha"] == entry["sha"] and response["encoding"] == "base64" and
                    response["size"] == entry["size"], "Pinned Git blob API identity/length differs")
            downloaded[key] = base64.b64decode(response["content"], validate=False)
        raw = downloaded[key]
        record = verify_original(raw, expected, entry, repo + "/" + name)
        path = output / repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        records[repo + "/" + name] = record
    for repo, raw in tree_raw.items():
        (output / repo / "tree.json").write_bytes(raw)
    subset = output / "possession"
    subset.mkdir()
    service_records = {}
    for (repo, name), expected in sorted(service_pins.items()):
        raw = (output / repo / name).read_bytes()
        service_records[repo + "/" + name] = fetch.verify_bytes(raw, expected, repo + "/" + name)
        path = subset / repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    (subset / "receipt.json").write_text(json.dumps(dict(schema="possession-service-ci-acquisition/v1",
        repositories=recipe["repositories"], files=service_records, downloadedGitBlobs=0,
        localCacheUsed=False, source="authenticated-same-job-closed-union"), indent=2) + "\n")
    fetch.verify_directory(subset)
    receipt = dict(schema="defensive-switch-ci-acquisition/v1", status="passed", repositories=recipe["repositories"],
        files=records, tree_sha256={repo: sha(raw) for repo, raw in tree_raw.items()},
        downloadedGitBlobs=len(downloaded), localCacheUsed=False, privateOriginalsUploaded=False,
        seconds=round(time.monotonic() - started, 3))
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(dict(status="passed", originals=len(records), downloadedGitBlobs=len(downloaded),
                         seconds=receipt["seconds"])))
    return receipt


def checked_json(path):
    path = Path(path)
    require(path.resolve() == path.absolute() and stat.S_ISREG(path.stat().st_mode),
            "Actual regular result/input required: " + str(path))
    result = json.loads(path.read_bytes())
    require(type(result) is dict, "Actual object result required: " + str(path))
    return result


def validate_full(directory, full):
    require(full.get("schema") == "source-C2-M1-complete-actual-factory-replay/v1" and
            full.get("status") == "passed-portable-M1-public-caller-gate" and
            full.get("whole_M1_or_game_or_native_accepted") is False and
            full.get("gameplay_compatibility") == "portable",
            "Actual normal full enabled-profile artifact required, not a status-only or partial result")
    for field in ("original8_controls_retained", "original3_cache_controls_retained",
                  "public_controller_policy_seed_vector_clock_unchanged", "complete_compiled_control_closure_executed",
                  "uninstrumented_and_actual_observer_state_RNG_and_poses_byte_identical",
                  "all3_cache_and8_caller_controls_compiled_and_causal", "M1_public_caller_gate_qualified",
                  "live_production_unchanged"):
        require(full.get(field) is True, "Mandatory actual full gate missing: " + field)
    require(full.get("public_vector_executions") == 6 and
            full.get("production_before") == full.get("production_after") and
            len(full.get("production_before", {})) == 78 and len(full.get("harness_sha256", {})) == 12,
            "The full six-replay/78-source/12-harness generation must be retained")
    canonical = load("defensive_ci_full_canonical", "tests/movement-callers/canonical.py")
    controls = load("defensive_ci_full_controls", "tests/movement-callers/run.py")
    cache = [("cache-control-" + spec[0], spec[-1]) for spec in canonical.CANONICAL_CACHE_MUTANTS]
    callers = [("caller-control-" + spec[0], spec[-1]) for spec in controls.MUTANTS]
    expected = (["uninstrumented", "isolated-observer"] + [name for name, _ in cache] +
                ["controlled-baseline"] + [name for name, _ in callers] +
                ["controlled-final", "existing-caller-physics", "final-uninstrumented"])
    runs = full.get("runs")
    require(len(cache) == 3 and len(callers) == 8 and type(runs) is dict and list(runs) == expected,
            "All original3 cache/eight caller controls, baselines, observer and physics must actually execute")
    for name in expected:
        row = runs[name]
        require(row.get("compiled") is True and row.get("zero_diagnostics") is True,
                "Fresh zero-diagnostic compilation required: " + name)
        build_log = (directory / (name + "-compile.log")).read_text()
        require("0 Warning(s)" in build_log and "0 Error(s)" in build_log and
                not re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+", build_log),
                "Actual final compile log must have zero diagnostics: " + name)
        stdout = (directory / (name + ".log")).read_text()
        summaries = [json.loads(line) for line in stdout.splitlines() if line.startswith('{"schema":')]
        require(summaries == [row.get("summary")], "The actual managed output must bind its summary: " + name)
    for name, cause in cache + callers:
        row = runs[name]
        controls.require_mutant(row["exit_code"], row["summary"], cause)
        require(row.get("named_causal_failure") == row["summary"]["failed_assertion"],
                "Every isolated compiled control must fail its own named cause: " + name)
    for name in ("uninstrumented", "isolated-observer", "final-uninstrumented"):
        row = runs[name]
        receipt_path = directory / name / "m1-replay-result.json"
        actual = checked_json(receipt_path)
        require(actual == row.get("receipt") and sha(receipt_path.read_bytes()) == row.get("replay_receipt_sha256"),
                "Actual full replay receipt differs or is absent: " + name)
        require(canonical.require_m1_replay(row["exit_code"], row["summary"], actual, "portable", True) == "positive" and
                actual.get("replayFileSha256") == PUBLIC_SHA256 and actual.get("preSnapSwitchWitnesses") == 15 and
                actual.get("rSkipFrames") == 59 and actual.get("rawCursor") == 22781 and
                row["summary"].get("mode") == ("presnap-complete-observed" if name == "isolated-observer" else "presnap-complete") and
                row["summary"].get("actual_animation_library_manifest") == canonical.PRESNAP_ANIMATION_SHA and
                row["summary"].get("assertions", 0) > 0,
                "Actual12000/11400 new factory, same-window B, ordered RNG and authentic R_SKIP required: " + name)
    for filename in ("m1-public-replay.jsonl", "m1-all-raw-draws.json", "m1-R_SKIP-real-poses.json"):
        raw = (directory / "uninstrumented" / filename).read_bytes()
        require(raw and all((directory / label / filename).read_bytes() == raw
                            for label in ("isolated-observer", "final-uninstrumented")),
                "Actual uninstrumented/observer/final state-input-RNG-pose byte equality required: " + filename)
    for name in ("controlled-baseline", "controlled-final"):
        controls.require_summary(runs[name]["exit_code"], runs[name]["summary"], "controlled", canonical.PRESNAP_ANIMATION_SHA)
    physics = runs["existing-caller-physics"]
    post = full.get("original_C_post_VM", {})
    require(physics["exit_code"] == 0 and physics["summary"].get("status") == "passed" and
            physics["summary"].get("assertions") == 816 and
            physics["summary"].get("actual_Core_post_VM_physics_cases") == 664 and
            post.get("status") == "passed" and post.get("exit_code") == 0 and
            post.get("actual_Core_post_animation_inputs") == 664 and post.get("exact_original_C_assertions") == 5312 and
            post.get("source_blob") == "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c" and
            post.get("actual_rows_sha256") ==
            sha((directory / "existing-caller-physics/actual-caller-physics.tsv").read_bytes()),
            "Every actual664 Core post-VM input and5312 authenticated original-C checks is mandatory")
    require(len(full.get("inputs_before", {})) == 1124, "Exact834+289+one saved public input closure required")
    for name, expected_sha in full["inputs_before"].items():
        path = Path(name)
        require(path.resolve() == path and stat.S_ISREG(path.stat().st_mode) and sha(path.read_bytes()) == expected_sha,
                "Actual full-gate frozen input changed: " + name)
    return [name for name, _ in cache + callers]


def summarize(root, results, output):
    sdk = checked_json(results / "sdk-preflight/receipt.json")
    require(sdk.get("status") == "passed" and sdk.get("target_framework") == "netstandard2.1" and
            sdk.get("language_version") == "7.3" and sdk.get("fresh_final_build_zero_diagnostics") is True,
            "The actual strict SDK API preflight is required")
    require(sdk.get("production_project_sha256") == sha((HERE / "PreSnapSwitch.Production.csproj").read_bytes()) and
            sdk.get("qualifying_assembly_sha256") == sha(
                (results / "sdk-preflight/bin/Release/netstandard2.1/PreSnapSwitch.Production.dll").read_bytes()) and
            sdk["commands"][-1]["exit"] == 0 and sdk["commands"][-1]["command"][:2] == ["dotnet", "build"],
            "The actual fresh SDK assembly/project and successful real build must bind the SDK receipt")
    sdk_log = results / "sdk-preflight" / sdk["commands"][-1]["log"]
    require(sha(sdk_log.read_bytes()) == sdk["commands"][-1]["log_sha256"] and
            "0 Warning(s)" in sdk_log.read_text() and "0 Error(s)" in sdk_log.read_text(),
            "Actual final SDK compile log must retain zero diagnostics")
    focused = checked_json(results / "focused/result.json")
    require(focused.get("schema") == "defensive-presnap-switch-qualification/v1" and
            focused.get("status") == "passed-focused-not-whole-feature" and
            focused.get("controls_requested") is True and focused.get("source_unchanged") is True and
            focused.get("inputs_unchanged") is True and
            focused.get("actual_factory") == "SourceC2Composition.CreatePreSnapSwitchSimulation",
            "Actual complete original-C/focused/raw-host result required")
    mutations = load("defensive_ci_mutations", "tests/defensive-presnap-switch/mutations.py")
    expected = [row[0] for row in mutations.MUTATIONS + mutations.ROOT_MUTATIONS]
    controls = focused.get("controls")
    require(expected == list(FOCUSED_CONTROLS) and type(controls) is list and
            [row.get("id") for row in controls] == expected and len(expected) == 15 and
            all(row.get("fresh_compile_zero_diagnostics") is True and
                row.get("original_sha256") != row.get("mutated_sha256") and
                str(row.get("named_kill", "")).startswith("PRE_SNAP_ASSERTION: ") for row in controls),
            "Every one of the actual15 compiled named focused causal kills is mandatory")
    for name in ("baseline", "final-baseline"):
        row = focused["runs"][name]
        runner = load("defensive_ci_focused_runner", "tests/defensive-presnap-switch/run.py")
        runner.require_result(row["exit"], row["receipt"])
        require(row.get("zero_diagnostics") is True, "Both freshly compiled focused baselines required")
    require(set(focused["runs"]) == {"baseline", "final-baseline"} |
            {"control-" + name for name in expected}, "Every compiled focused run must have its own actual receipt")
    for spec in mutations.MUTATIONS + mutations.ROOT_MUTATIONS:
        label = "control-" + spec[0]
        row = focused["runs"][label]
        runner.require_result(row["exit"], row["receipt"], spec[4])
        require(row.get("zero_diagnostics") is True and
                checked_json(results / "focused" / label / "focused-result.json") == row["receipt"],
                "Actual per-control compiled failing result required: " + label)
    commands = focused.get("commands", [])
    qualified_builds = []
    for index, row in enumerate(commands):
        command = row.get("command", [])
        if command[:2] != ["dotnet", "build"]:
            require(row.get("diagnostics") == [], "Runtime/restore diagnostics are not successful focused evidence")
            continue
        if row.get("exit") != 0:
            diagnostics = row.get("diagnostics", [])
            require(diagnostics and all("error NETSDK1004:" in line for line in diagnostics) and
                    index + 2 < len(commands) and commands[index + 1]["command"][:2] == ["dotnet", "restore"] and
                    commands[index + 1]["exit"] == 0 and commands[index + 2]["command"] == command and
                    commands[index + 2]["exit"] == 0 and commands[index + 2]["diagnostics"] == [],
                    "Only an actual missing-assets failure followed by restore and identical successful rebuild is allowed")
        else:
            require(row.get("diagnostics") == [], "Qualifying focused builds must have zero diagnostics")
            log = (results / "focused" / row["log"]).read_text()
            require("0 Warning(s)" in log and "0 Error(s)" in log, "Actual zero-diagnostic build log required")
            qualified_builds.append(row)
    require(any("production-netstandard21" in row.get("log", "") and row.get("exit") == 0
                for row in commands) and any(row.get("log") == "actual-raw-host.log" and row.get("exit") == 0
                for row in commands) and len(qualified_builds) == 19,
            "Actual netstandard2.1 API build and real raw PlayerHost must execute with zero diagnostics")
    host = checked_json(results / "focused/raw-host/raw-host-proof.json")
    require(host.get("schema") == "defensive-presnap-switch-raw-host/v1" and host.get("status") == "passed" and
            host.get("assertions", 0) >= 280 and len(host.get("changes", [])) == 2 and
            host.get("builtProfile") == PROFILE and host.get("factory") == focused["actual_factory"] and
            host.get("ManifestSha256") == PLAYER563_SHA256 and host.get("trace"),
            "Actual two-edge raw PlayerHost ownership/pose/sound/stance proof required")
    full = checked_json(results / "new-full/result.json")
    full_controls = validate_full(results / "new-full", full)
    negative = checked_json(results / "failure-guards.json")
    require(negative.get("status") == "passed" and negative.get("publicPreparationCLIRejects") == 2 and
            negative.get("sourceCLIRejects") == 1 and len(negative.get("artifactMutationsRejected", [])) >= 20 and
            negative.get("actualFullResultSha256") == sha((results / "new-full/result.json").read_bytes()),
            "Actual result/source/saved-recording corruption guards must pass, not synthetic workflow fixtures")
    history = checked_json(results / "current-history/result.json")
    generations = load("defensive_ci_summary_generations", "tests/movement-callers/check_replay_generations.py")
    require(history.get("status") == "passed-recording-generation-checks" and
            history.get("partition") == "u2-current-fifth" and history.get("source_harness_inputs_unchanged") is True and
            history.get("required_probe_inventory") == list(generations.ALL_PROBES) and
            history.get("selected_probe_inventory") == list(generations.probe_inventory("u2-current-fifth")) and
            history.get("executed_probe_inventory") == history["selected_probe_inventory"],
            "The actual fresh current U2 fifth historical-boundary probe is mandatory")
    generations.validate_probe_inventory("u2-current-fifth", history.get("probes"))
    replay = load("defensive_ci_public", "tests/movement-callers/prepare_public_replay.py")
    require(sha((ROOT / f"tests/movement-callers/fixtures/{CASE}.jsonl.gz").read_bytes()) == GZIP_SHA256 and
            sha((ROOT / f"tests/movement-callers/fixtures/{CASE}.recipe.json").read_bytes()) == RECIPE_SHA256 and
            sha((root / (CASE + ".jsonl")).read_bytes()) == PUBLIC_SHA256, "Saved public input/recipe identity changed")
    replay.audit_public((root / (CASE + ".jsonl")).read_bytes(), CASE)
    replay.load_recipe(ROOT / f"tests/movement-callers/fixtures/{CASE}.recipe.json", CASE)
    old = checked_json(root / "player558/manifest.json")
    new = checked_json(root / "player563/manifest.json")
    require(sha((root / "player558/manifest.json").read_bytes()) == BASE558_SHA256 and
            sha((root / "player563/manifest.json").read_bytes()) == PLAYER563_SHA256 and
            len(old["files"]) == 558 and len(new["files"]) == 563 and new["profile"] == PROFILE,
            "The exact complete558-to563 actual bundle identity is required")
    pins = {row["path"]: row for row in new["files"]}
    require(all(pins[row["path"]] == row for row in old["files"]), "Every inherited558 file pin must remain unchanged")
    for row in new["files"]:
        path = root / "player563" / row["path"]
        require(path.resolve() == path.absolute() and stat.S_ISREG(path.stat().st_mode) and
                path.stat().st_size == row["bytes"] and sha(path.read_bytes()) == row["sha256"],
                "Missing/corrupt actual private player input: " + row["path"])
    acquisition = checked_json(root / "acquired/receipt.json")
    require(acquisition.get("status") == "passed" and acquisition.get("localCacheUsed") is False and
            acquisition.get("downloadedGitBlobs", 0) > 0 and acquisition.get("privateOriginalsUploaded") is False,
            "Cold authenticated independent original acquisition required")
    summary = dict(schema="defensive-switch-ci-private-summary/v1", status="passed",
        profile=PROFILE, factory=focused["actual_factory"], focused_controls=expected,
        full_controls=full_controls, actual_original_C_cases=664, exact_original_C_checks=5312,
        public_case=CASE, public_sha256=PUBLIC_SHA256, public_recording_input_only=True,
        authoring_windows=0, private_assets_or_observations_uploaded=False,
        current_history_probes=list(history["probes"]),
        result_sha256={name: sha(path.read_bytes()) for name, path in {
            "focused": results / "focused/result.json", "full": results / "new-full/result.json",
            "history": results / "current-history/result.json", "acquisition": root / "acquired/receipt.json"}.items()},
        whole_native_or_hosted_qualified=False)
    require(not output.exists(), "Never replace a prior CI privacy receipt")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary))
    return summary


def failure_guards(root, results, output):
    full_path = results / "new-full/result.json"
    original = full_path.read_bytes()
    full = checked_json(full_path)
    validate_full(results / "new-full", full)
    mutations = []
    for field in ("original8_controls_retained", "original3_cache_controls_retained",
                  "complete_compiled_control_closure_executed",
                  "uninstrumented_and_actual_observer_state_RNG_and_poses_byte_identical",
                  "all3_cache_and8_caller_controls_compiled_and_causal", "M1_public_caller_gate_qualified",
                  "live_production_unchanged"):
        bad = copy.deepcopy(full)
        bad[field] = False
        mutations.append((field, bad))
    for name in full["runs"]:
        if name.startswith(("cache-control-", "caller-control-")):
            bad = copy.deepcopy(full)
            del bad["runs"][name]
            mutations.append(("omit-" + name, bad))
    for field, value in (("public_vector_executions", 1), ("runs", {}), ("original_C_post_VM", {})):
        bad = copy.deepcopy(full)
        bad[field] = value
        mutations.append((field, bad))
    for name in ("uninstrumented", "isolated-observer", "final-uninstrumented"):
        bad = copy.deepcopy(full)
        bad["runs"][name]["receipt"]["factory"] = "REAL SourceC2Composition.CreateM1Simulation"
        mutations.append(("wrong-factory-" + name, bad))
    rejected = []
    for name, bad in mutations:
        try:
            validate_full(results / "new-full", bad)
        except (AssertionError, RuntimeError) as error:
            rejected.append(dict(id=name, cause=str(error)[:220], actual_result_sha256=sha(original)))
        else:
            raise AssertionError("Real full-result mutation was accepted: " + name)
    work = new_output(results / "guard-negatives")
    work.mkdir(parents=True)
    fixture = ROOT / f"tests/movement-callers/fixtures/{CASE}.jsonl.gz"
    recipe = ROOT / f"tests/movement-callers/fixtures/{CASE}.recipe.json"
    packed = bytearray(fixture.read_bytes())
    packed[9] ^= 1
    altered_fixture = work / fixture.name
    altered_fixture.write_bytes(packed)
    altered_recipe = work / recipe.name
    changed_recipe = json.loads(recipe.read_bytes())
    changed_recipe["factory"] = "SourceC2Composition.CreateM1Simulation"
    altered_recipe.write_text(json.dumps(changed_recipe) + "\n")
    commands = []
    for label, supplied_fixture, supplied_recipe, diagnostic in (
        ("changed-gzip", altered_fixture, recipe, "tracked fixture container SHA256 differs"),
        ("changed-recipe", fixture, altered_recipe, "Public replay preparation failed:"),
    ):
        target = work / (label + ".jsonl")
        command = ["python3", "-B", "tests/movement-callers/prepare_public_replay.py", "prepare", "--case", CASE,
                   "--fixture", str(supplied_fixture), "--recipe", str(supplied_recipe), "--output", str(target)]
        process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=60)
        raw = (process.stdout + process.stderr).encode()
        (work / (label + ".log")).write_bytes(raw)
        require(process.returncode == 1 and diagnostic in process.stderr and not target.exists(),
                "Actual closed public preparation must reject its changed artifact before output: " + label)
        commands.append(dict(id=label, command=command, exit=process.returncode, log_sha256=sha(raw),
                             output_created=False))
    source = work / "corrupt-source"
    source.mkdir()
    for name in (*json.loads((HERE / "source-inputs.json").read_bytes())["inputs"], "tree.json"):
        target = source / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((root / "original" / name).read_bytes())
    corrupt = source / "sounds.c"
    corrupt.write_bytes(corrupt.read_bytes() + b"\n")
    command = shlex.split(COMMANDS["Execute actual original C focused VM and raw host with all15 controls"][0])
    command[command.index("--source-root") + 1] = str(source)
    command[command.index("--output") + 1] = str(work / "bad-source-result")
    process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120)
    raw = (process.stdout + process.stderr).encode()
    (work / "changed-original-source.log").write_bytes(raw)
    actual = checked_json(work / "bad-source-result/result.json")
    require(process.returncode == 1 and "Original source blob mismatch: sounds.c" in process.stderr and
            actual.get("status") == "failed" and actual.get("commands") == [] and "original_C" not in actual,
            "Actual focused CLI must reject the corrupted authenticated source before any C/managed execution")
    commands.append(dict(id="changed-original-source", command=command, exit=process.returncode,
                         log_sha256=sha(raw), managed_or_C_commands_executed=0))
    shutil.rmtree(source)
    require(full_path.read_bytes() == original and sha(fixture.read_bytes()) == GZIP_SHA256 and
            sha(recipe.read_bytes()) == RECIPE_SHA256, "Negative controls cannot alter real frozen results or saved inputs")
    receipt = dict(schema="defensive-switch-ci-actual-artifact-negatives/v1", status="passed",
        actualFullResultSha256=sha(original), artifactMutationsRejected=rejected, commands=commands,
        publicPreparationCLIRejects=2, sourceCLIRejects=1, publicAuthoringWindows=0,
        qualifyingNamedRuntimeControlsReplaced=False)
    require(not output.exists(), "Preserve earlier actual negative-control receipts")
    output.write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(dict(status="passed", actual_artifact_mutations=len(rejected), actual_cli_rejections=len(commands))))
    return receipt


class DefensiveCI(unittest.TestCase):
    def test_actual_workflows_have_exact_required_parallel_job_and_reversible_old_hashes(self):
        inherited = validate_workflows((ROOT / GAME).read_text(), (ROOT / PLAYER).read_text())
        self.assertEqual(sha(inherited.encode()), INHERITED_GAME_SHA256)

    def test_removing_or_misbinding_actual_new_job_inputs_controls_flags_and_budget_is_red(self):
        game, player = (ROOT / GAME).read_text(), (ROOT / PLAYER).read_text()
        changes = [(new_job(), ""), ("  defensive-presnap-switch:\n", "  optional-presnap:\n"),
            ("    timeout-minutes: 40\n", "    timeout-minutes: 41\n"),
            (" --partition archived-four\n", "\n"),
            ("--partition u2-current-fifth", "--partition archived-four"),
            ("    steps:\n", "    needs: core-and-tooling\n    steps:\n"),
            (LEGACY_BOOTSTRAP, ""), (LEGACY_DEFAULTS, ""),
            ("--sdk-netstandard21 --controls", "--controls"),
            ("--sdk-netstandard21 --controls", "--sdk-netstandard21"),
            ("--c2-presnap-switch-inputs", "--c2-m1-inputs"),
            ("--source-compatibility portable", "--source-compatibility strict"),
            ("--profile defensive-presnap-switch", "--profile contact272"),
            ("--catalog tools/animation/presnap_switch_sequences.json", "--catalog tools/animation/c2_sequences.json"),
            ("--input-manifest-sha256 " + BASE558_SHA256, "--input-manifest-sha256 " + "0" * 64),
            ("--recipe tests/movement-callers/fixtures/" + CASE + ".recipe.json",
             "--recipe tests/movement-callers/fixtures/" + OLD_CASE + ".recipe.json"),
            ("--fixture tests/movement-callers/fixtures/" + CASE + ".jsonl.gz",
             "--fixture tests/movement-callers/fixtures/" + OLD_CASE + ".jsonl.gz"),
        ]
        for commands in COMMANDS.values():
            for command in commands:
                changes.append((command, "true"))
        for path in GAME_PATH_ADDITIONS:
            changes.append(("      - '" + path + "'\n", ""))
        for old, new in changes:
            with self.subTest(mutation=old):
                self.assertIn(old, game)
                with self.assertRaises(AssertionError):
                    validate_workflows(game.replace(old, new, 1), player)
        for path in PLAYER_PATH_ADDITIONS:
            with self.subTest(trigger=path), self.assertRaises(AssertionError):
                validate_workflows(game, player.replace("      - '" + path + "'\n", "", 1))
        for name, commands in PLAYER_STEPS.items():
            for command in commands:
                with self.subTest(asset_free=command), self.assertRaises(AssertionError):
                    validate_workflows(game, player.replace(command, "true", 1))

    def test_preparation_commands_use_permanent_producers_and_exact_immutable_generations(self):
        commands = kernel_commands(Path(D))
        self.assertEqual(len(commands), 11)
        self.assertTrue(all(command[:2] == ["python3", "-B"] for command in commands))
        self.assertEqual(commands[0][2], "tests/parity/prepare_inputs.py")
        self.assertIn("--profile", commands[-1])
        source = (HERE / "test_ci.py").read_text()
        self.assertIn('"--settings", "tools/source-player/match-settings.four-player.json"', source)
        preparation = (ROOT / "tests/presentation/possession-services/prepare.py").read_text()
        self.assertIn("original = Path(original).absolute()", preparation)
        self.assertIn("fire.prepare(original / \"nfl\"", preparation)

    def test_named_acquisition_preserves_original69_78_79_and_closed_team_world_metadata(self):
        recipe, pins, _ = acquisition_catalog()
        kernel = load("defensive_ci_test_fetch", "tests/parity/fetch_inputs.py")
        self.assertEqual([len(kernel.input_pins(profile)) for profile in
                          ("legacy", "contact272", "defensive-presnap-switch")], [69, 78, 79])
        for name, pin in kernel.input_pins("defensive-presnap-switch").items():
            self.assertEqual(pins["nfl", name]["gitBlobSha1"], pin)
        self.assertEqual(recipe["repositories"]["nfl"]["revision"], "89f840d58d152a2a9f230cb8d0cdffbd554c8ded")
        self.assertEqual(recipe["repositories"]["midway"]["revision"], "1792293aaaea00a919b59e8c05716978e85b4333")
        for (repo, name), pin in pins.items():
            self.assertFalse(Path(name).is_absolute())
            self.assertTrue("gitBlobSha1" in pin or "sha256" in pin, (repo, name))

    def test_regular_original_validation_rejects_alias_kind_mode_length_hash_or_pin(self):
        raw = b"authored non-original test source, not a numeric fixture"
        digest = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        entry = dict(type="blob", mode="100644", sha=digest, size=len(raw))
        expected = dict(gitBlobSha1=digest, bytes=len(raw), sha256=sha(raw))
        verify_original(raw, expected, entry, "authored.c")
        for field, value in (("type", "tree"), ("mode", "120000"), ("mode", "160000"),
                             ("size", 0), ("sha", "0" * 40)):
            with self.subTest(field=field), self.assertRaises(AssertionError):
                verify_original(raw, expected, dict(entry, **{field: value}), "authored.c")
        with self.assertRaises(AssertionError):
            verify_original(raw + b"\n", expected, entry, "authored.c")
        document = dict(project=dict(frameworks={"netstandard2.1": dict(
            frameworkReferences={"NETStandard.Library": {"privateAssets": "all"}},
            downloadDependencies=[{"name": "NETStandard.Library.Ref", "version": "[2.1.0, 2.1.0]"}])}))
        validate_targeting_assets(document, True)
        changed = copy.deepcopy(document)
        changed["project"]["frameworks"]["netstandard2.1"]["downloadDependencies"][0]["version"] = "[2.1.0,)"
        with self.assertRaisesRegex(AssertionError, "exact implicit"):
            validate_targeting_assets(changed, True)

    def test_closed_input_fixture_is_real_and_recipe_has_no_private_payload_fields(self):
        replay = load("defensive_ci_test_public", "tests/movement-callers/prepare_public_replay.py")
        raw = replay.load_fixture(ROOT / f"tests/movement-callers/fixtures/{CASE}.jsonl.gz", CASE)
        self.assertEqual(sha(raw), PUBLIC_SHA256)
        self.assertEqual(len(replay.audit_public(raw, CASE)), 12000)
        replay.load_recipe(ROOT / f"tests/movement-callers/fixtures/{CASE}.recipe.json", CASE)
        for field, value in (("rawVector", [1]), ("poses", []), ("state", {}), ("sourceprofile", "old")):
            recipe = json.loads((ROOT / f"tests/movement-callers/fixtures/{CASE}.recipe.json").read_bytes())
            with self.subTest(field=field), self.assertRaises(ValueError):
                replay.validate_presnap_recipe(dict(recipe, **{field: value}))

    def test_actual_helper_compile_and_metadata_closures_are_present(self):
        self.assertTrue((HERE / "oracle.py").is_file())
        runner = (HERE / "run.py").read_text()
        self.assertIn("oracle.generate(args.source_root", runner)
        self.assertIn("MUTATIONS+ROOT_MUTATIONS", runner)
        for filename in ("PreSnapSwitch.Checks.csproj", "PreSnapSwitch.Production.csproj"):
            text = (HERE / filename).read_text()
            for helper in ("FullKernelSources.props", "SourceSoundVmSources.props",
                           "SourceSoundVmTransport.cs", "SourceCinematicPlayback.cs"):
                self.assertIn(helper, text)
        self.assertTrue((ROOT / "tools/rosters/c_source.py").is_file())
        inventory = code_paths()
        for name in ("tests/defensive-presnap-switch/PreSnapSwitch.Checks.csproj",
                     "tests/defensive-presnap-switch/PreSnapSwitch.Production.csproj"):
            self.assertIn(name, inventory)
        self.assertTrue(all((ROOT / name).is_file() for name in inventory))
        mutations = load("defensive_ci_literal_mutations", "tests/defensive-presnap-switch/mutations.py")
        self.assertEqual([spec[0] for spec in mutations.MUTATIONS + mutations.ROOT_MUTATIONS], list(FOCUSED_CONTROLS))

    def test_missing_or_corrupt_actual_results_are_not_status_only_success(self):
        root = ROOT / ".squad/.scratch" / ("ci-artifact-negative-" + uuid.uuid4().hex)
        root.mkdir(parents=True)
        try:
            with self.assertRaises(OSError):
                summarize(root, root, root / "summary.json")
            path = root / "changed-result.json"
            path.write_text('{"status":"passed-portable-M1-public-caller-gate"}')
            with self.assertRaisesRegex(AssertionError, "Actual normal full"):
                validate_full(root, checked_json(path))
            path.write_text("{")
            with self.assertRaises(ValueError):
                checked_json(path)
            self.assertFalse((root / "summary.json").exists())
        finally:
            shutil.rmtree(root)


def main():
    if len(sys.argv) == 1 or sys.argv[1].startswith("-"):
        unittest.main()
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="operation", required=True)
    create = sub.add_parser("workspace")
    create.add_argument("--output", type=Path, required=True)
    create.add_argument("--inherited", action="store_true")
    fetch = sub.add_parser("acquire")
    fetch.add_argument("--output", type=Path, required=True)
    sub.add_parser("ensure-sdk").add_argument("--output", type=Path, required=True)
    for name in ("prepare-kernel", "prepare-player"):
        sub.add_parser(name).add_argument("--root", type=Path, required=True)
    summary = sub.add_parser("summarize")
    summary.add_argument("--root", type=Path, required=True)
    summary.add_argument("--results", type=Path, required=True)
    summary.add_argument("--output", type=Path, required=True)
    negatives = sub.add_parser("failure-guards")
    negatives.add_argument("--root", type=Path, required=True)
    negatives.add_argument("--results", type=Path, required=True)
    negatives.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.operation == "workspace":
        workspace(args.output, args.inherited)
    elif args.operation == "acquire":
        acquire(args.output)
    elif args.operation == "ensure-sdk":
        ensure_sdk(args.output)
    elif args.operation == "prepare-kernel":
        prepare_kernel(args.root)
    elif args.operation == "prepare-player":
        prepare_player(args.root)
    elif args.operation == "failure-guards":
        failure_guards(args.root, args.results, args.output)
    else:
        summarize(args.root, args.results, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
