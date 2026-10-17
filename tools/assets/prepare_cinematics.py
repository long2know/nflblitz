#!/usr/bin/env python3
"""Convert active original impact/endplay camera data, never invent camera shots."""

import argparse
import re
from pathlib import Path
import sys

import prepare_player as files
from prepare_teams import read_pinned

ROOT = Path(__file__).absolute().parents[2]
sys.path.insert(0, str(ROOT / "tools/rosters"))
from c_source import CData, Macro, Scalar, SourceError
from export_rosters import REVISION, TREE, read_json, require, verify_tree

TABLES = ("csi_fs_endplay", "csi_fs_endqtr", "csi_sn_diveattack", "csi_zm_divetack",
          "csi_rs_suplex", "csi_rs_suplex_nf", "csi_zm_endplayfg")
INPUTS = ("include/GAME.H", "include/ani3d.h", "include/camdefs.h", "CamData.c",
          "cambot.c", "plyrseq.c", "detect.c", "player.c", "lib/ani3d/ANIM.C")


def inventory(script):
    result = []
    owner, next_script = None, False
    lines = script.splitlines()
    for i, original in enumerate(lines):
        line = original.strip()
        if line.startswith("#") or not line:
            continue
        if re.fullmatch(r"\.byte\s+SCRIPT", line):
            next_script = True
        elif next_script and re.fullmatch("[A-Za-z_]\\w*:", line):
            owner, next_script = line[:-1], False
        elif re.match(r"\.byte\s+ANI_(?:SETCAMBOT|SHAKER)\b", line):
            words = next((item.strip() for item in lines[i + 1:i + 5]
                          if item.strip() and not item.strip().startswith("#")), "")
            require(words.startswith(".word"), "Truncated original presentation opcode at line " + str(i + 1))
            operand = words.partition(".word")[2].strip()
            shaker = "ANI_SHAKER" in line
            if shaker:
                match = re.fullmatch(r"\.byte\s+ANI_SHAKER\s*,\s*(\d+)", line)
                require(match is not None and operand.isdigit(), "Unsupported original shaker operands.")
                args = [int(match.group(1)), int(operand)]
            else:
                require(re.fullmatch("[A-Za-z_]\\w*", operand) is not None, "Invalid original CSI table symbol.")
                args = [operand]
            result.append({"script": owner, "line": i + 1, "kind": "shaker" if shaker else "camera",
                           "arguments": args, "execution": "actual ordered VM opcode required; no frame/velocity inference"})
        elif re.match(r"\.word\s+(dust_cloud|attack_snd|get_hit_snd|grnd_hit_snd|pop_helmet)\s*,", line):
            values = line.partition(".word")[2].strip().split(",")
            result.append({"script": owner, "line": i + 1, "kind": "callback",
                           "arguments": [value.strip() for value in values],
                           "execution": "actual ordered source callback required"})
    return result


def contact_sites(text):
    active = CData(text)
    sites = []
    functions = list(re.finditer(r"(?:static\s+)?(?:int|void)\s+([A-Za-z_]\w*)\s*\([^;{}]*\)\s*\{", active.text))
    for match in re.finditer(r"\bselect_csi_item\s*\(\s*([A-Za-z_]\w*)\s*\)", active.text):
        owners = [owner for owner in functions if owner.start() < match.start()]
        require(owners, "Original contact-camera selection has no function owner.")
        sites.append({"function": owners[-1].group(1), "line": active.text[:match.start()].count("\n") + 1,
                      "table": match.group(1)})
    return sites


