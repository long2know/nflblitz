"""Lossless private replay retention for the bounded source-driven scenario."""
import gzip
import hashlib
import os
from pathlib import Path


FILES = ("m1-public-replay.jsonl", "m1-all-raw-draws.json", "m1-R_SKIP-real-poses.json")


def open_evidence(directory, filename):
    if filename not in FILES:
        raise ValueError("Only the three complete private replay evidence files may be archived.")
    path = Path(directory) / filename
    packed = path.with_name(path.name + ".gz")
    if path.exists() and packed.exists():
        raise ValueError("Ambiguous raw/archive replay evidence is forbidden: " + filename)
    if path.is_file() and not path.is_symlink():
        return path.open("rb")
    if not packed.is_file() or packed.is_symlink():
        raise ValueError("Missing real raw or losslessly archived replay evidence: " + filename)
    return gzip.open(packed, "rb")


def same(left, right, filename):
    with open_evidence(left, filename) as first, open_evidence(right, filename) as second:
        while True:
            a, b = first.read(1024 * 1024), second.read(1024 * 1024)
            if a != b:
                return False
            if not a:
                return True


def archive(directory):
    result = {}
    for filename in FILES:
        path = Path(directory) / filename
        if not path.exists():
            continue
        if path.is_symlink() or not path.is_file():
            raise ValueError("Replay retention requires real owned files.")
        packed = path.with_name(filename + ".gz")
        digest, total = hashlib.sha256(), 0
        with path.open("rb") as source, packed.open("xb") as target:
            with gzip.GzipFile(fileobj=target, mode="wb", filename="", mtime=0, compresslevel=6) as stream:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    stream.write(chunk)
                    digest.update(chunk)
                    total += len(chunk)
            target.flush()
            os.fsync(target.fileno())
        with path.open("rb") as source, gzip.open(packed, "rb") as restored:
            while True:
                a, b = source.read(1024 * 1024), restored.read(1024 * 1024)
                if a != b:
                    raise ValueError("Lossless replay archive byte roundtrip failed: " + filename)
                if not a:
                    break
        packed_sha = hashlib.sha256()
        with packed.open("rb") as source:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                packed_sha.update(chunk)
        result[filename] = dict(bytes=total, sha256=digest.hexdigest(), gzip_bytes=packed.stat().st_size,
                                gzip_sha256=packed_sha.hexdigest(), byte_roundtrip_verified=True)
        path.unlink()
    return result


def verify(directory, records):
    if set(records) != set(FILES):
        raise ValueError("All three complete private replay archives must be retained.")
    for filename, record in records.items():
        if set(record) != {"bytes", "sha256", "gzip_bytes", "gzip_sha256", "byte_roundtrip_verified"} or \
                record["byte_roundtrip_verified"] is not True or type(record["bytes"]) is not int or \
                type(record["gzip_bytes"]) is not int or record["bytes"] <= 0 or record["gzip_bytes"] <= 0:
            raise ValueError("A real complete lossless replay archive record is required.")
        packed = Path(directory) / (filename + ".gz")
        if packed.is_symlink() or not packed.is_file() or packed.stat().st_size != record["gzip_bytes"]:
            raise ValueError("Replay archive container differs: " + filename)
        digest = hashlib.sha256()
        with packed.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        if digest.hexdigest() != record["gzip_sha256"]:
            raise ValueError("Replay archive container identity differs: " + filename)
        digest, total = hashlib.sha256(), 0
        with gzip.open(packed, "rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
                total += len(chunk)
        if digest.hexdigest() != record["sha256"] or total != record["bytes"]:
            raise ValueError("Replay archive restored byte identity differs: " + filename)
