"""Original generated data: no archive roster/model/texture arrays."""

import copy
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import export_rosters as exporter
from c_source import CData, f32


def prefix(index):
    return "X" + chr(65 + index // 26) + chr(65 + index % 26)


def team_sources(record_count=16, team_count=31):
    player = CData("""
enum { PS_NORM, PS_FAT };
enum { PC_BLACK, PC_WHITE };
enum { PP_QB, PP_WR, PP_RB, PP_TE, PP_OL, PP_K, PP_LB, PP_CB, PP_DL, PP_S };
""").enums()
    game_text = "#define NUM_TEAMS 31\n"
    for index in range(team_count):
        code = prefix(index)
        game_text += "#define TEAM_FIXTURE_" + code + " " + str(index) + "\n"
        game_text += "#define TEAM_" + code + " TEAM_FIXTURE_" + code + "\n"
    game = CData(game_text, player.expressions.macros)
    text = "#define SC(x) (1.05f * x)\n#define NA 0\n"
    for team in range(team_count):
        text += "const struct st_plyr_data spd_" + prefix(team).lower() + "[] = {\n"
        for index in range(record_count):
            text += '{0,1,2,3,4,5,SC(1.0f),PS_NORM,PC_WHITE,0x54,PP_QB,0,0,0,NA,"Fixture","Player"},\n'
        text += "};\n"
    text += "const struct fbteam_data teaminfo[] = {\n"
    for team in range(team_count):
        code = prefix(team)
        text += '{{1,2,3,4,5},7,"Fixture ' + str(team) + '","Test City","' + code + '","' + code + '",spd_' + code.lower() + ',0,0,0,0,0,0},\n'
    text += "};\n"
    return text, game


def synthetic_dataset():
    manifest = exporter.read_json(exporter.INPUT_MANIFEST.read_bytes())
    assets = {}

    def asset(identity, kind="texture", path=None, symbol=None, filename=None):
        assets[identity] = {
            "id": identity, "kind": kind, "runtime_filename": filename, "symbol": symbol,
            "requested_source_path": path, "source_path": path,
            "git_blob_sha1": "a" * 40 if path else None,
            "status": "available-in-pinned-tree" if path else "missing-from-pinned-tree",
        }
        return identity

    for filename in ("ARMHND.WMS", "ARMHND2.WMS", "HEDSHU.WMS", "HEDSHU2.WMS"):
        asset("runtime/" + filename, filename=filename)
    teams = []
    for team in range(31):
        code, players = prefix(team), []
        for index in range(16):
            size = 1 if index < 3 else 0
            skin = index % 2
            position = index % 10
            packed = 0x54 if index == 0 else 0x08
            size_symbol = ("PS_NORM", "PS_FAT")[size]
            skin_symbol = ("PC_BLACK", "PC_WHITE")[skin]
            position_symbol = ("PP_QB", "PP_WR", "PP_RB", "PP_TE", "PP_OL", "PP_K", "PP_LB", "PP_CB", "PP_DL", "PP_S")[position]
            raw = ["0", "1", "2", "3", "4", "5", "SC(1.0f)", size_symbol, skin_symbol,
                   "0x54" if index == 0 else "0x08", position_symbol, "0", "0", "0", "0", '"Fixture"', '"Player"']
            players.append({
                "id": "spd_" + code.lower() + ":" + str(index).zfill(2), "source_static_id": team * 16 + index,
                "team_id": team, "index": index, "source_line": 1 + index, "raw_fields": raw,
                "statistics": list(range(6)), "scale_raw": "SC(1.0f)", "scale": f32(1.05),
                "size": {"raw": size_symbol, "symbol": size_symbol, "value": size},
                "skin": {"raw": skin_symbol, "symbol": skin_symbol, "value": skin},
                "position": {"raw": position_symbol, "symbol": position_symbol, "value": position},
                "number_raw": raw[9], "number_packed_bcd": packed, "jersey_number": 54 if index == 0 else 8,
                "jersey_digit_indices": [4, 3] if index == 0 else [9, 7],
                "mugshot": None, "selection_name": None, "hud_name": None,
                "announcer_raw": "0", "announcer": 0, "lastname": "Fixture", "firstname": "Player",
                "height_inches": None, "weight_pounds": None, "static_flags": None, "static_head_selector": None,
            })
        teams.append({
            "id": team, "symbol": "TEAM_FIXTURE_" + code, "aliases": ["TEAM_" + code],
            "name": "Fixture " + str(team), "home": "Test City", "abbrev": code, "prefix": code,
            "roster_symbol": "spd_" + code.lower(), "attributes": [1, 2, 3, 4, 5], "drone_base": 7,
            "image_symbols": [None] * 6, "captain_player_id": None, "rgb_palette": None, "players": players,
        })
    uniforms = []
    for team in teams:
        for variant in range(2):
            filename = team["prefix"] + ("H" if variant == 0 else "A") + "LOD.WMS"
            atlas = asset("runtime/" + filename, path="img3d/" + filename.lower(), filename=filename)
            for skin in range(2):
                uniforms.append({
                    "team_id": team["id"], "home_away": variant, "skin": skin, "decal_offset": 0 if skin == 1 else 1,
                    "slot_asset_ids": ["runtime/ARMHND.WMS" if skin else "runtime/ARMHND2.WMS",
                                       "runtime/HEDSHU.WMS" if skin else "runtime/HEDSHU2.WMS"] +
                                      [atlas] * 4 + ["runtime/HEDSHU.WMS"],
                })
    skeleton = asset("file/models/skeleton.h", "skeleton", "models/skeleton.h")
    limbs = []
    for index in range(15):
        symbol = "limb_fixture_" + str(index)
        limbs.append(asset("limb/" + symbol, "limb", "models/fixture.h", symbol=symbol))
    models = [{
        "index": index, "limb_array": "fixture_model_" + str(index), "header": "models/fixture.h",
        "material_array": "fixture_textures", "material_slots": ["armhnd.wms", "hedshu.wms", "jersf.wms", "leghel.wms", "slvmsk.wms"],
        "limb_symbols": ["limb_fixture_" + str(joint) for joint in range(15)], "limb_asset_ids": list(limbs),
    } for index in range(32)]
    jerseys = []
    for index in range(8):
        symbol = "limb_fixture_jersey_" + str(index)
        jerseys.append({"index": index, "limb_symbol": symbol,
                        "asset_id": asset("limb/" + symbol, "limb", "models/fixture.h", symbol=symbol)})
    generated = [
        asset("generated/models/head.h", "generated-header"),
        asset("generated/models/helm.h", "generated-header"),
        asset("limb/limb_phead_obj", "limb", symbol="limb_phead_obj"),
    ]
    heads = []
    for index in range(20):
        symbol = "limb_fixture_head_" + str(index)
        heads.append({
            "index": index, "limb_symbol": symbol,
            "limb_asset_id": asset("limb/" + symbol, "limb", "models/fixture-head.h", symbol=symbol),
            "texture_asset_id": asset("runtime/FIXTURE_HEAD_" + str(index), filename="FIXTURE_HEAD_" + str(index)),
        })
    codes = [{"name": "HEAD  " if index == 0 else "F" + str(index).zfill(5),
              "code": str(index).zfill(4), "head_index": index % 20} for index in range(21)]
    lineups = {name: list(range(7)) for name in ("std_offense", "kickoff", "kick_return1", "kick_return2", "kick_return3", "punt", "fgoal")}
    lineups["std_defense"] = list(range(7, 14))
    lineups["returners"] = [index % 3 for index in range(30)]
    lineups["source_issue"] = "Synthetic test retains missing team-30 returner contract."
    lineups["offense_selector"] = [{"play": "play_punt", "lineup": "punt"},
                                  {"play": "play_kick_return_short", "lineup": "returners"}]
    lineups["defense_selector"] = [{"play": "play_kickoff_313", "lineup": "returners"}]
    constants = [{"symbol": symbol, "raw": str(value), "value": value} for symbol, value in (
        ("PPOS_QBACK", 3), ("MOD_266", 0), ("MOD_FAT", 1), ("MOD_498", 2), ("MOD_LBALL", 4),
        ("MOD_RBALL", 8), ("MOD_JUMBO", 16), ("MOD_2DIGITS", 4))]
    return {
        "schema": exporter.SCHEMA,
        "provenance": {"repository": exporter.REPOSITORY, "commit": exporter.REVISION, "tree": exporter.TREE,
                       "manifest_sha256": exporter.MANIFEST_SHA256, "content_pin_verified": True,
                       "inputs": copy.deepcopy(manifest["inputs"])},
        "profile": {"platform": "SEATTLE", "release": True, "defines": ["SEATTLE"],
                    "disabled_flags": list(exporter.DISABLED_FLAGS), "teams": 31, "static_records_per_team": 16,
                    "active_per_team": 7, "maximum_humans": 4, "scale_macro": "(1.05f * x)",
                    "float_arithmetic": "IEEE-754 binary32", "player_field_order": list(exporter.PLAYER_FIELDS)},
        "teams": teams, "uniforms": uniforms,
        "actor_model_schema": {"skeleton_asset_id": skeleton, "skeleton_symbol": "fbplyr_skel",
                               "head_joint_index": 8, "custom_head_first_texture_slot": 7,
                               "number_texture_slot": 5, "number_matrix_index": 1, "models": models, "jerseys": jerseys,
                               "generated_header_asset_ids": generated},
        "lineups": lineups,
        "selection": {"away_jersey": [[int(row == column) for column in range(31)] for row in range(31)],
                      "legacy_away_jersey": [[0] * 31 for _ in range(31)], "legacy_qualifier": "Synthetic inactive branch.",
                      "secret_heads": {"heads": heads, "selection_codes": codes, "separate_static_rosters": [],
                                       "qualification": "Original synthetic fixture; no archive art or stat arrays."}},
        "tuning": {"constants": constants, "dir49_table": list(range(49)),
                   "field_friction_mult_factor": [1.0] * 7, "base_speeds": [f32(index / 10) for index in range(11)],
                   "base_turbo_speeds": [f32(index / 8) for index in range(11)],
                   "diff_settings": [[index, 1, 2] for index in range(9)],
                   "team_ladder": [[index, 1, 2, -1, -1] for index in range(9)], "beat_all_teams": [0, 1, 2, 3]},
        "references": [{"path": "teamdata.c", "symbol": "fixture", "detail": "Synthetic source-schema fixture, not original game data."}],
        "source_issues": [{"id": "fixture", "source": "synthetic", "detail": "Intentional missing asset and selector cases."}],
        "assets": sorted(assets.values(), key=lambda value: value["id"]),
        "missing_asset_ids": sorted(key for key, value in assets.items() if value["source_path"] is None),
    }


if __name__ == "__main__":
    destination = exporter.ROOT / "tools/rosters/SourceData/synthetic-rosters.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    data = exporter.json_bytes(synthetic_dataset())
    destination.write_bytes(data)
    print(str(destination.relative_to(exporter.ROOT)), exporter.digest(data))