def convert(source_root):
    tree = verify_tree(read_json(files.read_regular(source_root / "tree.json", 16 * 1024 * 1024)))
    sources = {path: read_pinned(source_root, tree, path) for path in INPUTS}
    env = {"SEATTLE": Macro("1")}
    game = CData(sources["include/GAME.H"].decode("latin1"), env)
    header = CData(sources["include/ani3d.h"].decode("latin1"), game.expressions.macros).enums()
    env = header.expressions.macros
    env["DG2RD"] = Macro("(x)", ("x",))
    definitions = CData(sources["include/camdefs.h"].decode("latin1"), env)
    env = definitions.expressions.macros
    camera_text = sources["CamData.c"].decode("latin1")
    camera = CData(camera_text, env)
    spline_data = CData(re.sub(r"\bSPLINE\s+([A-Za-z_]\w*)\s*=", r"float \1[] =", camera_text), env)
    tables = []
    for name in TABLES:
        rows, _ = camera.array(name)
        require(rows and len(rows[-1]) == 1 and camera.number(rows[-1][0]) == -1,
                "Missing original CSI terminator: " + name)
        entries = []
        for ordinal, row in enumerate(rows[:-1]):
            require(len(row) == 7, "Wrong original CAMSEQINFO row width: " + name)
            mode, total, travel, theta, phi, distance = [camera.number(value) for value in row[:6]]
            points = []
            pointer = row[6].raw
            if pointer != "NULL":
                require(re.fullmatch("&[A-Za-z_]\\w*", pointer) is not None, "Unsupported source CSI pointer: " + pointer)
                raw, _ = spline_data.array(pointer[1:])
                require(len(raw) in (6, 12), "Wrong source float/rail spline control count.")
                points = [spline_data.number(value) for value in raw]
            entries.append({
                "ordinal": ordinal, "source_line": row[0].line, "mode": mode, "mode_symbol": row[0].raw,
                "mode_ticks": total, "travel_ticks": travel, "theta_degrees": theta,
                "phi_degrees": phi, "distance": distance, "control_points": points,
            })
        tables.append({"symbol": name, "entries": entries})
    cambot = CData(sources["cambot.c"].decode("latin1"), env)
    raw, _ = cambot.array("shaker_exp")
    shaker = [cambot.number(value) for value in raw]
    require(len(shaker) == 64, "Wrong original shaker exponent table count.")
    catalog = {
        "schema": "original-impact-presentation/v1", "source_revision": REVISION, "source_tree": TREE,
        "profile": "normal-release-57hz-no-debug-no-NOSETCAM", "tick_rate": 57,
        "camera_mode_count": header.expressions.evaluate("CAM_MODECNT"),
        "tables": tables, "shaker_exp": shaker,
        "animation_cues": inventory(sources["plyrseq.c"].decode("latin1")),
        "contact_camera_sites": contact_sites(sources["detect.c"].decode("latin1")),
        "inputs": [dict(files.fingerprint(sources[path]), path=path) for path in INPUTS],
        "qualifications": [
            "Source selected ordinal is an actual randrng(count) result, not a renderer RNG.",
            "ANI camera branches and variable AA_TOGROUND timing require actual VM callbacks.",
            "No replays in active Blitz2000: csi_replay1 is #if0 and playback is FALSE. No generic slow motion.",
            "Normal camera is retained. Special functional math uses supplied original centered source coordinates.",
            "Host functional oracles use libm for fsin/fcos; original fast-trig/MIPS rounding remains qualified.",
        ],
    }
    return catalog


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        source = files.absolute_path(args.source_root, Path.cwd())
        destination = files.absolute_path(args.destination, ROOT)
        require(ROOT / ".squad/.scratch" in destination.parents, "Cinematic data must remain ignored/local.")
        require(source != destination and source not in destination.parents and destination not in source.parents,
                "Source and cinematic output must not overlap.")
        catalog = convert(source)
        bundle = {"impact-presentation.json": files.json_bytes(catalog)}
        if destination.exists():
            files.verify_identical(destination, bundle)
        else:
            files.make_directories(destination)
            (destination / "impact-presentation.json").write_bytes(bundle["impact-presentation.json"])
        print(files.json_bytes({
            "catalog": str(destination / "impact-presentation.json"),
            "sha256": files.sha256(bundle["impact-presentation.json"]),
            "tables": len(catalog["tables"]), "entries": sum(len(table["entries"]) for table in catalog["tables"]),
            "actual_animation_cues": len(catalog["animation_cues"]), "editor_executed": False,
        }).decode("ascii"), end="")
    except (SourceError, files.PreparationError, OSError, KeyError, ValueError) as error:
        parser.exit(1, "error: " + str(error) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
