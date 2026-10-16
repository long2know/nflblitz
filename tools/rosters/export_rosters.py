#!/usr/bin/env python3
"""Offline, pinned original team/static-player/selector metadata export."""

import argparse
import hashlib
import json
import os
import pathlib
import re
import sys

from c_source import CData, Macro, Scalar, SourceError, without_comments


REPOSITORY = "historicalsource/nflblitz2000"
REVISION = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
TREE = "a7e0352125d46075b7d98e87253ab89b9160a4c7"
MANIFEST_SHA256 = "608e2e6352470993426b93130a779d014d7bc32b1794c7cb9e2eaf6ba14469a1"
SCHEMA = "original-team-rosters/v1"
ROOT = pathlib.Path(__file__).resolve().parents[2]
INPUT_MANIFEST = pathlib.Path(__file__).with_name("source_inputs.json")
STATS = ("at_spd", "at_pwr", "at_qb", "at_wr", "at_off", "at_def")
PLAYER_FIELDS = STATS + ("scale", "size", "color", "number", "position",
                         "pii_mugshot", "pii_selname", "pii_name", "ancr", "lastname", "frstname")
TEAM_FIELDS = ("attr", "drone_base", "name", "home", "abbrev", "prefix", "static_data",
               "pii_logo", "pii_logo30", "pii_cn", "pii_cnb", "prl", "pti")
DISABLED_FLAGS = ("DEBUG", "JASON", "NODRONES", "NOKICKOFF", "NOCLOCK",
                  "SHORT_QUARTERS", "NO_FIRE", "NOAUTOHIKE")


def require(condition, message):
    if not condition:
        raise SourceError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git_hash(kind, data):
    return hashlib.sha1(kind.encode("ascii") + b" " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=True, allow_nan=False,
                       separators=(",", ":")) + "\n").encode("ascii")


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(data):
    try:
        return json.loads(data, object_pairs_hook=no_duplicates,
                          parse_constant=lambda value: (_ for _ in ()).throw(SourceError("Nonfinite JSON: " + value)))
    except (ValueError, UnicodeError) as exc:
        raise SourceError("Invalid JSON: " + str(exc)) from exc


def relative_path(path):
    require(isinstance(path, str) and len(path) <= 256 and "\\" not in path and
            not pathlib.PurePosixPath(path).is_absolute() and
            all(part not in ("", ".", "..") for part in path.split("/")),
            "Expected portable source-relative path: " + str(path))
    return path


def verify_tree(metadata, expected=TREE):
    """Reconstruct every Git tree object; JSON labels/URLs are not content pins."""
    require(isinstance(metadata, dict) and metadata.get("sha") == expected and
            metadata.get("truncated") is False, "Expected complete pinned Git tree metadata")
    entries = metadata.get("tree")
    require(isinstance(entries, list) and 1 <= len(entries) <= 50000, "Git tree metadata size")
    paths, directories, children = {}, {"": expected}, {}
    for entry in entries:
        require(isinstance(entry, dict), "Invalid Git tree entry")
        path = relative_path(entry.get("path"))
        require(path not in paths, "Duplicate Git tree path: " + path)
        sha = entry.get("sha", "")
        require(re.fullmatch("[0-9a-f]{40}", sha) is not None, "Invalid Git tree entry SHA: " + path)
        kind, mode = entry.get("type"), entry.get("mode")
        require((kind == "tree" and mode == "040000") or
                (kind == "blob" and mode in ("100644", "100755", "120000")),
                "Unsupported Git tree entry type/mode: " + path)
        paths[path] = entry
        if kind == "tree":
            directories[path] = sha
        parent, _, name = path.rpartition("/")
        children.setdefault(parent, []).append((name, entry))
    require(set(children).issubset(directories), "Git tree entry missing parent directory")
    for path, expected_sha in directories.items():
        payload = bytearray()
        for name, entry in sorted(children.get(path, []),
                                  key=lambda item: (item[0] + ("/" if item[1]["type"] == "tree" else "")).encode("utf-8")):
            payload.extend(entry["mode"].lstrip("0").encode("ascii") + b" " + name.encode("utf-8") +
                           b"\0" + bytes.fromhex(entry["sha"]))
        require(git_hash("tree", bytes(payload)) == expected_sha, "Git tree content pin mismatch: " + (path or "/"))
    return paths


