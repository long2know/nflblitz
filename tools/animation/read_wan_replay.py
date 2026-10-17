#!/usr/bin/env python3
"""Explicit, audited replay of a missing WAN limb's original converter state.

This is not a permissive WAN reader. The normal readers remain strict. A caller
must supply a complete retained-record recipe, and the last selected animation
before the target must completely define that record for every target frame.
"""

import copy
import importlib.util
import math
from pathlib import Path

try:
    from . import read_wan_clip as reader
except ImportError:
    _spec = importlib.util.spec_from_file_location(
        "read_wan_clip", Path(__file__).with_name("read_wan_clip.py"))
    reader = importlib.util.module_from_spec(_spec)
    _spec.loader.exec_module(reader)


wan = reader.wan
WanError = wan.WanError
POLICY = "wan2seq-retained-limb/v1"
SCHEMA = "original-wan-converter-replay-clip/v1"
REPLAY_FIELDS = {
    "policy", "missing_source_limb_id", "retained_authoring_name",
    "retained_chunk_number", "retained_chunk_byte_offset", "retained_chunk_end_byte_offset",
    "retained_animation_flags", "retained_source_frame_count", "retained_record_offset",
    "retained_data_offset", "retained_stride", "frame_mapping",
    "source_reference_path", "source_reference_git_blob_sha1", "source_reference_symbol",
    "shipped_binary_verified",
}


def require(condition, message):
    if not condition:
        raise WanError(message)


def validate_recipe(recipe):
    require(isinstance(recipe, dict) and recipe.keys() == REPLAY_FIELDS,
            "an explicit complete converter-state recipe is required")
    require(recipe["policy"] == POLICY, "unsupported converter-state replay policy")
    require(type(recipe["missing_source_limb_id"]) is int
            and 1 <= recipe["missing_source_limb_id"] <= len(wan.JOINTS),
            "invalid explicitly retained source limb")
    for key in ("retained_chunk_number", "retained_chunk_byte_offset",
                "retained_chunk_end_byte_offset", "retained_animation_flags",
                "retained_source_frame_count", "retained_record_offset",
                "retained_data_offset", "retained_stride"):
        require(type(recipe[key]) is int and recipe[key] >= 0,
                f"{key} must be a nonnegative integer")
    require(isinstance(recipe["retained_authoring_name"], str)
            and 0 < len(recipe["retained_authoring_name"]) <= 20,
            "retained authoring name is required")
    require(recipe["frame_mapping"] == "same-authoring-index"
            and recipe["retained_stride"] == wan.LIMB_FRAME.size,
            "only the original same-authoring-index matrix buffer is supported")
    require(recipe["retained_animation_flags"] == wan.SELECTED,
            "retained writer must be selected and uncompressed; compressed buffer mutation is not qualified")
    require(recipe["source_reference_path"] == "plyrseq.c"
            and recipe["source_reference_git_blob_sha1"] ==
            "28cef0d1082f2b0c9241930fdc8886de4c8f5438"
            and recipe["source_reference_symbol"] == "r_skip_anim"
            and recipe["shipped_binary_verified"] is False,
            "replay requires the pinned active R_SKIP source reference, not a shipped-binary claim")


def records_with_one_omission(data, offset, end, missing):
    """Parse actual records; never invent a matrix record or change source bytes."""
    records, count = {}, None
    while True:
        record_offset = offset
        kind, = wan._unpack(wan.INTEGER, data, offset, end)
        offset += 4
        if kind == 0:
            require(offset == end, "unexpected bytes after animation terminator")
            break
        require(kind in (-1, -2) or 1 <= kind <= len(wan.JOINTS),
                f"unsupported animation record id {kind}")
        require(kind not in records, f"duplicate animation record id {kind}")
        frames, = wan._unpack(wan.INTEGER, data, offset, end)
        offset += 4
        require(1 <= frames <= wan.MAX_FRAMES, f"invalid frame count {frames}")
        require(count is None or count == frames, "inconsistent frame counts")
        count = frames
        stride = {-1: 4, -2: 12}.get(kind, 48)
        require(offset + frames * stride <= end, f"truncated animation record id {kind}")
        records[kind] = {
            "record_offset": record_offset, "data_offset": offset,
            "count": frames, "stride": stride,
        }
        offset += frames * stride
    require(set(records) == {-1, -2, *range(1, 16)} - {missing},
            "actual record set differs from the single explicitly qualified omission")
    return records, count


