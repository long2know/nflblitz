"""Closed metadata/input seal for the separately captured reviewed-q11-6ad/v1 artifact."""
import gzip
import hashlib
import json
import math
from pathlib import Path


CASE = "portable-cabinet-wide-pass-clearance-presnap-switch-q11-6ad-v1-12000"
PUBLIC_HEAD = "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189"
SOURCE_SHA256 = "ff0ee6a691a87eb346959fa446e1496a7c9a9d7a4eeb0b368d97c864cb66aefb"
HARNESS_SHA256 = "53ac07b6647086696464f8728b07c0ef38ea4aa4356785fede60a78dff49908b"
MANIFEST_SHA256 = "f74c4fcecee45aac34687382604c2b0c52a2d197428a52c7d84183d0fd22743f"
JSONL_SHA256 = "ad31c767e1a54af3efc681e0edf93aef88cd76b58bea1869da0df57a62f525e8"
GZIP_SHA256 = "84856f6a35ad49335395f7c7fa1f59ae59e8075f032b0e6589af96ff1e13b17d"


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def mapping_sha(values):
    return sha(json.dumps(values, sort_keys=True, separators=(",", ":")).encode())


def verify(manifest_path, recording_path, packed_path, kernel_path, harness_path, source_root):
    raw_manifest = Path(manifest_path).read_bytes()
    require(sha(raw_manifest) == MANIFEST_SHA256, "reviewed q11 manifest/epoch seal differs")
    manifest = json.loads(raw_manifest)
    require(manifest["case"] == CASE and manifest["source_epoch"]["publicHead"] == PUBLIC_HEAD,
            "reviewed q11 actual authoring epoch differs")
    kernel = json.loads(Path(kernel_path).read_bytes())
    harness = json.loads(Path(harness_path).read_bytes())
    require(len(kernel) == 104 and mapping_sha(kernel) == SOURCE_SHA256,
            "reviewed q11 complete source seal differs")
    require(len(harness) == 12 and mapping_sha(harness) == HARNESS_SHA256,
            "reviewed q11 complete harness seal differs")
    for name, pin in {**kernel, **harness}.items():
        require(sha((Path(source_root) / name).read_bytes()) == pin,
                "reviewed q11 actual source member differs: " + name)
    raw = Path(recording_path).read_bytes()
    packed = Path(packed_path).read_bytes()
    require(sha(raw) == JSONL_SHA256 and len(raw) == manifest["jsonl"]["bytes"],
            "reviewed q11 unchanged command bytes differ")
    require(sha(packed) == GZIP_SHA256 and len(packed) == manifest["gzip"]["bytes"],
            "reviewed q11 immutable compressed artifact differs")
    require(gzip.decompress(packed) == raw, "reviewed q11 compressed/uncompressed bytes disagree")
    rows = raw.splitlines()
    require(len(rows) == 12000 and all(json.loads(row)["host"] == host
                                     for host, row in enumerate(rows)),
            "reviewed q11 complete input span differs")
    return manifest


def verify_distinct_poses(path):
    rows = json.loads(Path(path).read_bytes())
    earlier = {}
    for row in rows:
        for field in ("real15Joints", "real15LocalPose"):
            require(len(row[field]) == 15 and
                    all(math.isfinite(joint[axis]) for joint in row[field] for axis in ("X", "Y", "Z")),
                    "reviewed q11 actual fifteen-joint pose is missing/nonfinite")
        if row["SourceTick"] <= 2299:
            continue
        key = (row["actor"], row["InstanceId"])
        if key not in earlier:
            earlier[key] = row
            continue
        first = earlier[key]
        if (row["SourceTick"] > first["SourceTick"] and row["ordinal"] != first["ordinal"] and
                row["real15LocalPose"] != first["real15LocalPose"]):
            return {"actor": row["actor"], "instance": row["InstanceId"],
                    "firstTick": first["SourceTick"], "secondTick": row["SourceTick"],
                    "firstOrdinal": first["ordinal"], "secondOrdinal": row["ordinal"]}
    raise ValueError("reviewed q11 real R_SKIP distinct-ordinal/distinct-pose witness is missing")
