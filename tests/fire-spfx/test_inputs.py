#!/usr/bin/env python3
"""Real portable preparer/authorized-boundary/inventory checks; private originals never tracked."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def inventory(root):
    metadata = load("fire_metadata_check", ROOT / "tests/test-unity-metadata.py")
    expected = {
        "Contracts/SourceFireSpfxContracts.cs", "AssetImport/SourceFireSpfxData.cs", "Runtime/SourceFireSpfxRuntime.cs",
        "Presentation/SourceFireSpfxRenderModel.cs", "Presentation/SourceFireSpfxAsset.cs",
        "Presentation/SourceFireSpfxRenderer.cs", "Editor/SourceFireSpfxImporter.cs",
        "Gameplay/GameSimulation.FireSpfx.cs",
        "Contracts/SourceKickMeterContracts.cs", "Gameplay/SourceKickMeterProcess.cs",
    }
    scripts = root / "Assets/Scripts"
    base = subprocess.check_output(["git", "ls-tree", "-r", "--name-only", "5f570c132288871de2d1461860e19f85975caf5f", "Assets"], cwd=ROOT, text=True).splitlines()
    baseline_scripts = {name for name in base if name.endswith(".cs")}
    actual_scripts = {p.relative_to(root).as_posix() for p in scripts.rglob("*.cs")}
    assert actual_scripts - baseline_scripts == {"Assets/Scripts/" + p for p in expected}, "Exact new script registration differs"
    assert baseline_scripts <= actual_scripts, "Existing source inventory must remain intact"
    baseline_meta = [p for p in base if p.endswith(".meta")]
    assert len(baseline_meta) == 124
    for relative in baseline_meta + ["Assets.meta"]:
        pinned = subprocess.check_output(["git", "show", "5f570c132288871de2d1461860e19f85975caf5f:" + relative], cwd=ROOT)
        assert (root / relative).read_bytes() == pinned, "Changed baseline GUID/meta: " + relative
    checked = metadata.check_inventory(root / "Assets")
    added_meta = {p.relative_to(root).as_posix() for p in (root / "Assets").rglob("*.meta")} - set(baseline_meta)
    expected_meta = {"Assets/Scripts/" + p + ".meta" for p in expected}
    expected_meta.update("Assets/Scripts/Presentation/" + p + ".meta"
                         for p in ("SourceFireSpfx.shader", "SourcePlayerFlash.shader"))
    assert added_meta == expected_meta, "Exact combined feature metadata registration differs"
    assert checked["metadata"] == len(baseline_meta) + 1 + len(expected_meta)
    assert checked["guids"] == checked["metadata"]
    for relative in expected:
        if relative.startswith(("Contracts/", "Runtime/", "AssetImport/")):
            assert "using UnityEngine" not in (scripts / relative).read_text(), "Core/data contracts must stay pure"
    shader = (scripts / "Presentation/SourceFireSpfx.shader").read_text()
    for token in ("ZWrite On", "ZTest Always", "Cull [_Cull]",
                  "Blend SrcAlpha OneMinusSrcAlpha, SrcAlpha OneMinusSrcAlpha", "texel.rgb * texel.a", "portableDepth.z",
                  "-2 * _SourceRaster.z * v.vertex.x / _SourceRaster.x"):
        assert token in shader, "Actual Unity shader material contract changed: " + token
    renderer = (scripts / "Presentation/SourceFireSpfxRenderer.cs").read_text()
    for forbidden in ("ParticleSystem", "UnityEngine.Random", "Time.deltaTime", "Time.time", "LateUpdate(", "void Update("):
        assert forbidden not in renderer, "Presentation must not age/draw random/fallback effects: " + forbidden
    for token in ("new Texture2D", "TextureFormat.RGBA32, false, true", "TextureWrapMode.Repeat", "FilterMode.Bilinear",
                  "commands.DrawMesh", "draw.TextureFrame", "consumer.Consume(frame)",
                  "SourceVector3 p = draw.CameraVertices[i]", "vertices[i] = new Vector3(p.X, p.Y, p.Z)",
                  "new Vector4(512f, frame.Basis.VerticalResolution, 664f",
                  'properties.SetVector("_SourceRaster", raster)', "commands.DrawMesh(mesh, Matrix4x4.identity, material"):
        assert token in renderer, "Real Unity frame/texture/draw consumer missing: " + token
    return dict(checked, baseline_metadata_preserved=len(baseline_meta), root_meta_preserved=True,
                baseline_scripts=len(baseline_scripts), new_scripts=len(expected),
                all_current_scripts=len(actual_scripts))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--original-root", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(HERE / ".artifacts") and not output.exists()
    output.mkdir(parents=True)
    prep = load("fire_portable_preparer_tests", ROOT / "tools/fire-spfx/prepare.py")
    rejected = []

    def reject(name, expected_message, fn):
        try:
            fn()
        except (ValueError, OSError) as error:
            assert expected_message in str(error), f"{name}: failed for wrong cause: {error}"
            rejected.append(name)
        else:
            raise AssertionError("Required rejection survived: " + name)

    original = args.original_root.resolve()
    before = {p["path"]: hashlib.sha256((original / p["path"]).read_bytes()).hexdigest()
              for p in json.loads((ROOT / "tools/fire-spfx/inputs.json").read_text())["inputs"]}
    raw = (original / "models/spfx.sdl").read_bytes()
    geometry = prep.geometry_from_sdl(raw)
    assert len(geometry) == 148 and struct.unpack_from("<3I", geometry, 8) == (4, 4, 2)
    invalid = raw.replace(b"polygon ((0, 1, 2)", b"polygon ((0, 1, 4)", 1)
    assert invalid != raw
    reject("real SDL vertex indices", "vertex index out of range", lambda: prep.geometry_from_sdl(invalid))
    invalid_uv = raw.replace(b"polygon ((0, 1, 2),(0, 1, 2)", b"polygon ((0, 1, 2),(0, 1, 4)", 1)
    assert invalid_uv != raw
    reject("real SDL UV indices", "texture index out of range", lambda: prep.geometry_from_sdl(invalid_uv))
    reject("SDL grammar version", "pinned SDL7.51", lambda: prep.geometry_from_sdl(raw.replace(b"version = 7.51", b"version = 7.52")))
    reject("output traversal alias", "Dot-segment", lambda: prep.prepare(original, ROOT / ".squad/.scratch/../forbidden-input-output", "fixture"))
    reject("tracked asset output", "ignored roots", lambda: prep.prepare(original, ROOT / "Assets/SourceFireOriginals", "fixture"))
    reject("missing authorization", "authorization reference", lambda: prep.prepare(original, output / "missing-auth", ""))
    # Wrong-input tests own only new ignored copies; never revise the shared pinned original inputs.
    clone = output / "wrong-originals"
    (clone / "models").mkdir(parents=True)
    shutil.copyfile(original / "models/spfx.sdl", clone / "models/spfx.sdl")
    (clone / "img3d").mkdir()
    for path in (original / "img3d").glob("trail*.tga"):
        shutil.copyfile(path, clone / "img3d" / path.name)
    damaged = clone / "img3d/trail17.tga"
    altered = bytearray(damaged.read_bytes()); altered[18] ^= 1; damaged.write_bytes(altered)
    private_output = ROOT / ".squad/.scratch" / ("fire-negative-" + output.name)
    reject("original payload SHA/blob", "Wrong original input pin", lambda: prep.prepare(clone, private_output, "fixture"))
    damaged.unlink()
    reject("missing seventeenth TGA", "Missing required original input", lambda: prep.prepare(clone, private_output, "fixture"))
    alias = output / "symlink-originals"
    alias.symlink_to(original, target_is_directory=True)
    reject("authorized root symlink", "Symlink asset path", lambda: prep.prepare(alias, private_output, "fixture"))
    alias.unlink()
    after = {path: hashlib.sha256((original / path).read_bytes()).hexdigest() for path in before}
    assert before == after, "Frozen original payloads changed"
    shutil.rmtree(clone)
    result = {"status": "passed", "originals_unchanged": True, "rejected": rejected,
              "inventory": inventory(args.source_root.resolve()), "assetPins": before,
              "originalRoot": str(original), "invocation": [sys.executable, *sys.argv]}
    (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print("PASS real preparer: " + str(len(rejected)) + " negative controls; all18 originals unchanged; exact" +
          str(result["inventory"]["new_scripts"]) + " scripts/" + str(result["inventory"]["guids"] - 125) +
          " new GUIDs/old124 metadata preserved.")


if __name__ == "__main__":
    main()
