#!/usr/bin/env python3
"""Export pinned active kickoff camera data privately; keep the impact catalog unchanged."""
import argparse
import re
from pathlib import Path
import sys

import prepare_player as files
from prepare_teams import read_pinned

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/rosters"))
from c_source import CData, Macro, SourceError, split_values
from export_rosters import REVISION, TREE, read_json, require, verify_tree

INPUTS = ("include/GAME.H", "include/ani3d.h", "include/camdefs.h", "CamData.c", "cambot.c")


def convert(source_root):
    tree = verify_tree(read_json(files.read_regular(source_root / "tree.json", 16 * 1024 * 1024)))
    inputs = {name: read_pinned(source_root, tree, name) for name in INPUTS}
    env = {"SEATTLE": Macro("1"), "DG2RD": Macro("(x)", ("x",))}
    for name in INPUTS[:3]:
        env = CData(inputs[name].decode("latin1"), env).enums().expressions.macros
    camera = CData(inputs["CamData.c"].decode("latin1"), env)
    starts = {}
    for match in re.finditer(r"\bKI\s*\(([^;]+)\)\s*;", camera.text):
        values = [value.strip() for value, _ in split_values(match.group(1))]
        require(len(values) == 5 and values[0] not in starts, "Invalid original kickoff start definition.")
        starts[values[0]] = [camera.expressions.evaluate(value) for value in values[1:]]

    def sequence(row, ordinal, table):
        mode, total, travel, theta, phi, distance = [camera.number(value) for value in row[:6]]
        pointer = row[6].raw
        require(re.fullmatch(r"&[A-Za-z_]\w*", pointer), "Expected original camera data pointer.")
        symbol = pointer[1:]
        start = None
        if table == "csi_kickoff":
            require(symbol.startswith("ki_") and symbol[3:] in starts, "Missing original kickoff start.")
            symbol = symbol[3:]
            start = starts[symbol]
        raw, _ = camera.array(symbol)
        points = [camera.number(value) for value in raw]
        require(len(points) % 3 == 0 and 6 <= len(points) <= 6000, "Unbounded original camera point stream.")
        if mode == 19:
            require(points[-3:] == [0, 0, 0] and points[-4] != 0, "Missing original kickoff terminator/unit endpoint.")
        else:
            require(mode == 9 and len(points) == 6, "Unexpected active original kickoff/FG mode.")
        result = dict(ordinal=ordinal, source_line=row[0].line, mode=mode, mode_symbol=row[0].raw,
                      mode_ticks=total, travel_ticks=travel, theta_degrees=theta, phi_degrees=phi,
                      distance=distance, control_points=points)
        if start is not None:
            result.update(eye=start[:3], auto_hike_ticks=start[3])
        return result

    tables = {}
    for symbol, count in (("csi_kickoff", 11), ("csi_fs_fgtrk", 2)):
        rows, _ = camera.array(symbol)
        require(len(rows) == count + 1 and camera.number(rows[-1][0]) == -1, "Original camera table inventory changed.")
        tables[symbol] = [sequence(row, ordinal, symbol) for ordinal, row in enumerate(rows[:-1])]
    return dict(schema="original-cambot-gameplay/v1", source_revision=REVISION, source_tree=TREE,
                profile="normal-release-57hz-no-debug-no-NOSETCAM", tick_rate=57, tables=tables,
                inputs=[dict(files.fingerprint(inputs[name]), path=name) for name in INPUTS])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    try:
        source = args.source_root.resolve(strict=True)
        output = args.destination.absolute()
        require(ROOT / ".squad/.scratch" in output.parents and not output.exists(),
                "Choose a new ignored local camera output, never overwrite historical evidence.")
        payload = files.json_bytes(convert(source))
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(payload)
        print(files.json_bytes(dict(path=str(output), bytes=len(payload), sha256=files.sha256(payload),
                                   kickoff_entries=11, field_goal_entries=2, original_data_public=False)).decode(), end="")
    except (OSError, SourceError, files.PreparationError, ValueError) as error:
        parser.exit(1, "Original CAMBOT data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