def verify_source_bytes(data, pin, tree_entry):
    path = pin["path"]
    require(tree_entry is not None and tree_entry["type"] == "blob" and tree_entry["mode"] != "120000" and
            tree_entry["sha"] == pin["git_blob_sha1"], "Pinned source/tree linkage mismatch: " + path)
    require(len(data) == pin["bytes"] and git_hash("blob", data) == pin["git_blob_sha1"] and
            digest(data) == pin["sha256"], "Source content pin mismatch: " + path)
    require(b"\0" not in data and len(data) <= 2 * 1024 * 1024, "Expected bounded textual source: " + path)


class SourceSet:
    def __init__(self, source_root, manifest_bytes=None):
        self.root = pathlib.Path(source_root).resolve(strict=True)
        require(self.root.is_dir(), "sourceRoot must be an explicit local directory")
        manifest_bytes = INPUT_MANIFEST.read_bytes() if manifest_bytes is None else manifest_bytes
        require(digest(manifest_bytes) == MANIFEST_SHA256, "Pinned input manifest SHA256 mismatch")
        self.manifest = read_json(manifest_bytes)
        require(self.manifest.get("schema") == "original-roster-source-inputs/v1" and
                self.manifest.get("repository") == REPOSITORY and self.manifest.get("commit") == REVISION and
                self.manifest.get("tree") == TREE, "Unknown source identity/pin manifest")
        self.tree = verify_tree(read_json(self.local(self.manifest["tree_metadata_path"]).read_bytes()))
        self.texts, self.pins = {}, []
        for pin in self.manifest["inputs"]:
            path = relative_path(pin["path"])
            require(path not in self.texts, "Duplicate pinned source input: " + path)
            entry = self.tree.get(path)
            data = self.local(path).read_bytes()
            verify_source_bytes(data, pin, entry)
            self.texts[path] = data.decode("latin-1")
            self.pins.append(dict(pin))

    def local(self, path):
        relative_path(path)
        candidate = self.root / path
        require(not candidate.is_symlink() and candidate.resolve(strict=True).is_relative_to(self.root),
                "Source path escapes its explicit root: " + path)
        return candidate


def integer(data, value):
    result = data.number(value)
    require(isinstance(result, int), "Expected integer at source line " + str(value.line))
    return result


def numeric_array(data, name, scope=None):
    values, _ = data.array(name, scope)

    def convert(value):
        return [convert(item) for item in value] if isinstance(value, list) else data.number(value)
    return convert(values)


def fields(values, names, label):
    require(isinstance(values, list) and len(values) == len(names),
            label + ": expected " + str(len(names)) + " original fields")
    return dict(zip(names, values))


def enum_value(data, value, prefix):
    number = integer(data, value)
    candidates = sorted(name for name, macro in data.expressions.macros.items()
                        if name.startswith(prefix) and macro.parameters is None and
                        data.expressions.evaluate(name) == number)
    require(len(candidates) == 1, "Unknown/ambiguous original enum: " + value.raw)
    return {"raw": value.raw, "symbol": candidates[0], "value": number}


