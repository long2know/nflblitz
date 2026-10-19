#!/usr/bin/env python3
"""Prepare only authenticated possession CI operands; no imported assets, services or native Unity."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import time

from fetch_inputs import ROOT, HERE, digest, new_output, require, verify_bytes, verify_directory

HUD_SHA256 = "258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435"
FIRE_SHA256 = "90a134af37fe6d43f13977f7903315ee0d9a7f37f700669b365f45dd7c941d56"
SPEECH_SHA256 = "ac9bdf7c37c8f3d9d1e7184ca96e2d9c28aad2e157940d05194513b8f06da5df"
AUTHORIZATION_REFERENCE = "owner-directed-local-loose-helmet-no-redistribution"


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


def prepare(original, output):
    original = Path(original).absolute()
    recipe, before = verify_directory(original)
    output = new_output(output)
    output.mkdir(parents=True)
    started = time.monotonic()
    for pin in recipe["authority"]:
        raw = (original / pin["repository"] / pin["path"]).read_bytes()
        verify_bytes(raw, pin, pin["authority"])
        path = output / "authority" / pin["authority"]
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    hud = module("possession_ci_hud_prepare", ROOT / "tools/interface/prepare.py")
    work = output / "work"
    try:
        files = hud.prepare(original / "nfl", original / "midway", work)
        require(digest(files["interface.json"]) == HUD_SHA256, "Prepared actual HUD pin changed.")
        for name, raw in files.items():
            path = output / "hud" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
    finally:
        if work.exists():
            shutil.rmtree(work)
    fire = module("possession_ci_fire_prepare", ROOT / "tools/fire-spfx/prepare.py")
    fire_receipt = fire.prepare(original / "nfl", output / "fire", AUTHORIZATION_REFERENCE, with_helmet=True)
    require(fire_receipt["manifestSha256"] == FIRE_SHA256, "Prepared actual v3 FX pin changed.")
    speech = module("possession_ci_speech_prepare", ROOT / "tools/interface/prepare_possession.py")
    payload, speech_receipt = speech.convert(original / "nfl", original / "midway/include/goose/sound.h")
    require(digest(payload) == SPEECH_SHA256 and len(payload) == 40, "Prepared original speech operands changed.")
    (output / "speech").mkdir()
    (output / "speech/possession-speech.bytes").write_bytes(payload)
    require(verify_directory(original)[1] == before, "Authenticated originals changed during preparation.")
    files = {p.relative_to(output).as_posix(): dict(bytes=p.stat().st_size, sha256=digest(p.read_bytes()))
             for p in sorted(output.rglob("*")) if p.is_file()}
    require(len(files) == 50, "Exact prepared13-authority/4-HUD/32-FX/1-speech inventory changed.")
    receipt = dict(schema="possession-service-ci-preparation/v1", status="passed",
                   originalReceiptSha256=digest((original / "receipt.json").read_bytes()),
                   originalInputs=before, files=files, seconds=round(time.monotonic() - started, 3),
                   authorizationReference=AUTHORIZATION_REFERENCE, speechSource=speech_receipt,
                   originalMaterialAndDerivativesIgnored=True, nativeUnityOrPixelsQualified=False)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        receipt = prepare(args.original, args.output)
        print(json.dumps(dict(status=receipt["status"], files=len(receipt["files"]),
                              seconds=receipt["seconds"], hudSha256=HUD_SHA256,
                              fireSha256=FIRE_SHA256, speechSha256=SPEECH_SHA256)))
        return 0
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        print("Possession CI preparation rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
