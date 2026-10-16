#!/usr/bin/env python3
"""Read one first-selected 15-limb pose from a local Williams WIFF/3AN WAN."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys


WIFF = struct.Struct(">4sI")
CHUNK = struct.Struct(">4sIII")
ANIMATION = struct.Struct("<20sII10I")
INTEGER = struct.Struct("<i")
VECTOR = struct.Struct("<3f")
LIMB_FRAME = struct.Struct("<12f")
SELECTED = 0x4000
MAX_FRAMES = 1024
MAX_BYTES = 64 * 1024 * 1024
TRANSLATION_SCALE = 0.12898
SCRATCH = Path(__file__).resolve().parents[2] / ".squad" / ".scratch"
JOINTS = (
    ("pelvis", None), ("torso", 0),
    ("right_upper_arm", 1), ("right_forearm", 2), ("right_hand", 3),
    ("left_upper_arm", 1), ("left_forearm", 5), ("left_hand", 6),
    ("head", 1),
    ("right_thigh", 0), ("right_calf", 9), ("right_foot", 10),
    ("left_thigh", 0), ("left_calf", 12), ("left_foot", 13),
)
FORMAT_SOURCE = {
    "repository": "historicalsource/midwayvideo",
    "commit": "1792293aaaea00a919b59e8c05716978e85b4333",
    "path": "tools/wan2seq/wan2seq.c",
    "git_blob_sha1": "33c7e489d9b7a40d3b0cd435236db8c357d143fe",
    "definitions_lines": [29, 53],
    "container_reader_lines": [207, 277],
    "animation_reader_lines": [314, 555],
    "matrix_conversion_lines": [585, 631],
}


class WanError(ValueError):
    """Unsupported or malformed input; no pose should be emitted."""


def git_blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def _unpack(layout, data, offset, end):
    if offset < 0 or offset + layout.size > end:
        raise WanError(f"truncated {layout.format} at byte {offset}, boundary {end}")
    return layout.unpack_from(data, offset)


def _stem(name):
    return name.split(".", 1)[0][:15].upper()


def _chunks(data):
    signature, size = _unpack(WIFF, data, 0, len(data))
    if signature != b"WIFF":
        raise WanError("unsupported container: expected WIFF")
    if size != len(data) - WIFF.size:
        raise WanError("WIFF declared length does not match input length")
    offset = WIFF.size
    while offset < len(data):
        tag, size, number, reference = _unpack(CHUNK, data, offset, len(data))
        if size < CHUNK.size - WIFF.size:
            raise WanError(f"chunk size excludes its number/reference at byte {offset}")
        end = offset + WIFF.size + size
        padded_end = end + (end & 1)
        if padded_end > len(data):
            raise WanError(f"chunk or even-byte padding exceeds file at byte {offset}")
        yield tag, number, reference, offset, offset + CHUNK.size, end
        offset = padded_end


def _rotation_errors(matrix):
    if not all(math.isfinite(value) for value in matrix):
        raise WanError("non-finite rotation matrix")
    error = max(
        abs(sum(matrix[3 * r + k] * matrix[3 * c + k] for k in range(3))
            - (1.0 if r == c else 0.0))
        for r in range(3) for c in range(3)
    )
    a, b, c, d, e, f, g, h, i = matrix
    determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g)
    if error > 1e-4 or abs(determinant - 1.0) > 1e-4:
        raise WanError("matrix is not a proper orthonormal rotation")
    return error, abs(determinant - 1.0)


def matrix_to_quaternion(matrix):
    """Return normalized XYZW, with the original converter's hemisphere."""
    _rotation_errors(matrix)
    trace = matrix[0] + matrix[4] + matrix[8]
    terms = (
        1 + matrix[0] - matrix[4] - matrix[8],
        1 - matrix[0] + matrix[4] - matrix[8],
        1 - matrix[0] - matrix[4] + matrix[8],
        1 + trace,
    )
    pivot = 3 if trace > 0 else max(range(3), key=lambda k: matrix[4 * k])
    q = [0.0] * 4
    q[pivot] = math.sqrt(max(0.0, terms[pivot])) / 2
    denominator = 4 * q[pivot]
    if denominator <= 1e-12:
        raise WanError("degenerate quaternion conversion")
    if pivot == 3:
        q[:3] = [(matrix[7] - matrix[5]) / denominator,
                 (matrix[2] - matrix[6]) / denominator,
                 (matrix[3] - matrix[1]) / denominator]
    else:
        j, k = (pivot + 1) % 3, (pivot + 2) % 3
        for other in (j, k):
            q[other] = (matrix[3 * pivot + other] + matrix[3 * other + pivot]) / denominator
        q[3] = (matrix[3 * k + j] - matrix[3 * j + k]) / denominator
    norm = math.sqrt(sum(value * value for value in q))
    if not math.isfinite(norm) or norm <= 1e-12:
        raise WanError("non-finite or zero quaternion")
    return [value / norm for value in q]