def parse_player(data, values, team_id, roster_symbol, index):
    raw = fields(values, PLAYER_FIELDS, roster_symbol + ":" + str(index))
    scale = data.number(raw["scale"])
    require(isinstance(scale, float) and 0.1 <= scale <= 4, "Invalid original model scale")
    packed = integer(data, raw["number"])
    high, low = packed >> 4, packed & 15
    require(0 <= packed <= 0x99 and high <= 9 and low <= 9, "Invalid BCD jersey number: " + raw["number"].raw)
    statistics = [integer(data, raw[name]) for name in STATS]
    require(all(0 <= stat <= 100 for stat in statistics), "Original player rating out of bounded range")
    return {
        "id": roster_symbol + ":" + str(index).zfill(2),
        "source_static_id": team_id * 16 + index,
        "team_id": team_id, "index": index, "source_line": values[0].line,
        "raw_fields": [raw[name].raw for name in PLAYER_FIELDS],
        "statistics": statistics, "scale_raw": raw["scale"].raw, "scale": scale,
        "size": enum_value(data, raw["size"], "PS_"),
        "skin": enum_value(data, raw["color"], "PC_"),
        "position": enum_value(data, raw["position"], "PP_"),
        "number_raw": raw["number"].raw, "number_packed_bcd": packed,
        "jersey_number": high * 10 + low,
        "jersey_digit_indices": [9 if high == 0 else high - 1, 9 if low == 0 else low - 1],
        "mugshot": data.reference(raw["pii_mugshot"]),
        "selection_name": data.reference(raw["pii_selname"]),
        "hud_name": data.reference(raw["pii_name"]),
        "announcer_raw": raw["ancr"].raw, "announcer": integer(data, raw["ancr"]),
        "lastname": data.string(raw["lastname"]), "firstname": data.string(raw["frstname"]),
        "height_inches": None, "weight_pounds": None, "static_flags": None,
        "static_head_selector": None,
    }


def parse_teams(data, game):
    original, _ = data.array("teaminfo")
    expected = game.expressions.evaluate("NUM_TEAMS")
    require(len(original) == expected == 31, "Expected all 31 source-ordered teams")
    team_symbols, aliases = {}, {}
    for name, macro in game.expressions.macros.items():
        if name.startswith("TEAM_") and macro.parameters is None:
            number = game.expressions.evaluate(name)
            require(isinstance(number, int) and 0 <= number < expected, "Invalid original team ID: " + name)
            if re.fullmatch(r"\d+", macro.text):
                require(number not in team_symbols, "Duplicate original team ID: " + name)
                team_symbols[number] = name
            else:
                aliases.setdefault(number, []).append(name)
    require(set(team_symbols) == set(range(expected)), "Missing original team ID")
    teams, rosters, player_ids = [], set(), set()
    for team_id, row in enumerate(original):
        raw = fields(row, TEAM_FIELDS, "teaminfo/" + str(team_id))
        roster = data.reference(raw["static_data"])
        require(roster is not None and roster not in rosters, "Duplicate/missing original roster reference")
        rosters.add(roster)
        records, _ = data.array(roster)
        require(len(records) == 16, roster + ": expected sixteen original static records")
        prefix = data.string(raw["prefix"])
        require(prefix.isupper() and len(prefix) == 3 and roster == "spd_" + prefix.lower(),
                "Team ID/order/prefix/static roster mismatch")
        require("TEAM_" + prefix in aliases.get(team_id, []) or team_symbols[team_id] == "TEAM_" + prefix,
                "Team table is not in original header ID order: " + prefix)
        attributes = [integer(data, value) for value in raw["attr"]]
        require(len(attributes) == 5 and all(0 <= value <= 5 for value in attributes), "Invalid team attributes")
        players = [parse_player(data, value, team_id, roster, index) for index, value in enumerate(records)]
        for player in players:
            require(player["id"] not in player_ids, "Duplicate original static player ID")
            player_ids.add(player["id"])
        teams.append({
            "id": team_id, "symbol": team_symbols[team_id], "aliases": sorted(aliases.get(team_id, [])),
            "name": data.string(raw["name"]), "home": data.string(raw["home"]),
            "abbrev": data.string(raw["abbrev"]), "prefix": prefix, "roster_symbol": roster,
            "attributes": attributes, "drone_base": integer(data, raw["drone_base"]),
            "image_symbols": [data.reference(raw[name]) for name in TEAM_FIELDS[7:]],
            "captain_player_id": None, "rgb_palette": None,
            "players": players,
        })
    declared = set(re.findall(r"\bconst\s+struct\s+st_plyr_data\s+([A-Za-z_]\w*)\s*\[", data.text))
    require(declared == rosters, "Additional static roster declarations require a separate catalog: " +
            ", ".join(sorted(declared - rosters)))
    return teams


