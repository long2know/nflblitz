#!/usr/bin/env python3
"""Independent public TwoStation leave/continue/rejoin regression; no browser or downloads."""
import argparse
import glob
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SESSION = "tools/source-player/Runtime/SourceCabinetSession.cs"
OLD_SHA = "8ef9dad9c739f9c08927fe63b4d21eaebd9e7413281043ddbcd5da281953177b"
REJECTION = "Both original TwoStation humans must participate before leaving the join screen."


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inputs():
    paths = set((ROOT / "tools/source-player/Runtime").glob("*.cs"))
    for item in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        pattern = item.attrib["Include"].replace("$(BlitzSourceRoot)", str(ROOT) + "/")
        paths.update(Path(path) for path in glob.glob(pattern))
    for item in ET.parse(ROOT / "tools/source-player/SourcePlayer.Checks.csproj").iter("Compile"):
        name = item.attrib["Include"]
        if not name.startswith("../../"):
            paths.add(ROOT / "tools/source-player" / name)
    paths.update(ROOT / "tools/source-player" / name for name in
        ("SourcePlayer.Checks.csproj", "SourcePlayer.Core.csproj", "Directory.Build.props", "ProductionSources.props"))
    return {str(path.relative_to(ROOT)): path.read_bytes() for path in sorted(paths)}


def old_caller(content):
    text = content.decode()
    changes = (
        ("ParticipatingStationMask != 0 && CanLeaveJoining &&\n                ",
         "ParticipatingStationMask != 0 && "),
        ("else if (CanLeaveJoining) EnterRecordChoices();", "else EnterRecordChoices();"),
        ("    private bool CanLeaveJoining => StationLayout != SourceStationLayout.TwoStation || ParticipatingStationMask == 3;\n\n",
         ""),
    )
    for new, old in changes:
        if text.count(new) != 1:
            raise RuntimeError("The exact independently revised caller anchor must occur once.")
        text = text.replace(new, old)
    original = text.encode()
    if hashlib.sha256(original).hexdigest() != OLD_SHA:
        raise RuntimeError("Reverting only the caller predicate must reproduce the exact rejected source.")
    return original


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data_root", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expect-old-caller", action="store_true")
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    sdk = os.environ["SOURCE_PLAYER_DOTNET"]
    original = inputs()
    before = {name: hashlib.sha256(raw).hexdigest() for name, raw in original.items()}
    data = args.data_root.resolve()
    data_pins = {name: digest(data / name) for name in ("manifest.json", "audio/complete-av306-manifest.json")}
    commands = []
    runs = []

    def snapshot(name, replacement=None):
        destination = out / name / "source"
        for relative, raw in original.items():
            path = destination / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(replacement if relative == SESSION and replacement is not None else raw)
        return destination

    def run(name, source, rejected=False):
        directory = out / name
        directory.mkdir(parents=True, exist_ok=True)
        project = source / "tools/source-player/SourcePlayer.Checks.csproj"
        build = [sdk, "build", str(project), "-c", "Release", "--no-restore", "--nologo", "-v", "quiet",
            "-t:Rebuild", "-p:TreatWarningsAsErrors=true"]
        commands.append(build)
        built = subprocess.run(build, cwd=source, text=True, capture_output=True)
        (directory / "build-first.log").write_text(built.stdout + built.stderr)
        if built.returncode and "NETSDK1004" in built.stdout + built.stderr:
            restore = [sdk, "restore", str(project), "--nologo", "-v", "quiet"]
            commands.append(restore)
            restored = subprocess.run(restore, cwd=source, text=True, capture_output=True)
            (directory / "restore.log").write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("Missing-assets restore failed for " + name)
            built = subprocess.run(build, cwd=source, text=True, capture_output=True)
        (directory / "build.log").write_text(built.stdout + built.stderr)
        if built.returncode:
            raise RuntimeError("The real production/test source must compile: " + name + "\n" +
                built.stdout + built.stderr)
        assembly = source / "tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll"
        command = [sdk, str(assembly), str(data), "--two-station-join-revision-only"]
        commands.append(command)
        checked = subprocess.run(command, cwd=source, text=True, capture_output=True)
        output = checked.stdout + checked.stderr
        (directory / "checks.log").write_text(output)
        if rejected:
            if checked.returncode == 0 or REJECTION not in output:
                raise RuntimeError("The exact old caller must fail the genuine public input sequence: " + output)
            observed = {"rejectedBy": REJECTION}
        else:
            if checked.returncode:
                raise RuntimeError("The focused public revision failed: " + output)
            observed = next(json.loads(line) for line in output.splitlines()
                if line.startswith("{") and "twoStationJoinRevision" in line)
        row = {"name": name, "buildExit": built.returncode, "checksExit": checked.returncode,
            "assemblySha256": digest(assembly), "observed": observed}
        runs.append(row)
        print(json.dumps(row))

    if args.expect_old_caller:
        if before[SESSION] != OLD_SHA:
            raise RuntimeError("Pre-fix reproduction must use the exact rejected production file.")
        run("before-revision", snapshot("before-revision"), rejected=True)
    else:
        baseline = snapshot("fixed-baseline")
        run("fixed-baseline", baseline)
        if args.controls:
            run("old-caller-control", snapshot("old-caller-control", old_caller(original[SESSION])), rejected=True)
            run("forced-fresh-fixed-final", baseline)
            if runs[0]["assemblySha256"] != runs[-1]["assemblySha256"]:
                raise RuntimeError("The fresh same-source rebuild changed the executing regression assembly.")
    after = inputs()
    if original != after:
        raise RuntimeError("Canonical compiled input changed during isolated revision qualification.")
    if data_pins != {name: digest(data / name) for name in data_pins}:
        raise RuntimeError("Authenticated input manifest changed during revision qualification.")
    receipt = {"schema": "independent-two-station-join-revision/v1", "status": "passed",
        "runs": runs, "commands": commands, "sourceHashes": before, "canonicalStable": True,
        "dataRoot": str(data), "dataPins": data_pins, "frontendConsulted": False,
        "originalAuthorLockedOut": True, "browserAOTOrNaturalMatchClaimed": False}
    path = out / "qualification.json"
    path.write_text(json.dumps(receipt, indent=2) + "\n")
    print("Receipt: " + str(path))


if __name__ == "__main__":
    main()
