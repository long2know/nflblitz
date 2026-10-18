"""Compile pinned original meter continuations/velocity functions under an explicit authored-image seam."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
SOURCE_PINS = {
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "handicap.c": "871424596fb9786af3602a49cc9d20c466df8ca1",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
    "include/handicap.h": "45eef32b32721196e4575e59a0fd2ce01e0fbbdb",
    "include/PlayStuf.h": "3df5e8e8a3f478c3b02860a401c950d1e83c5ad3",
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
}
INTERFACE_PINS = {
    "plyrinfo.c": "e955e1c98a0fd0123ce0b865ddafdfd7c32c59d4",
    "img2d/plyrinfo.wif": "7cacc7475c090be42bc70903ca2d54717234b660",
    "img2d/ScPlate.wif": "e65a7fbb7d506cdfce539bda87ad34f6f4baf51c",
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def closing(data, start):
    depth = 0
    for token in re.finditer(rb'/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|[{}]', data[start:], re.S):
        if token[0] == b"{":
            depth += 1
        elif token[0] == b"}":
            depth -= 1
            if depth == 0:
                return start + token.end()
    raise ValueError("Unclosed original source block")


def build(source, interface, output):
    output = Path(output).absolute()
    if not output.is_relative_to(ROOT / ".squad/.scratch") or output.exists():
        raise ValueError("Use a new ignored local reference output.")
    output.mkdir(parents=True)
    raw, hashes, windows = {}, {}, []
    for base, pins in ((Path(source), SOURCE_PINS), (Path(interface), INTERFACE_PINS)):
        for name, expected in pins.items():
            path = base / name
            data = path.read_bytes()
            blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
            if blob != expected:
                raise ValueError("Pinned original blob differs: " + str(path) + " actual=" + blob)
            raw[name] = data
            hashes[str(path.absolute())] = digest(data)

    def select(name, start, end, identity):
        data = raw[name][start:end]
        windows.append(dict(path=name, identity=identity, first_line=raw[name][:start].count(b"\n") + 1,
                            start_byte=start, end_byte_exclusive=end, sha256=digest(data)))
        return data.decode("latin1")

    def function(name, symbol):
        matches = list(re.finditer(rb"(?m)^(?:static\s+)?(?:void|int)\s+" + symbol.encode()
                                   + rb"\s*\([^;{}]*\)\s*\{", raw[name]))
        if len(matches) != 1:
            raise ValueError("Expected one active original function: " + symbol)
        start = matches[0].start()
        return select(name, start, closing(raw[name], raw[name].index(b"{", matches[0].end() - 1)), symbol)

    names = {"VELBASE", "GRAVITY", "LESSER", "GREATER", "PF_FLIP", "JOINT_LANKLE", "JOINT_RANKLE",
             "TEAM1_MASK", "TEAM2_MASK", "TEAM_MASK", "ISHUMANTEAM", "ISDRONETEAM", "PLYRH",
             "GS_ATTRACT_MODE", "GS_GAME_MODE", "BARY", "BARX1", "BARX2"}
    prefixes = ("PUNT_", "GOAL_", "FG_", "PLF_", "GM_", "GF_", "BF_")
    constants = []
    for name in ("include/GAME.H", "include/player.h", "include/ani3d.h", "include/handicap.h",
                 "include/PlayStuf.h", "plyrinfo.c"):
        for match in re.finditer(rb"(?m)^#define[ \t]+(\w+)[^\r\n]*(?:\r?\n|$)", raw[name]):
            symbol = match[1].decode()
            if symbol in names or symbol.startswith(prefixes):
                constants.append(select(name, match.start(), match.end(), "macro/" + symbol))
    player = raw["include/player.h"]
    start = player.index(b"enum {", player.index(b"// Team attributes"))
    constants.append(select("include/player.h", start, player.index(b"};", start) + 2, "team-attribute-enum"))
    handicap = raw["handicap.c"]
    match = re.search(rb"(?m)^(?:int|float)\s+fg_bonus_ht\s*\[12\]\s*=", handicap)
    if match is None:
        raise ValueError("Original twelve FG handicap bonuses missing.")
    start = match.start()
    constants.append(select("handicap.c", start, handicap.index(b";", closing(handicap, handicap.index(b"{", start))) + 1,
                            "fg_bonus_ht"))

    spec = importlib.util.spec_from_file_location("kick_actual_authored_wif", ROOT / "tools/interface/prepare.py")
    authored = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = authored
    spec.loader.exec_module(authored)
    images = {}
    for name in ("img2d/plyrinfo.wif", "img2d/ScPlate.wif"):
        images.update(authored.wif_images(raw[name], name))
    symbols = ("k_box", "k_bar", "k_marker", "k_ball", "k_windtext", "k_windarrow", "p_meter", "playresult")
    definitions = []
    metrics = {}
    for symbol in symbols:
        image = images[symbol]
        metrics[symbol] = dict(width=image.width, height=image.height, anchor_x=image.ax, anchor_y=image.ay,
                               source=image.source, pixels_sha256=digest(image.rgba))
        definitions.append("static image_info_t " + symbol + "={" + ",".join(
            str(value) + ".0f" for value in (image.width, image.height, image.ax, image.ay)) + ",0,1};")
    functions = [function("plyrinfo.c", name) for name in ("kick_meter_exit", "punt_meter_exit")]
    instrumentation = []
    for name, probe in (
        ("kick_meter_proc", "km_probe(1,pos,vel,wo2,wind,mark_lx,mark_rx,xoff)"),
        ("punt_meter_proc", "km_probe(2,pos,vel,wo2,0,0,0,0)"),
    ):
        body = function("plyrinfo.c", name)
        count = len(re.findall(r"\bsleep\(\s*(?:1|30)\s*\)", body))
        body = re.sub(r"\bsleep\(\s*(1|30)\s*\)", lambda match: "(" + probe + ", sleep(" + match[1] + "))", body)
        functions.append(body)
        instrumentation.append(dict(function=name, added_before_sleep_probes=count,
                                    original_maths_branches_and_sleep_durations_unchanged=True))
    functions += [function("player.c", "set_punt_ball_vels"), function("player.c", "set_field_goal_ball_vels")]
    sequences = raw["plyrseq.c"]
    start = sequences.index(b"punt_ball_anim:")
    punt_sequence = select("plyrseq.c", start, sequences.index(b"kickball_anim:", start), "punt-release-bytecode")
    if not re.search(r"ANI_RUNTOFRAME,4,1.*?\.word\s+punt_ball,0,0,0", punt_sequence, re.S):
        raise ValueError("Original punt frame4 ANI_CODE contract changed.")
    start = sequences.index(b"kickball_anim:")
    kick_sequence = select("plyrseq.c", start, sequences.index(b"#;;;;;;;;", start), "FG-animation-no-physics-callback")
    if re.search(r"\.word\s+kick_ball\b", kick_sequence):
        raise ValueError("Original FG is not a delayed animation physics callback.")
    kicker = function("player.c", "_mode_kicker")
    original_fg = kicker[:kicker.index("if (game_info.play_type == PT_KICKOFF)")]
    if not re.search(r"odistxz.*?< 8\.0f.*?change_anim\(pobj, kickball_anim\).*?kick_ball\(pobj\);", original_fg, re.S):
        raise ValueError("Original immediate kickable-holder FG caller changed.")
    template = (SUITE / "driver.c").read_text()
    text = (template.replace("/* ORIGINAL_CONSTANTS */", "\n".join(constants))
            .replace("/* AUTHORED_IMAGE_SEAM */", "\n".join(definitions))
            .replace("/* ORIGINAL_FUNCTIONS */", "\n".join(functions)))
    path, binary = output / "original-kick-components.c", output / "original-kick-components"
    path.write_bytes(text.encode("latin1"))
    work = output / "work"
    work.mkdir()
    command = ["gcc", "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
               "-fexcess-precision=standard", "-Werror=implicit-function-declaration", str(path), "-lm", "-o", str(binary)]
    process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=120,
                             env=dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work)))
    (output / "compiler.log").write_text(process.stdout + process.stderr)
    receipt = dict(schema="portable-authored-kick-original-C/v1",
                   source_revision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                   source_tree="a7e0352125d46075b7d98e87253ab89b9160a4c7",
                   original_sha256=hashes, original_windows=windows, instrumentation=instrumentation,
                   authored_image_metrics=metrics, command=command, exit_code=process.returncode,
                   verified_release_contracts=["Original punt ANI_CODE after RUNTOFRAME4.",
                       "Original FG _mode_kicker calls kick_ball immediately within8; kickball_anim has no kick_ball callback."],
                   native_generated_image_info_or_original_ABI_claimed=False,
                   seams=["Original meter functions run as resumable host ucontext continuations.",
                          "Only observational probes before original sleep calls; gameplay expressions unchanged.",
                          "image_info_t inputs are explicit pinned AUTHORED WIFF metadata, NOT generated native metrics.",
                          "Platform drawing/text/sound/flasher handles are non-gameplay observation seams; no audio synthesized.",
                          "Original velocity functions and original constants/handicap table compiled unchanged.",
                          "No original MIPS ABI/Goose/libc RNG/Composit/Texus execution claimed."])
    (output / "provenance.json").write_text(json.dumps(receipt, indent=2) + "\n")
    if process.returncode or process.stderr or process.stdout:
        raise RuntimeError("Original component compiler must have zero diagnostics:\n" + process.stdout + process.stderr)
    return binary, receipt