class Bindings:
    def __init__(self, source, parsed):
        self.tree, self.assets = source.tree, {}
        self.headers, self.limbs = {}, {}
        for path in source.texts:
            if not path.startswith("models/"):
                continue
            text = without_comments(source.texts[path])
            for limb in re.findall(r"\bLIMB\s+([A-Za-z_]\w*)\s*=", text):
                require(limb not in self.limbs, "Duplicate original LIMB declaration: " + limb)
                self.limbs[limb] = path
        for path in self.tree:
            if self.tree[path]["type"] == "blob":
                self.headers.setdefault(path.casefold(), []).append(path)
        self.parsed = parsed

    def asset(self, identity, kind, path=None, filename=None, symbol=None):
        candidates = self.headers.get((path or "img3d/" + filename).casefold(), []) if path or filename else []
        require(len(candidates) <= 1, "Ambiguous case-insensitive original asset: " + identity)
        source_path = candidates[0] if candidates else None
        if source_path is not None:
            entry = self.tree[source_path]
            require(entry["type"] == "blob", "Expected source asset blob: " + source_path)
        result = {
            "id": identity, "kind": kind, "runtime_filename": filename, "symbol": symbol,
            "requested_source_path": path, "source_path": source_path,
            "git_blob_sha1": self.tree[source_path]["sha"] if source_path else None,
            "status": "available-in-pinned-tree" if source_path else "missing-from-pinned-tree",
        }
        if identity in self.assets:
            require(self.assets[identity] == result, "Conflicting original asset binding: " + identity)
        self.assets[identity] = result
        return identity

    def limb(self, symbol):
        return self.asset("limb/" + symbol, "limb", path=self.limbs.get(symbol), symbol=symbol)

    def texture(self, filename):
        return self.asset("runtime/" + filename.upper(), "texture", filename=filename)

    def model_schema(self, player, skeleton):
        models, _ = player.array("models")
        jerseys, _ = player.array("jerseys")
        require(len(models) == 32 and len(jerseys) == 8, "Original model/number selector cardinality mismatch")
        output_models = []
        for index, model in enumerate(models):
            name = player.reference(model)
            limbs, _ = skeleton.array(name)
            require(len(limbs) == 15, "Expected fifteen original joint-ordered model limbs: " + name)
            limb_names = [skeleton.reference(value) for value in limbs]
            first_path = self.limbs.get(limb_names[0])
            require(first_path is not None, "Missing base body model header: " + name)
            body = self.parsed[first_path]
            material_symbol = pathlib.PurePosixPath(first_path).stem + "_textures"
            materials, _ = body.array(material_symbol)
            require(len(materials) == 6 and body.reference(materials[-1]) is None,
                    "Expected five terminated original body material slots")
            output_models.append({
                "index": index, "limb_array": name, "header": first_path,
                "material_array": material_symbol,
                "material_slots": [body.string(value) for value in materials[:-1]],
                "limb_symbols": limb_names, "limb_asset_ids": [self.limb(value) for value in limb_names],
            })
        output_jerseys = [{
            "index": index, "limb_symbol": player.reference(value),
            "asset_id": self.limb(player.reference(value))
        } for index, value in enumerate(jerseys)]
        generated = [
            self.asset("generated/models/head.h", "generated-header", path="models/head.h"),
            self.asset("generated/models/helm.h", "generated-header", path="models/helm.h"),
            self.limb("limb_phead_obj"),
        ]
        return {
            "skeleton_asset_id": self.asset("file/models/skeleton.h", "skeleton", path="models/skeleton.h"),
            "skeleton_symbol": "fbplyr_skel", "head_joint_index": 8,
            "custom_head_first_texture_slot": 7, "number_texture_slot": 5,
            "number_matrix_index": 1, "models": output_models, "jerseys": output_jerseys,
            "generated_header_asset_ids": generated,
        }

    def uniforms(self, teams):
        result = []
        for team in teams:
            for home_away in (0, 1):
                atlas = self.texture(team["prefix"] + ("H" if home_away == 0 else "A") + "LOD.WMS")
                for skin in (0, 1):
                    result.append({
                        "team_id": team["id"], "home_away": home_away, "skin": skin,
                        "decal_offset": 0 if skin == 1 else 1,
                        "slot_asset_ids": [
                            self.texture("ARMHND.WMS" if skin == 1 else "ARMHND2.WMS"),
                            self.texture("HEDSHU.WMS" if skin == 1 else "HEDSHU2.WMS"),
                            atlas, atlas, atlas, atlas, self.texture("HEDSHU.WMS"),
                        ],
                    })
        return result

    def heads(self, team, selection):
        limbs, _ = team.array("head_limbs")
        images, _ = team.array("head_images")
        codes, _ = selection.array("secret_heads")
        require(len(limbs) == len(images) == 20, "Original custom-head table mismatch")
        heads = [{
            "index": index, "limb_symbol": team.reference(limb),
            "limb_asset_id": self.limb(team.reference(limb)),
            "texture_asset_id": self.texture(team.string(images[index])),
        } for index, limb in enumerate(limbs)]
        selection_codes, seen = [], set()
        for row in codes:
            require(len(row) == 3 and isinstance(row[0], list) and isinstance(row[1], list), "Unsupported secret-head selection row")
            name = "".join(selection.string(value) for value in row[0])
            code = "".join(selection.string(value) for value in row[1])
            head = integer(selection, row[2])
            require(len(name) == 6 and len(code) == 4 and code.isascii() and code.isdigit() and
                    0 <= head < len(heads), "Invalid secret-head selection reference")
            require((name, code) not in seen, "Duplicate secret-head selection code")
            seen.add((name, code))
            selection_codes.append({"name": name, "code": code, "head_index": head})
        return {"heads": heads, "selection_codes": selection_codes,
                "separate_static_rosters": [],
                "qualification": "These are station-selected head replacements, not extra teams or replacement stat rosters."}


