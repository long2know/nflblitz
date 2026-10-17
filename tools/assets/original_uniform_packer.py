"""Build/call the actual pinned original c_hack packer; never approximate packing."""

import json
import os
from pathlib import Path
import subprocess
import uuid

import prepare_player as files

REVISION = "1792293aaaea00a919b59e8c05716978e85b4333"
PINS = {
    "c_hack.c": "e5c2911052fe292758f2bb33056b149fcd152155",
    "pack.c": "3c750d86602ffde1e171f03b75277e398e135ff5",
    "pack.h": "05b63935da07db834d129c138e451428240ea386",
    "bagger.c": "3f09bc59caf66cfcfeae82b06b814a71c8b19dab",
    "bagger.h": "a2e4ad9546f6ef12e01cee550bbd25bf88523db1",
    "tga.c": "018b138c0fa975a1044a07c0d1b1c4778532833b",
    "tga.h": "c8ae30aced9ffc3c8f59c8cd3fd2bbbd8df22419",
    "system.h": "9ccf98816054d7e0bb4e7aeaaf5a1cc081f15a65",
    "util.c": "3abfd0c1d29b0835327f54d9fa915c782bdb245b",
    "util.h": "f9fd5aeceb680ff39004e429c69031d521d2785c",
}


class OriginalUniformPacker:
    def __init__(self, original_root, artifact_root):
        root = Path(original_root).absolute() / "tools/c_hack"
        files.check_physical(root, directory=True)
        artifacts = Path(artifact_root).absolute()
        project = Path(__file__).absolute().parents[2]
        if project / ".squad/.scratch" not in artifacts.parents:
            raise files.PreparationError("Original packer artifacts must remain in ignored project scratch.")
        files.make_directories(artifacts)
        self.inputs = []
        for name, pin in PINS.items():
            data = files.read_regular(root / name, 128 * 1024)
            if files.fingerprint(data)["git_blob_sha1"] != pin:
                raise files.PreparationError("Original c_hack content pin mismatch: " + name)
            self.inputs.append(dict(files.fingerprint(data), path="tools/c_hack/" + name))
        wrapper = Path(__file__).with_name("original_uniform_wrapper.c")
        identity = files.sha256(files.json_bytes(self.inputs) + wrapper.read_bytes())
        self.executable = artifacts / ("original-uniform-packer-" + identity[:16])
        if not self.executable.exists():
            env = dict(os.environ, TMPDIR=str(artifacts), TMP=str(artifacts), TEMP=str(artifacts))
            command = ["cc", "-std=gnu11", "-O2", "-ffunction-sections", "-fdata-sections",
                       "-Wno-implicit-function-declaration", "-Wl,--gc-sections", "-I" + str(root),
                       str(wrapper), str(root / "pack.c"), str(root / "bagger.c"), str(root / "tga.c"),
                       str(root / "util.c"), "-o", str(self.executable)]
            result = subprocess.run(command, cwd=project, env=env, capture_output=True, text=True)
            if result.returncode:
                raise files.PreparationError("Cannot compile unchanged original c_hack:\n" + result.stderr)
        files.check_physical(self.executable)
        self.receipt = {
            "repository": "historicalsource/midwayvideo", "revision": REVISION,
            "source_inputs": self.inputs, "wrapper_sha256": files.sha256(wrapper.read_bytes()),
            "executable_sha256": files.sha256(self.executable.read_bytes()),
            "scope": "Unchanged original pack_textures, pack, bagger, tga_read and tga_write; "
                     "explicit four-input driver only, not DOS command parsing or Texus quantization.",
        }
        (artifacts / "packer-receipt.json").write_bytes(files.json_bytes(self.receipt))
        self.artifacts = artifacts / ("atlas-run-" + uuid.uuid4().hex)
        self.artifacts.mkdir(mode=0o700)

    def pack(self, pieces, output_id):
        if len(pieces) != 4 or not output_id.isalnum():
            raise files.PreparationError("Expected one original four-piece uniform pack operation.")
        output = self.artifacts / (output_id + ".original-c-hack.tga")
        if output.exists():
            raise files.PreparationError("Original atlas output already exists; choose a new artifact directory.")
        for piece in pieces:
            files.inspect_tga(files.read_regular(piece, 1024 * 1024))
        result = subprocess.run([str(self.executable), str(output), *map(str, pieces)],
                                capture_output=True, text=True)
        if result.returncode:
            raise files.PreparationError("Unchanged original c_hack failed: " + result.stdout + result.stderr)
        placements = []
        for line in result.stdout.splitlines():
            if line.startswith("SOURCE_PACK_PLACEMENT "):
                values = list(map(int, line.split()[1:]))
                if len(values) != 5:
                    raise files.PreparationError("Invalid original packer placement receipt.")
                placements.append(values)
        if len(placements) != 4:
            raise files.PreparationError("Original packer did not return all four exact placements.")
        data = files.read_regular(output, 1024 * 1024)
        info = files.inspect_tga(data)
        if (info["width"], info["height"]) != (128, 256):
            raise files.PreparationError("Original uniform atlas dimensions changed.")
        return data, sorted(placements)
