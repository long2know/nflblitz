#!/usr/bin/env python3
"""Export every selected frame of one pinned local 15-limb WIFF/3AN WAN sequence."""

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path, PurePosixPath
import re
import sys

try:
    from . import read_wan_pose as wan
except ImportError:
    _spec = importlib.util.spec_from_file_location(
        "read_wan_pose", Path(__file__).with_name("read_wan_pose.py"))
    wan = importlib.util.module_from_spec(_spec)
    _spec.loader.exec_module(wan)


def _provenance(data, provenance):
    expected = provenance.get("git_blob_sha1", "")
    if not re.fullmatch(r"[0-9a-f]{40}", expected):
        raise wan.WanError("a lowercase 40-character expected Git blob SHA-1 is required")
    if wan.git_blob_sha1(data) != expected:
        raise wan.WanError("input does not match expected Git blob SHA-1")
    if not re.fullmatch(r"[0-9a-f]{40}", provenance.get("commit", "")):
        raise wan.WanError("source commit must be a lowercase 40-character SHA-1")
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", provenance.get("repository", ""), re.ASCII):
        raise wan.WanError("source repository must be owner/name")
    source_path = provenance.get("path", "")
    if (not source_path or not PurePosixPath(source_path).parts
            or PurePosixPath(source_path).is_absolute()
            or ".." in PurePosixPath(source_path).parts or "\\" in source_path):
        raise wan.WanError("source path must be a nonempty relative repository path")
    return {
        **provenance, "byte_length": len(data), "sha256": hashlib.sha256(data).hexdigest(),
        "content_pin_verified": True, "format_source": wan.FORMAT_SOURCE,
    }


def _sequence(data, sequence):
    if not sequence or not wan._stem(sequence):
        raise wan.WanError("sequence name is required")
    matches, chunk_count = [], 0
    for tag, number, reference, offset, body, end in wan._chunks(data):
        chunk_count += 1
        if chunk_count > 4096:
            raise wan.WanError("too many chunks")
        if tag != b"3AN ":
            continue
        header = wan._unpack(wan.ANIMATION, data, body, end)
        try:
            name = header[0].split(b"\0", 1)[0].decode("ascii")
        except UnicodeDecodeError as exc:
            raise wan.WanError("non-ASCII animation name") from exc
        if wan._stem(name) == wan._stem(sequence):
            matches.append((name, header, number, reference, offset, body, end))
    if len(matches) != 1:
        raise wan.WanError(f"expected one matching 3AN sequence, found {len(matches)}")
    name, header, number, reference, offset, body, end = matches[0]
    flags, limbs = header[1:3]
    if flags & ~0x4001:
        raise wan.WanError("unsupported animation flags")
    if not flags & wan.SELECTED:
        raise wan.WanError("sequence is not selected for original export")
    if reference:
        raise wan.WanError("referenced animation chunks are unsupported")
    if limbs != len(wan.JOINTS):
        raise wan.WanError("only the 15-limb fbplyr_skel mapping is supported")
    records, count = wan._records(data, body + wan.ANIMATION.size, end, limbs)
    return name, flags, number, reference, offset, end, records, count, chunk_count