def parse_lineups(nfl):
    offense = nfl.function("reinit_offense_players")
    defense = nfl.function("reinit_defense_players")
    result = {}
    for name in ("std_offense", "kickoff", "kick_return1", "kick_return2", "kick_return3", "punt", "fgoal", "returners"):
        result[name] = numeric_array(nfl, name, offense)
    result["std_defense"] = numeric_array(nfl, "std_defense", defense)
    for name in ("kickoff", "kick_return1", "kick_return2", "kick_return3", "punt", "fgoal", "returners"):
        require(numeric_array(nfl, name, defense) == result[name], "Offense/defense original lineup table mismatch: " + name)
    for name, indices in result.items():
        if name == "returners":
            require(len(indices) == 30 and all(0 <= value <= 2 for value in indices), "Unexpected source returner table")
        else:
            require(len(indices) == 7 and len(set(indices)) == 7 and all(0 <= value < 16 for value in indices),
                    "Invalid original seven-actor lineup: " + name)
    result["source_issue"] = "returners[30] has no entry for source team 30; selecting its return lineup must fail, not assume zero."
    result["offense_selector"] = [
        {"play": "play_punt", "lineup": "punt"},
        {"play": "play_field_goal", "lineup": "fgoal"},
        {"play": "play_fake_field_goal", "lineup": "fgoal"},
        {"play": "play_kickoff_313", "lineup": "kickoff"},
        {"play": "play_kickoff2_313", "lineup": "kickoff"},
        {"play": "play_onside_313", "lineup": "kickoff"},
        {"play": "play_kick_return_short", "lineup": "returners"},
    ]
    result["defense_selector"] = [
        {"play": "play_kickoff_313", "lineup": "returners"},
        {"play": "play_kickoff2_313", "lineup": "returners"},
        {"play": "play_onside_313", "lineup": "returners"},
        {"play": "play_kick_return_short", "lineup": "kickoff"},
    ]
    return result


def constants(data, prefixes=(), suffixes=()):
    output = []
    for symbol, macro in data.expressions.macros.items():
        if macro.parameters is None and (symbol.startswith(prefixes) or symbol.endswith(suffixes)):
            output.append({"symbol": symbol, "raw": macro.text, "value": data.expressions.evaluate(symbol)})
    return sorted(output, key=lambda item: item["symbol"])