def _records(data, offset, end, limb_count):
    records = {}
    frame_count = None
    while True:
        record_offset = offset
        kind, = _unpack(INTEGER, data, offset, end)
        offset += INTEGER.size
        if kind == 0:
            if offset != end:
                raise WanError("unexpected bytes after animation terminator")
            break
        if kind not in (-1, -2) and not 1 <= kind <= limb_count:
            raise WanError(f"unsupported animation record id {kind}")
        if kind in records:
            raise WanError(f"duplicate animation record id {kind}")
        count, = _unpack(INTEGER, data, offset, end)
        offset += INTEGER.size
        if not 1 <= count <= MAX_FRAMES:
            raise WanError(f"invalid frame count {count}")
        if frame_count is not None and frame_count != count:
            raise WanError("inconsistent frame counts")
        frame_count = count
        stride = {-1: INTEGER.size, -2: VECTOR.size}.get(kind, LIMB_FRAME.size)
        if offset + count * stride > end:
            raise WanError(f"truncated animation record id {kind}")
        records[kind] = {
            "record_offset": record_offset, "data_offset": offset,
            "count": count, "stride": stride,
        }
        offset += count * stride
    if set(records) != {-1, -2, *range(1, limb_count + 1)}:
        raise WanError("missing flags, root translation, or limb records")
    return records, frame_count