def full_replay_clip(data, sequence, provenance, recipe):
    validate_recipe(recipe)
    require(sequence == "R_SKIP", "converter replay is qualified only for source-referenced R_SKIP")
    require(len(data) <= wan.MAX_BYTES, "input exceeds this reader's 64 MiB limit")
    verified = reader._provenance(data, provenance)
    animations, matches, chunk_count = [], [], 0
    for tag, number, reference, offset, body, end in wan._chunks(data):
        chunk_count += 1
        require(chunk_count <= 4096, "too many chunks")
        if tag != b"3AN ":
            continue
        header = wan._unpack(wan.ANIMATION, data, body, end)
        try:
            name = header[0].split(b"\0", 1)[0].decode("ascii")
        except UnicodeDecodeError as exc:
            raise WanError("non-ASCII animation name") from exc
        item = (name, header, number, reference, offset, body, end)
        animations.append(item)
        if wan._stem(name) == sequence:
            matches.append(item)
    require(len(matches) == 1, f"expected one matching 3AN sequence, found {len(matches)}")
    name, header, number, reference, offset, body, end = target = matches[0]
    flags, limbs = header[1:3]
    require(not flags & ~0x4001, "unsupported animation flags")
    require(flags & wan.SELECTED, "sequence is not selected for original export")
    require(flags == wan.SELECTED, "converter-state replay requires an uncompressed selected target")
    require(not reference, "referenced animation chunks are unsupported")
    require(limbs == len(wan.JOINTS), "only the 15-limb fbplyr_skel mapping is supported")
    missing = recipe["missing_source_limb_id"]
    records, count = records_with_one_omission(data, body + wan.ANIMATION.size, end, missing)
    preceding = [item for item in animations if item[4] < offset and item[1][1] & wan.SELECTED]
    require(preceding, "no preceding selected converter record writer")
    donor = preceding[-1]
    dn, dh, dnum, dref, doffset, dbody, dend = donor
    require((dn, dnum, doffset, dend, dh[1]) == (
        recipe["retained_authoring_name"], recipe["retained_chunk_number"],
        recipe["retained_chunk_byte_offset"], recipe["retained_chunk_end_byte_offset"],
        recipe["retained_animation_flags"]),
        "retained record is not the explicitly pinned last preceding selected animation")
    require(not dref and dh[2] == limbs, "unsupported retained animation reference/limb mapping")
    donor_clip = reader.full_clip(data, wan._stem(dn), provenance)
    donor_records = {record["id"]: record for record in donor_clip["record_layout"]}
    retained = donor_records[missing]
    require((retained["record_offset"], retained["data_offset"], retained["stride"],
             retained["count"]) == (
        recipe["retained_record_offset"], recipe["retained_data_offset"],
        recipe["retained_stride"], recipe["retained_source_frame_count"]),
        "retained matrix record differs from the explicit converter-state recipe")
    donor_frames = {frame["source_frame_index"]: frame for frame in donor_clip["frames"]}
    require(retained["count"] >= count and set(range(count)) <= donor_frames.keys(),
            "retained record must define every target authoring frame without an earlier-buffer fallback")
    frame_flags = [wan._unpack(wan.INTEGER, data, records[-1]["data_offset"] + i * 4, end)[0]
                   for i in range(count)]
    selected = [i for i, value in enumerate(frame_flags) if value & wan.SELECTED]
    require(selected, "sequence has no selected frames")

    rotations = [[] for _ in range(count)]
    orthogonality = determinant = norm_error = 0.0
    min_dot = 1.0
    for joint in range(limbs):
        previous = None
        for frame in range(count):
            if joint + 1 == missing:
                q = donor_frames[frame]["rotations_xyzw"][joint]
                source_record, boundary = retained, dend
            else:
                source_record, boundary = records[joint + 1], end
                q = None
            values = wan._unpack(
                wan.LIMB_FRAME, data, source_record["data_offset"] + frame * 48, boundary)
            require(all(math.isfinite(value) for value in values[:3]),
                    f"non-finite authoring limb offset at source frame {frame}, limb {joint + 1}")
            e, d = wan._rotation_errors(values[3:])
            orthogonality, determinant = max(orthogonality, e), max(determinant, d)
            if q is None:
                q = wan.matrix_to_quaternion(values[3:])
                if previous is not None and sum(a * b for a, b in zip(previous, q)) < 0:
                    q = [-v for v in q]
            if previous is not None:
                min_dot = min(min_dot, sum(a * b for a, b in zip(previous, q)))
            previous = q
            norm_error = max(norm_error, abs(math.sqrt(sum(v * v for v in q)) - 1))
            rotations[frame].append(q)

    frames = []
    for frame in range(count):
        root = wan._unpack(wan.VECTOR, data, records[-2]["data_offset"] + frame * 12, end)
        require(all(math.isfinite(value) for value in root),
                f"non-finite root translation at source frame {frame}")
        if frame_flags[frame] & wan.SELECTED:
            frames.append({
                "exported_frame_index": len(frames), "source_frame_index": frame,
                "source_frame_flags": frame_flags[frame],
                "root_translation": {
                    "authoring_xyz": list(root),
                    "scaled_runtime_xyz": [value * wan.TRANSLATION_SCALE for value in root],
                },
                "rotations_xyzw": rotations[frame],
            })
    require(target[4] > donor[4], "retained writer must precede target")
    return {
        "schema": SCHEMA, "provenance": verified,
        "format": {
            "container": "WIFF", "chunk": "3AN ", "container_endianness": "big",
            "animation_body_endianness": "little", "source_long_bytes": 4,
            "source_float": "IEEE-754 binary32", "chunk_count": chunk_count,
        },
        "sequence": {
            "authoring_name": name, "runtime_name": sequence, "chunk_number": number,
            "chunk_reference": reference, "chunk_byte_offset": offset,
            "chunk_payload_bytes": end - offset - 8, "chunk_end_byte_offset": end,
            "animation_flags": flags, "limb_count": limbs, "source_frame_count": count,
            "selected_frame_count": len(selected), "selected_source_frame_indices": selected,
        },
        "converter_replay": copy.deepcopy(recipe),
        "joints": [
            {"name": n, "fbplyr_skel_index": i, "source_limb_id": i + 1, "parent_index": parent}
            for i, (n, parent) in enumerate(wan.JOINTS)
        ],
        "root_translation": {
            "runtime_scale": wan.TRANSLATION_SCALE,
            "space": "source model XYZ before player placement/facing and animation-mode-specific anchoring",
        },
        "semantics": {
            "quaternion_order": "XYZW",
            "rotation_space": "parent-composed rotations about fbplyr_skel reference pivots",
            "precision": "normalized Python double conversion of binary32 WAN matrices, not lossy SEQ compression",
            "retained_record": "original converter global frames[authoring_index][limb_id-1]; not a target WAN matrix",
            "selection": "original A_SELECTED=0x4000, A_COMPRESSED=0x1; raw flags unchanged",
            "pivots": "original models/skeleton.h fbplyr_skel; no pivot or basis changes",
            "shipped_binary": "not verified; source-converter replay only",
        },
        "frames": frames,
        "record_layout": [{"id": kind, **record} for kind, record in records.items()],
        "validation": {
            "validated_source_frames": count, "validated_limb_frames": count * limbs,
            "target_authored_limb_frames": count * (limbs - 1),
            "retained_limb_frames": count,
            "retained_writer_validated_limb_frames": donor_clip["validation"]["validated_limb_frames"],
            "matrix_error_limit": 1e-4, "max_matrix_orthogonality_error": orthogonality,
            "max_matrix_determinant_error": determinant, "max_quaternion_norm_error": norm_error,
            "min_adjacent_source_quaternion_dot": min_dot,
        },
        "playback": {
            "status": "requires-source-playback-binding", "tick_hz": None,
            "frames_per_second": None, "frame_advance": None, "loop": None, "anchor_mode": None,
        },
    }