def export(source):
    environment = {"SEATTLE": Macro("1")}
    player_header = CData(source.texts["include/player.h"], environment).enums()
    game = CData(source.texts["include/GAME.H"], player_header.expressions.macros)
    environment = game.expressions.macros
    parsed = {path: CData(text, environment) for path, text in source.texts.items()
              if path not in ("include/player.h", "include/GAME.H")}
    team, player, nfl = parsed["teamdata.c"], parsed["player.c"], parsed["nfl.c"]
    teams = parse_teams(team, game)
    bindings = Bindings(source, parsed)
    model_schema = bindings.model_schema(player, parsed["models/skeleton.h"])
    uniforms = bindings.uniforms(teams)
    heads = bindings.heads(team, parsed["lib/select/select.c"])
    away = numeric_array(team, "away_jersey")
    require(len(away) == 31 and all(len(row) == 31 and set(row) <= {0, 1} for row in away),
            "Invalid active uniform collision matrix")
    # Read the explicitly labelled inactive data branch, without altering any source file.
    match = re.search(r"#else\s*//\s*Blitz 99\s*\n(.*?)#endif", source.texts["teamdata.c"], re.DOTALL)
    require(match is not None, "Missing qualified Blitz 99 uniform branch")
    legacy = CData("#define x 0\n" + match.group(1), environment)
    legacy_away = numeric_array(legacy, "away_jersey")
    require(len(legacy_away) == 31 and all(len(row) == 31 for row in legacy_away),
            "Invalid qualified inactive uniform matrix")
    declared_fields = re.search(r"struct\s+st_plyr_data\s*\{(.*?)\};", player_header.text, re.DOTALL)
    require(declared_fields is not None, "Missing original static-player struct")
    field_names = re.findall(r"(?:int|float|void\s*\*|char)\s+([A-Za-z_]\w*)\s*(?:\[\d+\])?\s*;", declared_fields.group(1))
    require(tuple(field_names) == PLAYER_FIELDS, "Original static-player field layout changed")
    source_constants = constants(player_header, ("PS_", "PC_", "PP_", "PPOS_", "MOD_", "PF_"), ("_SPEED",))
    source_constants += constants(game, ("TEAM_", "HA_"))
    tuning = {
        "constants": source_constants,
        "dir49_table": numeric_array(player, "dir49_table"),
        "field_friction_mult_factor": numeric_array(player, "field_friction_mult_factor"),
        "base_speeds": numeric_array(nfl, "base_speeds", nfl.function("lineup_players")),
        "base_turbo_speeds": numeric_array(nfl, "base_turbo_speeds", nfl.function("lineup_players")),
        "diff_settings": numeric_array(team, "diff_settings"),
        "team_ladder": numeric_array(team, "team_ladder"),
        "beat_all_teams": numeric_array(team, "beat_all_teams"),
    }
    issues = [
        {"id": "returners-team-30", "source": "nfl.c:reinit_offense_players/reinit_defense_players",
         "detail": "31 teams but both returners tables have only 30 elements; no guessed entry is exported."},
        {"id": "punt-duplicate", "source": "nfl.c:reinit_offense_players/reinit_defense_players",
         "detail": "The source tests play_punt twice, not play_fake_punt. The latter uses standard lineups."},
        {"id": "not-static-fields", "source": "include/player.h:st_plyr_data/fbteam_data",
         "detail": "Static records have no height, weight, flags, per-player head, RGB palette or named captain. Null means absent, not zero. Captain is a runtime human-station role."},
    ]
    references = [
        {"path": "teamdata.c", "symbol": "teaminfo/spd_*/away_jersey", "detail": "Source team order, all original static fields and active #if 1 Blitz 2000 matrix; inactive Blitz 99 matrix is qualified separately."},
        {"path": "player.c", "symbol": "player_proc/reinit_player", "detail": "dnum = team ID * 16 + static index; ascale = static scale; white selects team_decals[team*2], black selects +1."},
        {"path": "player.c", "symbol": "draw_player/load_uniform/JNUM", "detail": "Body = high-count ? MOD_498 : MOD_266, OR fat/held-ball/jumbo flags; swap JOINT_NECK for station head; slots 2..5 share the team H/A LOD atlas; zero digit maps to atlas digit 9."},
        {"path": "nfl.c", "symbol": "load_player_textures", "detail": "Index collision matrix by min/max team IDs. If nonzero designated home side wears HA_HOME and other HA_AWAY; otherwise both HA_HOME."},
        {"path": "nfl.c", "symbol": "reinit_offense_players/reinit_defense_players", "detail": "Preserve original seven-actor standard/special-team lineup arrays, including the missing team-30 returner entry and duplicate punt test."},
        {"path": "lib/select/select.c", "symbol": "team_select_cntrl/update_tm_data", "detail": "Team selection starts at source ID 0 and wraps 31 IDs. Turbo+left requests randrng(NUM_TEAMS) and at least two loops. QB preview/name is static record 3; five team attribute meters index attr[]."},
        {"path": "lib/select/select.c", "symbol": "secret_heads/check_secret_heads", "detail": "Exact six-character name plus four game-code characters selects a station head index; aliases may share a head. These are public historical game selectors, not account credentials."},
        {"path": "pmisc.c", "symbol": "off_captain/update_stations", "detail": "Captain is runtime 0/1 human teammate, adjusted for always-QB/receiver powerups; no named real-player captain field exists."},
        {"path": "player.c", "symbol": "do_pup_player_scale", "detail": "Only unchanged static scale receives baby 0.8f or big 1.2f multiplier; baby takes priority. Exported scale is the original base, not powerup-mutated."},
        {"path": "cap.c", "symbol": "create-play subsystem", "detail": "This pinned file supplies custom plays, not additional static player/team rosters."},
    ]
    output = {
        "schema": SCHEMA,
        "provenance": {"repository": REPOSITORY, "commit": REVISION, "tree": TREE,
                       "manifest_sha256": MANIFEST_SHA256, "content_pin_verified": True, "inputs": source.pins},
        "profile": {"platform": "SEATTLE", "release": True, "defines": ["SEATTLE"],
                    "disabled_flags": list(DISABLED_FLAGS), "teams": 31, "static_records_per_team": 16,
                    "active_per_team": 7, "maximum_humans": 4, "scale_macro": team.expressions.macros["SC"].text,
                    "float_arithmetic": "IEEE-754 binary32", "player_field_order": list(PLAYER_FIELDS)},
        "teams": teams, "uniforms": uniforms, "actor_model_schema": model_schema,
        "lineups": parse_lineups(nfl),
        "selection": {"away_jersey": away, "legacy_away_jersey": legacy_away,
                      "legacy_qualifier": "Inactive #else Blitz 99 branch; its team ordering differs from the active 2000 table.",
                      "secret_heads": heads},
        "tuning": tuning, "references": references, "source_issues": issues,
        "assets": sorted(bindings.assets.values(), key=lambda value: value["id"]),
        "missing_asset_ids": sorted(key for key, value in bindings.assets.items() if value["source_path"] is None),
    }
    validate_dataset(output)
    return output


