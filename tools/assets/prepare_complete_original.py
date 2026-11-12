#!/usr/bin/env python3
"""Prepare only the exact original player shadows and reachable cabinet artwork."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).absolute().parents[2]
spec = importlib.util.spec_from_file_location("completion_interface", ROOT / "tools/interface/prepare.py")
ui = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = ui
spec.loader.exec_module(ui)
PINS = {
    "img3d/PSSHOULD.TGA": "b05993dc691d1c6d7632c26fa37092d09ebba4d9",
    "img3d/PSLIMB.TGA": "a589bfe2eb87dea0fff00421fdd81c25aad65ea4",
    "img3d/PSTORSO.TGA": "cbe1aebd1249da69cf6136e1072c20dc532901c2",
    "img3d/PSHAND.TGA": "e6f69144c2ff170d25afcc0398c0f2d68ed2c700",
    "img2d/halfstat.wif": "00598a8017f378c8852ed1d6f3de6a572222cc5d",
    "img2d/tipspg.wif": "9648c037dc0a43a8e0190c869d36a876018b519e",
    "img2d/winteams.wif": "6ab6de49adf3c08ad472e78dbeb62762424f443b",
    "lib/select/select.c": "26c3955a4081aff73d6e4f1f7633ae86f24e2c4a",
    "plyrinfo.c": "e955e1c98a0fd0123ce0b865ddafdfd7c32c59d4",
}
RECORD_PINS = {
    "img2d/makeplay.wif": "a0354072000ce15434f364296e0ca315f72da20d",
    "cap.c": "42605264092a30f861f5390535bbe217aac13b66",
    "img2d/ynplaq3.wif": "fdcd11153d0140a359e042ab7c83f676dc432855",
    "img2d/ynplaqu2.wif": "9c3b202bb3f19cff1be1eb87abf5c7d27722bcbd",
    "img2d/bkgrnd.wif": "762434da75d97911f5a61ce6cf44c58b2cb5bfde",
}
PREMATCH_PINS = {
    "img2d/stats.wif": "1012becbeca9ef14dc80bb33fcb646f54324e6ae",
    "img2d/teamsel2.wif": "da3175d989cabf9bd874da30d8c381301b20fe2a",
    "img2d/tmsel.lod": "5449a1b62d61dc68f1ba4b89f5f03f487e2aaec9",
}


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("ascii")


def prepare(source, output, records=False, prematch=False):
    source, output = source.absolute(), output.absolute()
    ui.require(not prematch or records, "The complete prematch artwork requires the record artwork family.")
    ui.require(source.resolve() == source and output.resolve() == output
               and output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists(),
               "Choose a create-new physical ignored artwork output.")
    ui.require(not output.is_relative_to(source) and not source.is_relative_to(output),
               "Original artwork input/output must not overlap.")
    inputs, outputs, rasters = {}, {}, []
    pins = dict(PINS, **RECORD_PINS) if records else dict(PINS)
    if prematch:
        pins.update(PREMATCH_PINS)
    for path, blob in pins.items():
        raw = ui.assets.read_file(source, path)
        ui.require(ui.blob(raw) == blob, "Original artwork/source Git blob differs: " + path)
        inputs[path] = raw
        outputs["source/" + path] = raw
    for slot, path in enumerate(list(PINS)[:4]):
        decoded = ui.assets.decode_tga(inputs[path], path)
        ui.require(decoded["width"] == decoded["height"] == 32 and decoded["sourceFormat"] == "TGA32",
                   "The four original32x32 alpha-bearing shadows are required.")
        name = Path(path).stem.lower()
        rgba = decoded["mips"][0]
        ui.require(any(rgba[3::4]) and any(0 < a < 255 for a in rgba[3::4]),
                   "Original shadows require their actual nonbinary authored alpha.")
        target = "rgba/" + name + ".rgba"
        outputs[target] = rgba
        rasters.append(dict(id=name, kind="shadow", slot=slot, width=32, height=32, anchorX=0, anchorY=0,
                            path=target, sha256=ui.sha256(rgba), sourcePath=path,
                            sourceGitBlob=PINS[path], sourceSha256=ui.sha256(inputs[path]),
                            representation="original TGA32 authored RGBA; bottom-first; native texus/WMS unqualified"))
    image_inputs = [
        ("img2d/halfstat.wif", ("halfstat", "finalstat")),
        ("img2d/tipspg.wif", tuple("cheerpg" + str(n) for n in range(1, 8))),
        ("img2d/winteams.wif", None),
    ]
    if records:
        image_inputs.append(("img2d/makeplay.wif", None))
        normal = tuple(prefix + str(n) for prefix in ("initplq", "pinplq", "ynplq") for n in range(1, 5))
        keys = tuple(str(n) for n in range(10)) + tuple("abcdefghijklmnopqrstuvwxyz") + (
            "ast", "del", "dsh", "end", "exc", "spc", "pdel", "pspc")
        normal += tuple(side + "_" + key for side in ("l", "r") for key in keys)
        image_inputs.append(("img2d/ynplaq3.wif", normal))
        image_inputs.append(("img2d/ynplaqu2.wif", (
            "cbox", "cboxt", "mcreate", "mcreatet", "menter", "mentert", "mselect", "mselectt")))
        image_inputs.append(("img2d/bkgrnd.wif", ("mbg",)))
    if prematch:
        image_inputs.extend((
            ("img2d/stats.wif", ("statpl_r", "statfade", "sstatplr")),
            ("img2d/ynplaqu2.wif", ("mrecnf", "mrecf")),
            ("img2d/teamsel2.wif", ("gridbox",)),
        ))
    for path, wanted in image_inputs:
        images = ui.wif_images(inputs[path], path, None if wanted is None else set(wanted))
        names = wanted if wanted is not None else sorted(images)
        for name in names:
            image = images[name]
            target = "rgba/" + name + ".rgba"
            rgba = ui.assets.bottom_first_rgba(image.width, image.height, image.rgba, True)
            outputs[target] = rgba
            rasters.append(dict(id=name, kind="image", slot=-1, width=image.width, height=image.height,
                                anchorX=image.ax, anchorY=image.ay, path=target, sha256=ui.sha256(rgba),
                                sourcePath=path, sourceGitBlob=pins[path], sourceSha256=ui.sha256(inputs[path]),
                                representation="original selected WIFF IMG/PAL RGBA; bottom-first; native Composit packing unqualified"))
    source_text = inputs["plyrinfo.c"].decode("ascii")
    winner_table = ui.array(source_text, "message_win_tex")
    winners = re.findall(r'"(win_[a-z0-9]+)\.wms"', winner_table)
    ui.require(len(winners) == 31 and all(name in {r["id"] for r in rasters} for name in winners),
               "All31 source-selected team-win images are required.")
    cheer = ui.array(inputs["lib/select/select.c"].decode("ascii"), "cheer_imgs")
    ui.require(re.findall(r"&cheerpg(\d+)(?![_\d])", cheer) == list(map(str, range(1, 8))),
               "Original seven-way source coaching background order changed.")
    manifest = dict(schema="original-complete-presentation/v1",
                    profile="original-teams-complete-original303-prematch" if prematch else
                        "original-teams-complete-original303-records" if records else "original-teams-complete-original303",
                    sourceRevision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                    sources=[dict(path="source/" + path, sourcePath=path, gitBlob=blob,
                                  sha256=ui.sha256(inputs[path]), bytes=len(inputs[path]))
                             for path, blob in pins.items()],
                    rasters=rasters, teamWins=winners,
                    qualifications=["Original authenticated authoring pixels, not generated substitutes.",
                                    "Original WMS/mip/Composit/native pixels and win sparkles remain separate qualification."])
    outputs["manifest.json"] = canonical(manifest)
    output.mkdir(parents=True)
    for path, raw in outputs.items():
        target = output / path
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(raw)
    for path, raw in outputs.items():
        ui.require((output / path).read_bytes() == raw, "Prepared original artwork output changed: " + path)
    return dict(schema="original-complete-presentation-preparation/v1", output=str(output),
                manifestSha256=ui.sha256(outputs["manifest.json"]), shadows=4,
                images=len(rasters) - 4, files=[dict(path=path, bytes=len(raw), sha256=ui.sha256(raw))
                                              for path, raw in sorted(outputs.items())])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--records", action="store_true")
    parser.add_argument("--prematch", action="store_true")
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.output, args.records, args.prematch), indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