def full_clip(data, sequence, provenance):
    """Decode selected WAN frames, not the original converter's lossy SEQ deltas.

    Commit/path linkage is supplied by the caller's pinned source tree. The blob
    identity and every frame in the requested sequence are checked here offline.
    """
    if len(data) > wan.MAX_BYTES:
        raise wan.WanError("input exceeds this reader's 64 MiB limit")
    verified = _provenance(data, provenance)
    (name, flags, number, reference, offset, end, records,
     count, chunk_count) = _sequence(data, sequence)
    frame_flags = [
        wan._unpack(wan.INTEGER, data, records[-1]["data_offset"] + 4 * i, end)[0]
        for i in range(count)
    ]
    selected_frames = [i for i, value in enumerate(frame_flags) if value & wan.SELECTED]
    if not selected_frames:
        raise wan.WanError("sequence has no selected frames")

    frames = []
    selected_set = set(selected_frames)
    for frame in range(count):
        translation = wan._unpack(
            wan.VECTOR, data, records[-2]["data_offset"] + wan.VECTOR.size * frame, end)
        if not all(math.isfinite(value) for value in translation):
            raise wan.WanError(f"non-finite root translation at source frame {frame}")
        if frame in selected_set:
            frames.append({
                "exported_frame_index": len(frames), "source_frame_index": frame,
                "source_frame_flags": frame_flags[frame],
                "root_translation": {
                    "authoring_xyz": list(translation),
                    "scaled_runtime_xyz": [value * wan.TRANSLATION_SCALE for value in translation],
                },
                "rotations_xyzw": [],
            })
    by_source_frame = {frame["source_frame_index"]: frame for frame in frames}
    orthogonality_error = determinant_error = norm_error = 0.0
    min_source_dot = 1.0
    for joint in range(len(wan.JOINTS)):
        previous = None
        for frame in range(count):
            position = records[joint + 1]["data_offset"] + wan.LIMB_FRAME.size * frame
            values = wan._unpack(wan.LIMB_FRAME, data, position, end)
            if not all(math.isfinite(value) for value in values[:3]):
                raise wan.WanError(
                    f"non-finite authoring limb offset at source frame {frame}, limb {joint + 1}")
            try:
                orthogonality, determinant = wan._rotation_errors(values[3:])
                q = wan.matrix_to_quaternion(values[3:])
            except wan.WanError as exc:
                raise wan.WanError(f"source frame {frame}, limb {joint + 1}: {exc}") from exc
            if previous is not None and sum(a * b for a, b in zip(previous, q)) < 0:
                q = [-value for value in q]
            if previous is not None:
                min_source_dot = min(min_source_dot, sum(a * b for a, b in zip(previous, q)))
            previous = q
            orthogonality_error = max(orthogonality_error, orthogonality)
            determinant_error = max(determinant_error, determinant)
            norm_error = max(norm_error, abs(math.sqrt(sum(value * value for value in q)) - 1))
            if frame in selected_set:
                by_source_frame[frame]["rotations_xyzw"].append(q)

    return {
        "schema": "original-wan-clip/v1",
        "provenance": verified,
        "format": {
            "container": "WIFF", "chunk": "3AN ", "container_endianness": "big",
            "animation_body_endianness": "little", "source_long_bytes": 4,
            "source_float": "IEEE-754 binary32", "chunk_count": chunk_count,
        },
        "sequence": {
            "authoring_name": name, "runtime_name": wan._stem(name),
            "chunk_number": number, "chunk_reference": reference,
            "chunk_byte_offset": offset, "chunk_payload_bytes": end - offset - wan.WIFF.size,
            "chunk_end_byte_offset": end, "animation_flags": flags,
            "limb_count": len(wan.JOINTS), "source_frame_count": count,
            "selected_frame_count": len(selected_frames),
            "selected_source_frame_indices": selected_frames,
        },
        "joints": [
            {"name": name, "fbplyr_skel_index": i, "source_limb_id": i + 1, "parent_index": parent}
            for i, (name, parent) in enumerate(wan.JOINTS)
        ],
        "root_translation": {
            "runtime_scale": wan.TRANSLATION_SCALE,
            "space": "source model XYZ before player placement/facing and animation-mode-specific anchoring",
        },
        "semantics": {
            "quaternion_order": "XYZW",
            "rotation_space": "parent-composed rotations about fbplyr_skel reference pivots",
            "joint_offsets": "validated authoring evidence only; wan2seq discards these, not runtime pivots",
            "pivots": "original models/skeleton.h fbplyr_skel; no pivot or basis changes",
            "unity_conversion": "none",
            "hemisphere": "original converter continuity through every authoring frame before selection",
            "precision": "normalized Python double conversion of binary32 WAN matrices, not lossy SEQ compression",
        },
        "frames": frames,
        "record_layout": [{"id": kind, **record} for kind, record in records.items()],
        "validation": {
            "validated_source_frames": count, "validated_limb_frames": count * len(wan.JOINTS),
            "matrix_error_limit": 1e-4,
            "max_matrix_orthogonality_error": orthogonality_error,
            "max_matrix_determinant_error": determinant_error,
            "max_quaternion_norm_error": norm_error,
            "min_adjacent_source_quaternion_dot": min_source_dot,
        },
        "loop_boundary": {
            "first_source_frame_index": selected_frames[0],
            "last_source_frame_index": selected_frames[-1],
            "last_to_first_quaternion_dots": [
                sum(a * b for a, b in zip(last, first))
                for last, first in zip(frames[-1]["rotations_xyzw"], frames[0]["rotations_xyzw"])
            ],
            "last_minus_first_scaled_root_xyz": [
                last - first for last, first in zip(
                    frames[-1]["root_translation"]["scaled_runtime_xyz"],
                    frames[0]["root_translation"]["scaled_runtime_xyz"])
            ],
            "policy": "diagnostic only; loopability comes from gameplay script; shortest-arc interpolation at seam",
        },
        "playback": {
            "status": "requires-source-playback-binding",
            "tick_hz": None, "frames_per_second": None, "frame_advance": None,
            "loop": None, "anchor_mode": None,
        },
    }


def encode_clip(clip):
    """Deterministic compact JSON; decoded original motion remains local-only."""
    return json.dumps(clip, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="explicit local WAN path; never downloaded")
    parser.add_argument("--sequence", required=True, help="original runtime or authoring sequence name")
    parser.add_argument("--expected-blob", required=True, help="pinned upstream Git blob SHA-1")
    parser.add_argument("--source-repository", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--source-path", required=True)
    parser.add_argument("--output", type=Path, required=True, help="file beneath ignored .squad/.scratch/")
    args = parser.parse_args(argv)
    try:
        output = args.output.resolve()
        scratch = wan.SCRATCH.resolve()
        if not output.is_relative_to(scratch) or output == scratch:
            raise wan.WanError("clip output must stay beneath ignored .squad/.scratch/")
        if output == args.input.resolve() or (
                output.exists() and args.input.exists() and output.samefile(args.input)):
            raise wan.WanError("output must not overwrite input")
        if args.input.stat().st_size > wan.MAX_BYTES:
            raise wan.WanError("input exceeds this reader's 64 MiB limit")
        clip = full_clip(args.input.read_bytes(), args.sequence, {
            "repository": args.source_repository, "commit": args.source_commit,
            "path": args.source_path, "git_blob_sha1": args.expected_blob,
            "local_input": str(args.input),
            "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
        })
        text = encode_clip(clip)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(text, encoding="utf-8")
        print(f"Wrote {len(clip['frames'])} selected frames x {len(clip['joints'])} joints "
              f"({clip['sequence']['runtime_name']}) to {output}")
        return 0
    except (OSError, wan.WanError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