def validate_dataset(doc):
    require(doc["schema"] == SCHEMA, "Unknown roster schema")
    teams, player_ids, static_ids = doc["teams"], set(), set()
    require(len(teams) == 31 and [team["id"] for team in teams] == list(range(31)), "Missing/duplicate/out-of-order team IDs")
    for team in teams:
        require(len(team["players"]) == 16, "Expected sixteen original static records")
        for index, player in enumerate(team["players"]):
            require(player["index"] == index and player["team_id"] == team["id"] and
                    player["source_static_id"] == team["id"] * 16 + index and
                    player["id"] == team["roster_symbol"] + ":" + str(index).zfill(2), "Unstable original player identity")
            require(player["id"] not in player_ids and player["source_static_id"] not in static_ids, "Duplicate original player identity")
            player_ids.add(player["id"])
            static_ids.add(player["source_static_id"])
    require(len(player_ids) == 496, "Expected 496 original static player records")
    assets = {asset["id"]: asset for asset in doc["assets"]}
    require(len(assets) == len(doc["assets"]), "Duplicate asset identity")
    referenced = []
    for uniform in doc["uniforms"]:
        require(0 <= uniform["team_id"] < 31 and uniform["skin"] in (0, 1) and uniform["home_away"] in (0, 1) and
                len(uniform["slot_asset_ids"]) == 7, "Invalid original uniform selector")
        referenced.extend(uniform["slot_asset_ids"])
    require(len(doc["uniforms"]) == 124 and len({(u["team_id"], u["home_away"], u["skin"]) for u in doc["uniforms"]}) == 124,
            "Missing/duplicate team/uniform/skin selector")
    schema = doc["actor_model_schema"]
    referenced.extend([schema["skeleton_asset_id"]] + schema["generated_header_asset_ids"])
    for model in schema["models"]:
        referenced.extend(model["limb_asset_ids"])
    referenced.extend(jersey["asset_id"] for jersey in schema["jerseys"])
    for head in doc["selection"]["secret_heads"]["heads"]:
        referenced.extend([head["limb_asset_id"], head["texture_asset_id"]])
    require(set(referenced).issubset(assets), "Missing asset binding reference")
    require(doc["missing_asset_ids"] == sorted(key for key, asset in assets.items() if asset["source_path"] is None),
            "Missing-asset list does not match actual unresolved bindings")
    bears = teams[5]["players"][3]
    require(bears["firstname"] == "Cade" and bears["lastname"] == "McNown" and bears["skin"]["symbol"] == "PC_WHITE" and
            bears["jersey_number"] == 8 and bears["position"]["symbol"] == "PP_QB",
            "Pinned Bears/Cade McNown selector cross-check failed")