def first_pose(data, sequence, provenance):
    """Decode one pose; provenance's content pin is mandatory and checked offline."""
    if len(data) > MAX_BYTES:
        raise WanError("input exceeds this narrow reader's 64 MiB limit")
    expected = provenance.get("git_blob_sha1", "")
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise WanError("a lowercase 40-character expected Git blob SHA-1 is required")
    if git_blob_sha1(data) != expected:
        raise WanError("input does not match expected Git blob SHA-1")
    if not sequence or not _stem(sequence):
        raise WanError("sequence name is required")

    matches = []
    chunk_count = 0
    for tag, number, reference, chunk_offset, body, end in _chunks(data):
        chunk_count += 1
        if chunk_count > 4096:
            raise WanError("too many chunks")
        if tag != b"3AN ":
            continue
        header = _unpack(ANIMATION, data, body, end)
        try:
            name = header[0].split(b"\0", 1)[0].decode("ascii")
        except UnicodeDecodeError as exc:
            raise WanError("non-ASCII animation name") from exc
        if _stem(name) == _stem(sequence):
            matches.append((name, header, number, reference, chunk_offset, body, end))
    if len(matches) != 1:
        raise WanError(f"expected one matching 3AN sequence, found {len(matches)}")

    name, header, number, reference, chunk_offset, body, end = matches[0]
    flags, limb_count = header[1:3]
    if flags & ~0x4001:
        raise WanError("unsupported animation flags")
    if not flags & SELECTED:
        raise WanError("sequence is not selected for original export")
    if reference:
        raise WanError("referenced animation chunks are unsupported")
    if limb_count != len(JOINTS):
        raise WanError("only the 15-limb fbplyr_skel mapping is supported")
    records, frame_count = _records(data, body + ANIMATION.size, end, limb_count)
    frame_flags = [
        _unpack(INTEGER, data, records[-1]["data_offset"] + INTEGER.size * i, end)[0]
        for i in range(frame_count)
    ]
    selected_frames = [i for i, value in enumerate(frame_flags) if value & SELECTED]
    if not selected_frames:
        raise WanError("sequence has no selected frames")
    source_frame = selected_frames[0]
    translation_offset = records[-2]["data_offset"] + VECTOR.size * source_frame
    translation = _unpack(VECTOR, data, translation_offset, end)
    if not all(math.isfinite(value) for value in translation):
        raise WanError("non-finite root translation")

    joints = []
    orthogonality_errors, determinant_errors = [], []
    for index, (joint_name, parent) in enumerate(JOINTS):
        record = records[index + 1]
        previous = None
        for frame in range(source_frame + 1):
            values = _unpack(LIMB_FRAME, data, record["data_offset"] + LIMB_FRAME.size * frame, end)
            q = matrix_to_quaternion(values[3:])
            if previous is not None and sum(a * b for a, b in zip(previous, q)) < 0:
                q = [-value for value in q]
            previous = q
        if not all(math.isfinite(value) for value in values[:3]):
            raise WanError("non-finite authoring limb offset")
        orthogonality, determinant = _rotation_errors(values[3:])
        orthogonality_errors.append(orthogonality)
        determinant_errors.append(determinant)
        joints.append({
            "name": joint_name, "fbplyr_skel_index": index,
            "source_limb_id": index + 1, "parent_index": parent,
            "quaternion_xyzw": q, "rotation_matrix_row_major": list(values[3:]),
            "authoring_offset_ignored_by_converter": list(values[:3]),
            "source_frame_byte_offset": record["data_offset"] + LIMB_FRAME.size * source_frame,
        })

    return {
        "schema": "original-wan-first-pose/v1",
        "provenance": {**provenance, "byte_length": len(data),
                       "sha256": hashlib.sha256(data).hexdigest(),
                       "content_pin_verified": True, "format_source": FORMAT_SOURCE},
        "format": {
            "container": "WIFF", "chunk": "3AN ", "container_endianness": "big",
            "animation_body_endianness": "little", "source_long_bytes": 4,
            "source_float": "IEEE-754 binary32", "chunk_count": chunk_count,
        },
        "sequence": {
            "authoring_name": name, "runtime_name": _stem(name),
            "chunk_number": number, "chunk_reference": reference,
            "chunk_byte_offset": chunk_offset, "chunk_payload_bytes": end - chunk_offset - WIFF.size,
            "chunk_end_byte_offset": end, "animation_flags": flags,
            "limb_count": limb_count, "source_frame_count": frame_count,
            "selected_frame_count": len(selected_frames),
            "source_frame_index": source_frame, "exported_frame_index": 0,
            "source_frame_flags": frame_flags[source_frame],
            "root_translation_byte_offset": translation_offset,
        },
        "semantics": {
            "quaternion_order": "XYZW",
            "rotation_space": "parent-composed rotations about fbplyr_skel reference pivots",
            "root_translation_space": "source model XYZ, before player position/facing and runtime anchoring",
            "joint_offsets": "preserved as evidence only; wan2seq ignores these, not local joint translations",
            "pivots": "use original models/skeleton.h fbplyr_skel; no pivots or basis changes invented here",
            "unity_conversion": "none",
            "scope": "one first-exported pose, not a runtime animation system or standing-state classifier",
        },
        "root_translation": {
            "authoring_xyz": list(translation), "runtime_scale": TRANSLATION_SCALE,
            "scaled_runtime_xyz": [value * TRANSLATION_SCALE for value in translation],
        },
        "joints": joints,
        "record_layout": [{"id": kind, **record} for kind, record in records.items()],
        "validation": {
            "max_matrix_orthogonality_error": max(orthogonality_errors),
            "max_matrix_determinant_error": max(determinant_errors),
            "max_quaternion_norm_error": max(
                abs(math.sqrt(sum(v * v for v in joint["quaternion_xyzw"])) - 1)
                for joint in joints
            ),
        },
    }


def encode_pose(pose):
    return json.dumps(pose, indent=2, sort_keys=True, allow_nan=False) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="explicit local WAN path; never downloaded")
    parser.add_argument("--sequence", required=True, help="original export name or authoring name")
    parser.add_argument("--expected-blob", required=True, help="pinned upstream Git blob SHA-1")
    parser.add_argument("--source-repository", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--source-path", required=True)
    parser.add_argument("--output", type=Path, default=SCRATCH / "original-standing-pose.json")
    args = parser.parse_args(argv)
    try:
        output = args.output.resolve()
        if not output.is_relative_to(SCRATCH.resolve()) or output == SCRATCH.resolve():
            raise WanError("pose output must stay beneath ignored .squad/.scratch/")
        if output == args.input.resolve():
            raise WanError("output must not overwrite input")
        if args.input.stat().st_size > MAX_BYTES:
            raise WanError("input exceeds this narrow reader's 64 MiB limit")
        if not re.fullmatch(r"[0-9a-f]{40}", args.source_commit):
            raise WanError("source commit must be a lowercase 40-character SHA-1")
        pose = first_pose(args.input.read_bytes(), args.sequence, {
            "repository": args.source_repository, "commit": args.source_commit,
            "path": args.source_path, "git_blob_sha1": args.expected_blob,
            "local_input": str(args.input),
            "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
        })
        text = encode_pose(pose)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(text, encoding="utf-8")
        print(f"Wrote {len(pose['joints'])} original-source joint rotations to {output}")
        return 0
    except (OSError, WanError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