def output_path(path):
    path = pathlib.Path(path).absolute()
    resolved = path.resolve()
    allowed = (ROOT / ".squad/.scratch/SourceData", ROOT / "tools/rosters/SourceData")
    require(any(resolved.is_relative_to(root.resolve()) for root in allowed),
            "Output must be explicit ignored .squad/.scratch/SourceData/ or tools/rosters/SourceData/")
    require(path == resolved and path.suffix == ".json", "Output cannot traverse symlinks and must end in .json")
    return path


def write_output(path, data):
    # Replace the directory entry, never truncate a potentially hard-linked input.
    pending = path.with_name("." + path.name + ".writing-" + str(os.getpid()))
    created = False
    try:
        with pending.open("xb") as stream:
            created = True
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(pending, path)
    finally:
        if created and pending.exists():
            pending.unlink()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=pathlib.Path, help="Explicit local source root; never downloaded")
    parser.add_argument("--output", required=True, type=pathlib.Path, help="Ignored local JSON destination")
    args = parser.parse_args(argv)
    try:
        destination = output_path(args.output)
        output_manifest = output_path(destination.with_name(destination.stem + ".manifest.json"))
        source = SourceSet(args.source_root)
        require(not destination.resolve().is_relative_to(source.root), "Output cannot overwrite or modify sourceRoot")
        require(not output_manifest.resolve().is_relative_to(source.root), "Manifest cannot overwrite or modify sourceRoot")
        doc = export(source)
        data = json_bytes(doc)
        manifest = {"schema": "original-team-rosters-output/v1", "dataset": destination.name,
                    "schema_version": SCHEMA, "sha256": digest(data), "bytes": len(data),
                    "source_revision": REVISION, "source_manifest_sha256": MANIFEST_SHA256,
                    "teams": len(doc["teams"]), "static_players": sum(len(team["players"]) for team in doc["teams"]),
                    "uniform_variants": len(doc["uniforms"]),
                    "model_selectors": len(doc["actor_model_schema"]["models"]),
                    "custom_heads": len(doc["selection"]["secret_heads"]["heads"]),
                    "custom_head_codes": len(doc["selection"]["secret_heads"]["selection_codes"]),
                    "missing_asset_ids": doc["missing_asset_ids"]}
        destination.parent.mkdir(parents=True, exist_ok=True)
        write_output(destination, data)
        write_output(output_manifest, json_bytes(manifest))
        print(json.dumps(manifest, sort_keys=True))
        return 0
    except (OSError, SourceError, KeyError, RecursionError) as exc:
        print("roster export rejected: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
